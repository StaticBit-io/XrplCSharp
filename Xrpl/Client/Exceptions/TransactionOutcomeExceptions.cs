using System;

namespace Xrpl.Client.Exceptions
{
    /// <summary>
    /// A submitted transaction will never be applied: its <c>LastLedgerSequence</c> has passed and
    /// it is in none of the ledgers it could have reached.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reported only when the server held every ledger from <see cref="MinLedger"/> to
    /// <see cref="LastLedgerSequence"/>, validated, and found the transaction in none of them. The
    /// same transaction can no longer be applied, so a new one can be submitted in its place.
    /// </para>
    /// <para>
    /// <see cref="SearchedAll"/> tells how that was established. <c>true</c>: the lookup itself
    /// answered <c>searched_all</c>. <c>false</c>: rippled gives <c>searched_all</c> only for ranges
    /// where every ledger holds a transaction, which a quiet network does not have, so each ledger
    /// of the range was read validated and none of them lists the transaction. Either is
    /// conclusive.
    /// </para>
    /// <para>
    /// Not a <see cref="ValidationException"/>: that one says the transaction was refused before it
    /// was sent. This one says it was sent and expired.
    /// </para>
    /// </remarks>
    public class TransactionExpiredException : XrplException
    {
        /// <summary>The transaction's hash.</summary>
        public string Hash { get; }

        /// <summary>The transaction's <c>LastLedgerSequence</c>.</summary>
        public uint LastLedgerSequence { get; }

        /// <summary>The first ledger searched: the one validated before the transaction was submitted.</summary>
        public uint MinLedger { get; }

        /// <summary>
        /// The node's provisional answer to the submission, such as <c>tefPAST_SEQ</c> or
        /// <c>terPRE_SEQ</c>; <c>null</c> when the transaction was only being tracked.
        /// </summary>
        public string PreliminaryResult { get; }

        /// <summary>
        /// Whether the expiry was proven by the lookup's own <c>searched_all</c>, rather than by
        /// reading each ledger of the range.
        /// </summary>
        public bool SearchedAll { get; }

        /// <param name="message">What expired, and the range searched.</param>
        /// <param name="hash">The transaction's hash.</param>
        /// <param name="lastLedgerSequence">The transaction's <c>LastLedgerSequence</c>.</param>
        /// <param name="minLedger">The first ledger searched.</param>
        /// <param name="preliminaryResult">The provisional answer to the submission, if any.</param>
        /// <param name="searchedAll">Whether the lookup's own <c>searched_all</c> proved it.</param>
        public TransactionExpiredException(string message, string hash, uint lastLedgerSequence, uint minLedger, string preliminaryResult, bool searchedAll)
            : base(message)
        {
            Hash = hash;
            LastLedgerSequence = lastLedgerSequence;
            MinLedger = minLedger;
            PreliminaryResult = preliminaryResult;
            SearchedAll = searchedAll;
        }
    }

    /// <summary>
    /// A transaction may have been submitted, and whether it will be applied is not known.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raised when submitting or waiting failed after the transaction could have reached the
    /// network: the submit request failed other than by an error answer from the node, a lookup
    /// failed while waiting, or <see cref="LastLedgerSequence"/> passed while the server lacked part
    /// of the ledgers to search. The cause is in <see cref="Exception.InnerException"/> when there is one.
    /// </para>
    /// <para>
    /// The transaction may still be in a ledger. Submitting a different transaction for the same
    /// purpose can apply both; resubmitting this same signed transaction cannot. To get a final
    /// answer, call <c>WaitForTransactionOutcome</c> with <see cref="Hash"/>,
    /// <see cref="LastLedgerSequence"/> and <see cref="MinLedger"/>, against a server that holds
    /// those ledgers.
    /// </para>
    /// </remarks>
    public class TransactionOutcomeUnknownException : XrplException
    {
        /// <summary>The transaction's hash.</summary>
        public string Hash { get; }

        /// <summary>The transaction's <c>LastLedgerSequence</c>.</summary>
        public uint LastLedgerSequence { get; }

        /// <summary>The first ledger the transaction could be in: the one validated before it was submitted.</summary>
        public uint MinLedger { get; }

        /// <summary>The node's provisional answer to the submission, when one arrived.</summary>
        public string PreliminaryResult { get; }

        /// <param name="message">What could not be told, and why.</param>
        /// <param name="hash">The transaction's hash.</param>
        /// <param name="lastLedgerSequence">The transaction's <c>LastLedgerSequence</c>.</param>
        /// <param name="minLedger">The first ledger the transaction could be in.</param>
        /// <param name="preliminaryResult">The provisional answer to the submission, if any.</param>
        /// <param name="innerException">The failure that left the outcome unknown, if one did.</param>
        public TransactionOutcomeUnknownException(
            string message,
            string hash,
            uint lastLedgerSequence,
            uint minLedger,
            string preliminaryResult,
            Exception innerException = null)
            : base(message, innerException)
        {
            Hash = hash;
            LastLedgerSequence = lastLedgerSequence;
            MinLedger = minLedger;
            PreliminaryResult = preliminaryResult;
        }
    }
}
