using NBitcoin.Protocol;

using Newtonsoft.Json;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Subscriptions;

using TimeoutException = Xrpl.Client.Exceptions.TimeoutException;
using Timer = System.Timers.Timer;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/client/RequestManager.ts

namespace Xrpl.Client
{
    /// <summary>
    /// Manage all the requests made to the websocket, and their async responses
    /// that come in from the WebSocket.Responses come in over the WS connection
    /// after-the-fact, so this manager will tie that response to resolve the
    /// original request.
    /// </summary>
    public class RequestManager
    {

        public class XrplRequest
        {
            public Guid Id { get; set; }
            public string Message { get; set; }
            public Task<Dictionary<string, object>> Promise { get; set; }
        }

        public class XrplGRequest
        {
            public Guid Id { get; set; }
            public string Message { get; set; }
            public Task<object> Promise { get; set; }
        }

        private Guid nextId = Guid.NewGuid();
        private readonly ConcurrentDictionary<Guid, Timer> timeoutsAwaitingResponse = new ConcurrentDictionary<Guid, Timer>();
        private readonly ConcurrentDictionary<Guid, TaskInfo> promisesAwaitingResponse = new ConcurrentDictionary<Guid, TaskInfo>();
        private readonly JsonSerializerSettings serializerSettings;

        public RequestManager()
        {
            serializerSettings = new JsonSerializerSettings();
            serializerSettings.NullValueHandling = NullValueHandling.Ignore;
            serializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Utc;
        }

        /// <summary>
        /// </summary>
        public void Resolve(Guid id, BaseResponse response)
        {
            if (!promisesAwaitingResponse.TryGetValue(id, out var taskInfo) || taskInfo == null)
            {
                Debug.WriteLine($"Resolve called for non-existent promise {id} (likely already cancelled/timed out)");
                return;
            }

            if (timeoutsAwaitingResponse.TryRemove(id, out var timer))
                timer.Stop();

            try
            {
                var deserialized = JsonConvert.DeserializeObject($"{response.Result}", taskInfo.Type, serializerSettings);
                var setResult = taskInfo.TaskCompletionResult.GetType().GetMethod("TrySetResult");
                setResult.Invoke(taskInfo.TaskCompletionResult, new[] { deserialized });
                this.DeletePromise(id, taskInfo);
            }
            catch (Exception ex)
            {
                var error = new XrplException($"Failed to deserialize response for request {id}: {ex.Message}", ex);
                this.Reject(id, error);
                throw;  // re-throw so IOnMessageFastPath also logs via OnError
            }
        }

        /// <summary>
        /// Rejects a pending request with the specified exception.
        /// Safe to call even if the promise no longer exists (e.g., already resolved).
        /// The exception is automatically "observed" to prevent UnobservedTaskException 
        /// from being raised in consuming applications like DaddyWallet.
        /// </summary>
        public void Reject<T>(Guid id, T error) where T : Exception
        {
            if (!promisesAwaitingResponse.TryGetValue(id, out var taskInfo) || taskInfo == null)
            {
                Debug.WriteLine($"Reject called for non-existent promise {id} (likely already resolved)");
                return;
            }
            if (timeoutsAwaitingResponse.TryRemove(id, out var timer))
                timer.Stop();
            var setException = taskInfo.TaskCompletionResult.GetType().GetMethod("TrySetException", new Type[] { typeof(Exception) }, null);
            setException.Invoke(taskInfo.TaskCompletionResult, new[] { error });
            
            // Observe the exception to prevent UnobservedTaskException in consuming apps
            // This is critical for MAUI/mobile apps that have global exception handlers
            ObserveTaskException(taskInfo.TaskCompletionResult);
            
            this.DeletePromise(id, taskInfo);
        }
        
        /// <summary>
        /// Observes the exception on a TaskCompletionSource's Task to prevent UnobservedTaskException.
        /// When a Task faults but is never awaited, .NET raises UnobservedTaskException event.
        /// By adding a ContinueWith that reads the exception, we mark it as "observed".
        /// </summary>
        private void ObserveTaskException(object taskCompletionSource)
        {
            try
            {
                // Get the Task property from TaskCompletionSource<T>
                var taskProperty = taskCompletionSource.GetType().GetProperty("Task");
                if (taskProperty == null) return;
                
                var task = taskProperty.GetValue(taskCompletionSource) as Task;
                if (task == null) return;
                
                // Add a continuation that observes the exception (reads it to mark as handled)
                // This prevents UnobservedTaskException from being raised
                task.ContinueWith(t => 
                {
                    // Reading t.Exception marks it as observed
                    _ = t.Exception;
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch
            {
                // Ignore any reflection errors - this is a best-effort operation
            }
        }

        /// <summary>
        /// Rejects all pending requests with the specified exception.
        /// </summary>
        public void RejectAll(Exception error)
        {
            foreach (var id in this.promisesAwaitingResponse.Keys)
            {
                this.Reject(id, error);
            }
        }

        /// <summary>
        /// Rejects all pending requests with OperationCanceledException.
        /// Used for intentional disconnects to avoid logging as Critical errors.
        /// </summary>
        public void RejectAllWithCancellation()
        {
            var cancellationError = new OperationCanceledException("Connection was intentionally closed.");
            foreach (var id in this.promisesAwaitingResponse.Keys)
            {
                this.Reject(id, cancellationError);
            }
        }

        public XrplGRequest CreateGRequest<T, R>(R request, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (timeout != System.Threading.Timeout.InfiniteTimeSpan && timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), 
                    $"Timeout must be positive or Timeout.InfiniteTimeSpan, but was {timeout.TotalSeconds:F1}s");
            }

            Guid newId;
            var info = request.GetType().GetProperty("Id");
            if (info.GetValue(request) == null)
            {
                newId = this.nextId;
                this.nextId = Guid.NewGuid();
            }
            else
            {
                newId = (Guid)info.GetValue(request);
            }

            info.SetValue(request, newId, null);

            string newRequest = JsonConvert.SerializeObject(request, serializerSettings);

            if (this.promisesAwaitingResponse.ContainsKey(newId))
            {
                throw new XrplException($"Response with id '${newId}' is already pending");
            }

            TaskCompletionSource<object> task = new TaskCompletionSource<object>();
            TaskInfo taskInfo = new TaskInfo();
            taskInfo.TaskId = newId;
            taskInfo.TaskCompletionResult = task;
            taskInfo.RemoveUponCompletion = true;
            taskInfo.Type = typeof(T);

            promisesAwaitingResponse.TryAdd(newId, taskInfo);

            if (cancellationToken.CanBeCanceled)
            {
                CancellationTokenRegistration registration = cancellationToken.Register(() =>
                {
                    try
                    {
                        this.Reject(newId, new OperationCanceledException("Request was cancelled"));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"CancellationToken callback error: {ex.Message}");
                    }
                });
                taskInfo.CancellationRegistration = registration;
            }

            if (timeout != System.Threading.Timeout.InfiniteTimeSpan)
            {
                Timer timer = new Timer(timeout.TotalMilliseconds);
                timer.AutoReset = false;
                timer.Elapsed += (sender, e) =>
                {
                    try
                    {
                        this.Reject(newId, new TimeoutException($"Timeout for request: {newRequest} with id {newId}", request));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Timer.Elapsed Reject error (already resolved?): {ex.Message}");
                    }
                };
                timer.Start();
                timeoutsAwaitingResponse.TryAdd(newId, timer);
            }

            return new XrplGRequest()
            {
                Id = newId,
                Message = newRequest,
                Promise = task.Task
            };
        }

        /// <summary>
        /// </summary>
        public XrplRequest CreateRequest(Dictionary<string, object> request, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (timeout != System.Threading.Timeout.InfiniteTimeSpan && timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), 
                    $"Timeout must be positive or Timeout.InfiniteTimeSpan, but was {timeout.TotalSeconds:F1}s");
            }

            Guid newId;
            var hasId = request.TryGetValue("id", out var id);
            if (!hasId)
            {
                newId = this.nextId;
                this.nextId = Guid.NewGuid();
            }
            else
            {
                newId = (Guid)id;
            }

            request["id"] = newId;

            string newRequest = JsonConvert.SerializeObject(request, serializerSettings);

            if (this.promisesAwaitingResponse.ContainsKey(newId))
            {
                throw new XrplException($"Response with id '${newId}' is already pending");
            }

            TaskCompletionSource<Dictionary<string, object>> task = new TaskCompletionSource<Dictionary<string, object>>();
            TaskInfo taskInfo = new TaskInfo();
            taskInfo.TaskId = newId;
            taskInfo.TaskCompletionResult = task;
            taskInfo.RemoveUponCompletion = true;
            taskInfo.Type = typeof(Dictionary<string, object>);

            promisesAwaitingResponse.TryAdd(newId, taskInfo);

            if (cancellationToken.CanBeCanceled)
            {
                CancellationTokenRegistration registration = cancellationToken.Register(() =>
                {
                    try
                    {
                        this.Reject(newId, new OperationCanceledException("Request was cancelled"));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"CancellationToken callback error: {ex.Message}");
                    }
                });
                taskInfo.CancellationRegistration = registration;
            }

            if (timeout != System.Threading.Timeout.InfiniteTimeSpan)
            {
                Timer timer = new Timer(timeout.TotalMilliseconds);
                timer.AutoReset = false;
                timer.Elapsed += (sender, e) =>
                {
                    try
                    {
                        this.Reject(newId, new TimeoutException($"Timeout for request: {newRequest} with id {newId}", request));
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Timer.Elapsed Reject error (already resolved?): {ex.Message}");
                    }
                };
                timer.Start();
                timeoutsAwaitingResponse.TryAdd(newId, timer);
            }

            return new XrplRequest()
            {
                Id = newId,
                Message = newRequest,
                Promise = task.Task
            };
        }

        /// <summary>
        /// Handles an incoming response message. Returns a tuple of (response, handled).<br/>
        /// handled=true means the message was matched to a pending request (resolved or rejected).<br/>
        /// handled=false means the id was not found among pending requests — the caller
        /// should treat the message as a stream/follow-up (e.g. path_find async updates).
        /// </summary>
        public (BaseResponse Response, bool Handled) HandleResponse(string message)
        {
            var response = JsonConvert.DeserializeObject<ErrorResponse>(message);

            if (response.Id == null)
            {
                return (response, false);
            }

            if(!Guid.TryParse($"{response.Id}", out var id))
            {
                return (response, false);
            }

            if (!promisesAwaitingResponse.ContainsKey(id))
            {
                return (response, false);
            }

            if (response.Status == null)
            {
                if (response.Error is not null || response.ErrorMessage is not null)
                {
                    var errMessage = response.Error is null
                        ? response.ErrorMessage
                        : $"{response.Error} - {response.ErrorMessage}";
                    XrplException error = new XrplException(errMessage);
                    this.Reject(id, error);
                    return (response, true);
                }

                ResponseFormatException responseError = new ResponseFormatException("Response has no status");
                this.Reject(id, responseError);
                return (response, true);
            }

            if (response.Status == "error" )
            {
                ErrorResponse errorResponse = null;
                try
                {
                    errorResponse = JsonConvert.DeserializeObject<ErrorResponse>(message);

                }
                catch (Exception e)
                {
                    
                }
                var errMessage = response.Error is null
                    ? response.ErrorMessage
                    : $"{response.Error} - {response.ErrorMessage}";
                var error = new RippledException(errMessage, errorResponse);
                this.Reject(id, error);
                return (response, true);
            }

            if (response.Status != "success")
            {
                XrplException error = new XrplException($"unrecognized response.status: ${response.Status ?? ""}");
                this.Reject(id, error);
                return (response, true);
            }

            this.Resolve(id, response);
            return (response, true);
        }

        /// <summary>
        /// </summary>
        public void DeletePromise(Guid id, TaskInfo taskInfo)
        {
            this.promisesAwaitingResponse.TryRemove(id, out _);
            if (taskInfo.CancellationRegistration is { } reg)
            {
                _ = reg.DisposeAsync();
            }
        }
    }
}