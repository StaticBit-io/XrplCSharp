using System;

using Xrpl.Models.Methods;

namespace Xrpl.Client.Exceptions
{
    /// <summary>
    /// A transaction was submitted and did not succeed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The result code decides what a caller should do next, and the classes differ completely:
    /// <c>tem</c> means the request is malformed and must be fixed before it is sent again,
    /// <c>tec</c> means it was applied to a ledger and the fee was taken, <c>ter</c> means it may
    /// work later. Telling them apart used to mean reading the exception's message, which works
    /// until the first transaction whose message contains the substring somewhere else.
    /// </para>
    /// <para>
    /// This derives from <see cref="RippleException"/> and keeps the message it always had, so a
    /// <c>catch (RippleException)</c> and anything matching on the text carry on unchanged. What is
    /// new is beside the message rather than inside it.
    /// </para>
    /// </remarks>
    public class TransactionFailedException : RippleException
    {
        /// <summary>
        /// The result code, as the node reported it - <c>tecINSUFFICIENT_PAYMENT</c>,
        /// <c>temBAD_FEE</c> and so on.
        /// </summary>
        public string EngineResult { get; }

        /// <summary>
        /// The transaction's hash.
        /// </summary>
        /// <remarks>
        /// Worth having even when the transaction never reached a ledger, but it is the
        /// <see cref="ReachedLedger"/> case where it matters: there is something to look up, and
        /// showing it is usually the first thing anyone wants to do after a refusal.
        /// </remarks>
        public string Hash { get; }

        /// <summary>
        /// The validated transaction, metadata included - or <c>null</c> when there is none to hand
        /// back yet.
        /// </summary>
        /// <remarks>
        /// Absent for a failure the node refused before a ledger. A <c>tec</c> is reported once it
        /// is validated, so it carries one. Use <see cref="ReachedLedger"/> to tell whether the fee
        /// was taken; <see cref="Hash"/> is there in every case.
        /// </remarks>
        public TransactionSummary Result { get; }

        /// <summary>
        /// Whether the transaction was applied to a ledger - the fee taken, the transaction there
        /// to be looked up.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The difference is not a detail: applied means the fee is gone and there is something to
        /// show, while a refusal before that costs nothing and leaves nothing. The two arrive as
        /// the same kind of failure and are not the same event.
        /// </para>
        /// <para>
        /// Read from the result code, so it holds for an exception built from a code alone: a
        /// <c>tec</c> means applied whether or not <see cref="Result"/> is here. Reliable submission
        /// reports a <c>tec</c> once validated, with its metadata, and the hash is present in every
        /// case - the hash is what an explorer needs.
        /// </para>
        /// </remarks>
        public bool ReachedLedger =>
            Result is not null ||
            (EngineResult is not null && EngineResult.StartsWith("tec", StringComparison.Ordinal));

        /// <param name="message">The message this exception has always carried, unchanged.</param>
        /// <param name="engineResult">The node's result code.</param>
        /// <param name="hash">The transaction's hash.</param>
        /// <param name="result">The validated transaction, or <c>null</c> if there is none.</param>
        public TransactionFailedException(string message, string engineResult, string hash, TransactionSummary result = null)
            : base(message)
        {
            EngineResult = engineResult;
            Hash = hash;
            Result = result;
        }
    }
}
