using System;
using System.Threading;
using System.Threading.Tasks;

namespace Xrpl.Client
{
    public class TaskInfo
    {
        public Guid TaskId { get; set; }

        public Type Type { get; set; }

        public object TaskCompletionResult { get; set; }

        /// <summary>
        /// Completes <see cref="TaskCompletionResult"/> with a deserialized result. Set when the
        /// request is created, so the response path does not have to reach for the strongly typed
        /// TrySetResult through reflection. Null for externally built instances.
        /// </summary>
        public Func<object, bool> SetResult { get; set; }

        /// <summary>
        /// Faults <see cref="TaskCompletionResult"/>. Set when the request is created; null for
        /// externally built instances.
        /// </summary>
        public Func<Exception, bool> SetException { get; set; }

        /// <summary>
        /// The task behind <see cref="TaskCompletionResult"/>, used to observe faults without
        /// reflection. Null for externally built instances.
        /// </summary>
        public Task CompletionTask { get; set; }

        public bool RemoveUponCompletion { get; set; }

        public CancellationTokenRegistration? CancellationRegistration { get; set; }

        public TaskInfo()
        {
            RemoveUponCompletion = true;
        }
    }
}
