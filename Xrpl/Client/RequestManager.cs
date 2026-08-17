using NBitcoin.Protocol;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Models.Subscriptions;

using TimeoutException = Xrpl.Client.Exceptions.TimeoutException;
using Timer = System.Timers.Timer;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/client/RequestManager.ts

namespace Xrpl.Client
{
    /// <summary>
    /// rippled's <c>admin_user</c>/<c>admin_password</c> port-stanza credentials. They are carried
    /// inside the JSON body of each request, which is the only mechanism rippled accepts for
    /// admin commands over ws/wss.
    /// </summary>
    public sealed record AdminCredentials(string User, string Password);

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
            public Task<object> Promise { get; set; }
        }

        public class XrplGRequest
        {
            public Guid Id { get; set; }
            public string Message { get; set; }
            public Task<object> Promise { get; set; }
        }

        private readonly ConcurrentDictionary<Guid, Timer> timeoutsAwaitingResponse = new ConcurrentDictionary<Guid, Timer>();
        private readonly ConcurrentDictionary<Guid, TaskInfo> promisesAwaitingResponse = new ConcurrentDictionary<Guid, TaskInfo>();
        private readonly JsonSerializerOptions serializerOptions = XrplJsonOptions.Default;

        /// <summary>
        /// Stands in for a missing <c>result</c>, matching what deserializing the literal
        /// <c>"{}"</c> used to produce.
        /// </summary>
        private static ReadOnlySpan<byte> EmptyResult => "{}"u8;

        public RequestManager()
        {
        }

        /// <summary>
        /// </summary>
        public void Resolve(Guid id, BaseResponse response)
        {
            if (!promisesAwaitingResponse.TryGetValue(id, out var taskInfo) || taskInfo == null)
            {
                Debug.WriteLine($"Resolve called for non-existent promise {id} (likely already cancelled/timed out)");
                DisposeTimeout(id);
                return;
            }

            DisposeTimeout(id);

            try
            {
                object deserialized = DeserializeResult(response.RawResult, taskInfo.Type);
                CompleteWithResult(taskInfo, new ResolvedResponse(deserialized, response));
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
        /// Converts the <c>result</c> member of a response into the type the request was created
        /// with, parsing it straight out of the frame.
        /// </summary>
        /// <remarks>
        /// The member is not parsed before this point: the envelope only recorded where it sits.
        /// That leaves exactly one parse of the response body, against the UTF-8 the node sent,
        /// with no intermediate document and no pooled array left unreturned.
        /// </remarks>
        private object DeserializeResult(RawJson raw, Type type)
        {
            ReadOnlySpan<byte> json = raw.IsEmpty ? EmptyResult : raw.Span;

            // An explicit `"result": null` arrives as a four-byte literal; it used to reach the
            // requested type as an empty object rather than null, and callers rely on that.
            if (json.SequenceEqual("null"u8))
            {
                json = EmptyResult;
            }

            return JsonSerializer.Deserialize(json, type, serializerOptions);
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

                // A timer registered after its request had already finished has no other chance of
                // being cleaned up: this is the Elapsed callback of exactly such a timer.
                DisposeTimeout(id);
                return;
            }

            DisposeTimeout(id);
            CompleteWithException(taskInfo, error);

            // Observe the exception to prevent UnobservedTaskException in consuming apps
            // This is critical for MAUI/mobile apps that have global exception handlers
            ObserveTaskException(taskInfo);
            
            this.DeletePromise(id, taskInfo);
        }
        
        /// <summary>
        /// Removes the timeout timer of <paramref name="id"/> and disposes it.
        /// Dispose, not Stop: <see cref="Timer"/> is a finalizable Component, and a stopped but
        /// undisposed one per request piles up on the finalization queue over a long paged run.
        /// </summary>
        private void DisposeTimeout(Guid id)
        {
            if (timeoutsAwaitingResponse.TryRemove(id, out Timer timer))
            {
                timer.Dispose();
            }
        }

        /// <summary>
        /// Completes the pending request with a deserialized result, using the typed delegate
        /// captured when the request was created and falling back to reflection for
        /// <see cref="TaskInfo"/> instances built outside this manager.
        /// </summary>
        private static void CompleteWithResult(TaskInfo taskInfo, object result)
        {
            if (taskInfo.SetResult is not null)
            {
                taskInfo.SetResult(result);
                return;
            }

            MethodInfo setResult = taskInfo.TaskCompletionResult.GetType().GetMethod("TrySetResult");
            setResult.Invoke(taskInfo.TaskCompletionResult, new[] { result });
        }

        /// <summary>
        /// Faults the pending request. See <see cref="CompleteWithResult"/> for the fallback rules.
        /// </summary>
        private static void CompleteWithException(TaskInfo taskInfo, Exception error)
        {
            if (taskInfo.SetException is not null)
            {
                taskInfo.SetException(error);
                return;
            }

            MethodInfo setException = taskInfo.TaskCompletionResult.GetType()
                .GetMethod("TrySetException", new Type[] { typeof(Exception) }, null);
            setException.Invoke(taskInfo.TaskCompletionResult, new object[] { error });
        }

        /// <summary>
        /// Observes the exception on a TaskCompletionSource's Task to prevent UnobservedTaskException.
        /// When a Task faults but is never awaited, .NET raises UnobservedTaskException event.
        /// By adding a ContinueWith that reads the exception, we mark it as "observed".
        /// </summary>
        private void ObserveTaskException(TaskInfo taskInfo)
        {
            try
            {
                Task task = taskInfo.CompletionTask;
                if (task == null)
                {
                    // Externally built TaskInfo: fall back to reading the Task property reflectively.
                    PropertyInfo taskProperty = taskInfo.TaskCompletionResult.GetType().GetProperty("Task");
                    if (taskProperty == null) return;

                    task = taskProperty.GetValue(taskInfo.TaskCompletionResult) as Task;
                }

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

        /// <summary>
        /// Adds rippled's admin credentials to an already serialized request.
        /// </summary>
        /// <remarks>
        /// Applied to the serialized JSON rather than to the request object so that the credentials
        /// never end up in the request instance that error messages are built from.
        /// </remarks>
        private static string ApplyAdminCredentials(string json, AdminCredentials? credentials)
        {
            if (credentials is null)
            {
                return json;
            }

            if (JsonNode.Parse(json) is not JsonObject request)
            {
                return json;
            }

            request["admin_user"] = credentials.User;
            request["admin_password"] = credentials.Password;
            return request.ToJsonString();
        }

        public XrplGRequest CreateGRequest<T, R>(
            R request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            AdminCredentials? adminCredentials = null)
        {
            if (timeout != System.Threading.Timeout.InfiniteTimeSpan && timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), 
                    $"Timeout must be positive or Timeout.InfiniteTimeSpan, but was {timeout.TotalSeconds:F1}s");
            }

            var info = request.GetType().GetProperty("Id");
            object existingId = info.GetValue(request);
            Guid newId = existingId == null ? Guid.NewGuid() : (Guid)existingId;

            info.SetValue(request, newId, null);

            string newRequest = JsonSerializer.Serialize(request, serializerOptions);
            string outgoingRequest = ApplyAdminCredentials(newRequest, adminCredentials);

            TaskCompletionSource<object> task = new TaskCompletionSource<object>();
            TaskInfo taskInfo = new TaskInfo();
            taskInfo.TaskId = newId;
            taskInfo.TaskCompletionResult = task;
            taskInfo.SetResult = result => task.TrySetResult(result);
            taskInfo.SetException = error => task.TrySetException(error);
            taskInfo.CompletionTask = task.Task;
            taskInfo.RemoveUponCompletion = true;
            taskInfo.Type = typeof(T);

            if (!promisesAwaitingResponse.TryAdd(newId, taskInfo))
            {
                throw new XrplException($"Response with id '${newId}' is already pending");
            }

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

                // An already cancelled token runs its callback inline, so Reject completed and
                // removed the promise before the registration was stored — nothing would ever
                // dispose it.
                if (!promisesAwaitingResponse.ContainsKey(newId))
                {
                    _ = registration.DisposeAsync();
                }
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

                // Same inline-cancellation case: the request may already be finished, and the
                // Reject that finished it ran before this timer existed, so it had nothing to
                // remove. Whatever is registered under this id now belongs to a request that is
                // already gone.
                if (!promisesAwaitingResponse.ContainsKey(newId))
                {
                    DisposeTimeout(newId);
                }
            }

            return new XrplGRequest()
            {
                Id = newId,
                Message = outgoingRequest,
                Promise = task.Task
            };
        }

        /// <summary>
        /// </summary>
        public XrplRequest CreateRequest(
            Dictionary<string, object> request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            AdminCredentials? adminCredentials = null)
        {
            if (timeout != System.Threading.Timeout.InfiniteTimeSpan && timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), 
                    $"Timeout must be positive or Timeout.InfiniteTimeSpan, but was {timeout.TotalSeconds:F1}s");
            }

            var hasId = request.TryGetValue("id", out var id);
            Guid newId = hasId ? (Guid)id : Guid.NewGuid();

            request["id"] = newId;

            string newRequest = JsonSerializer.Serialize(request, serializerOptions);
            string outgoingRequest = ApplyAdminCredentials(newRequest, adminCredentials);

            TaskCompletionSource<object> task = new TaskCompletionSource<object>();
            TaskInfo taskInfo = new TaskInfo();
            taskInfo.TaskId = newId;
            taskInfo.TaskCompletionResult = task;
            taskInfo.SetResult = result => task.TrySetResult(result);
            taskInfo.SetException = error => task.TrySetException(error);
            taskInfo.CompletionTask = task.Task;
            taskInfo.RemoveUponCompletion = true;
            taskInfo.Type = typeof(Dictionary<string, object>);

            if (!promisesAwaitingResponse.TryAdd(newId, taskInfo))
            {
                throw new XrplException($"Response with id '${newId}' is already pending");
            }

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

                // An already cancelled token runs its callback inline, so Reject completed and
                // removed the promise before the registration was stored — nothing would ever
                // dispose it.
                if (!promisesAwaitingResponse.ContainsKey(newId))
                {
                    _ = registration.DisposeAsync();
                }
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

                // Same inline-cancellation case: the request may already be finished, and the
                // Reject that finished it ran before this timer existed, so it had nothing to
                // remove. Whatever is registered under this id now belongs to a request that is
                // already gone.
                if (!promisesAwaitingResponse.ContainsKey(newId))
                {
                    DisposeTimeout(newId);
                }
            }

            return new XrplRequest()
            {
                Id = newId,
                Message = outgoingRequest,
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
            return HandleResponse(Encoding.UTF8.GetBytes(message));
        }

        /// <summary>
        /// Handles a message still in its wire form. This is the socket path.
        /// </summary>
        /// <remarks>
        /// The frame is kept rather than sliced away: the envelope records where <c>result</c> sits
        /// inside it, and both the typed deserialization and <see cref="BaseResponse.RawResult"/>
        /// are cut from those bounds. The array is the exact-sized one the receive loop already
        /// allocated, so keeping it costs nothing over what was allocated anyway.
        /// </remarks>
        /// <param name="frame">
        /// The message bytes. Ownership passes to the returned response: it keeps the array and cuts
        /// <see cref="BaseResponse.RawResult"/> from it, so the caller must not reuse or mutate it —
        /// a pooled or ring buffer will silently rewrite a response that was already handed out.
        /// </param>
        public (BaseResponse Response, bool Handled) HandleResponse(byte[] frame)
        {
            ErrorResponse response = JsonSerializer.Deserialize<ErrorResponse>(frame, serializerOptions);
            response.AttachFrame(frame);
            return HandleResponse(response);
        }

        private (BaseResponse Response, bool Handled) HandleResponse(ErrorResponse response)
        {
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
                if (response.Error is not null || response.ErrorMessage is not null || response.ErrorException is not null)
                {
                    string detail = response.ErrorMessage ?? response.ErrorException ?? "Unknown error";
                    var errMessage = response.Error is null
                        ? detail
                        : $"{response.Error} - {detail}";
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
                // The message was already deserialized into an ErrorResponse above, so the error
                // details are in hand - parsing it a second time only produced an equal copy.
                string detail = response.ErrorMessage ?? response.ErrorException;
                var errMessage = response.Error is null
                    ? detail
                    : $"{response.Error} - {detail}";
                var error = new RippledException(errMessage, response);
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