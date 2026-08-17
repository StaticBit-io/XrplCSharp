using Xrpl.Models.Subscriptions;

namespace Xrpl.Client
{
    /// <summary>
    /// What a resolved request puts into its promise: the typed result and the envelope it came
    /// from, together.
    /// </summary>
    /// <remarks>
    /// The promise is <c>Task&lt;object&gt;</c> and <see cref="RequestManager"/> knows the target
    /// type only as a <see cref="System.Type"/>, so it cannot build a
    /// <see cref="XrplResponse{T}"/> itself. It carries both halves this far and the generic
    /// client assembles them, which keeps the manager free of the generic parameter.
    /// </remarks>
    internal sealed class ResolvedResponse
    {
        public ResolvedResponse(object result, BaseResponse envelope)
        {
            Result = result;
            Envelope = envelope;
        }

        public object Result { get; }

        public BaseResponse Envelope { get; }
    }
}
