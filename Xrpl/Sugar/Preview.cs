using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.BinaryCodec;
using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Utils;

namespace Xrpl.Sugar
{
    /// <summary>
    /// The result of running a transaction through <c>simulate</c>: what the node would do with it,
    /// without submitting it.
    /// </summary>
    /// <typeparam name="TOutcome">What is read from the simulated metadata.</typeparam>
    public sealed class TransactionPreview<TOutcome> where TOutcome : class
    {
        /// <summary>The autofilled transaction that was simulated; submit this one to get the same result.</summary>
        public ITransactionRequest Transaction { get; init; }

        /// <summary>The result code the transaction would get, for example <c>tesSUCCESS</c>.</summary>
        public string EngineResult { get; init; }

        /// <summary>The node's explanation of <see cref="EngineResult"/>.</summary>
        public string EngineResultMessage { get; init; }

        /// <summary>Whether the transaction would apply successfully.</summary>
        public bool WouldSucceed => string.Equals(EngineResult, "tesSUCCESS", StringComparison.Ordinal);

        /// <summary>The metadata the transaction would produce; null when the node returned none.</summary>
        public ITransactionMetadata Metadata { get; init; }

        /// <summary>What the transaction would do; null unless <see cref="WouldSucceed"/>.</summary>
        public TOutcome Outcome { get; init; }
    }

    /// <summary>
    /// Previews transactions through <c>simulate</c>. The node runs the transaction against the
    /// current open ledger and discards it, so the numbers are the ones it would apply - rounding,
    /// interest and fees included - as long as nothing else changes the ledger in between.
    /// </summary>
    /// <remarks>
    /// Each method autofills the transaction first, the same way <c>Autofill</c> does before a
    /// submission, and returns that transaction in <see cref="TransactionPreview{TOutcome}.Transaction"/>.
    /// The transaction must not be signed: <c>simulate</c> refuses signed transactions.
    /// </remarks>
    public static class PreviewSugar
    {
        /// <summary>Previews any transaction as the balance changes it would cause, per account.</summary>
        public static Task<TransactionPreview<Dictionary<string, List<Currency>>>> PreviewBalanceChanges(
            this IXrplClient client,
            ITransactionRequest transaction,
            CancellationToken cancellationToken = default) =>
            Preview(client, transaction, (_, metadata) => BalanceChanges.GetBalanceChanges(metadata), cancellationToken);

        /// <summary>Previews a <c>LoanPay</c>: how the payment would be split, and the loan it would leave.</summary>
        public static Task<TransactionPreview<LoanPaymentOutcome>> PreviewLoanPay(
            this IXrplClient client,
            LoanPay transaction,
            CancellationToken cancellationToken = default) =>
            Preview(client, transaction, (filled, metadata) => LoanPaymentOutcome.FromMetadata((ILoanPay)filled, metadata), cancellationToken);

        /// <summary>
        /// Previews a <c>LoanSet</c> before the borrower signs it: the exact terms the node would give
        /// the loan, and what the borrower would receive.
        /// </summary>
        /// <remarks>
        /// A <c>LoanSet</c> must carry a <c>CounterpartySignature</c>, which the borrower has not
        /// provided yet. <c>simulate</c> does not check a signature whose signing key is empty, so
        /// the preview sends the transaction with an empty <c>CounterpartySignature</c> as a
        /// binary blob. The returned <see cref="TransactionPreview{TOutcome}.Transaction"/> carries no
        /// signature of either party; sign it the usual way.
        /// </remarks>
        public static Task<TransactionPreview<LoanSetOutcome>> PreviewLoanSet(
            this IXrplClient client,
            LoanSet transaction,
            CancellationToken cancellationToken = default) =>
            Preview(
                client,
                transaction,
                (filled, metadata) => LoanSetOutcome.FromMetadata(filled, metadata),
                cancellationToken,
                filled => new SimulateRequest { TxBlob = UnsignedLoanSetBlob(filled) });

        /// <summary>Previews a <c>VaultDeposit</c>: the shares it would mint.</summary>
        public static Task<TransactionPreview<VaultOutcome>> PreviewVaultDeposit(
            this IXrplClient client,
            VaultDeposit transaction,
            CancellationToken cancellationToken = default) =>
            Preview(client, transaction, (filled, metadata) => VaultOutcome.FromMetadata(filled, metadata), cancellationToken);

        /// <summary>Previews a <c>VaultWithdraw</c>: the assets it would pay out and the shares it would burn.</summary>
        public static Task<TransactionPreview<VaultOutcome>> PreviewVaultWithdraw(
            this IXrplClient client,
            VaultWithdraw transaction,
            CancellationToken cancellationToken = default) =>
            Preview(client, transaction, (filled, metadata) => VaultOutcome.FromMetadata(filled, metadata), cancellationToken);

        /// <summary>Previews an <c>AMMDeposit</c>: the assets it would take and the LP tokens it would issue.</summary>
        public static Task<TransactionPreview<AmmOutcome>> PreviewAMMDeposit(
            this IXrplClient client,
            AMMDeposit transaction,
            CancellationToken cancellationToken = default) =>
            Preview(client, transaction, (filled, metadata) => AmmOutcome.FromMetadata((IAMMDeposit)filled, metadata), cancellationToken);

        /// <summary>Previews an <c>AMMWithdraw</c>: the assets it would return and the LP tokens it would redeem.</summary>
        public static Task<TransactionPreview<AmmOutcome>> PreviewAMMWithdraw(
            this IXrplClient client,
            AMMWithdraw transaction,
            CancellationToken cancellationToken = default) =>
            Preview(client, transaction, (filled, metadata) => AmmOutcome.FromMetadata((IAMMWithdraw)filled, metadata), cancellationToken);

        private static string UnsignedLoanSetBlob(LoanSet transaction)
        {
            Dictionary<string, object> json = transaction.ToDictionary();
            json["SigningPubKey"] = string.Empty;
            json["CounterpartySignature"] = new Dictionary<string, object> { ["SigningPubKey"] = string.Empty };
            json.Remove("TxnSignature");
            return XrplBinaryCodec.Encode(json);
        }

        private static async Task<TransactionPreview<TOutcome>> Preview<TTransaction, TOutcome>(
            IXrplClient client,
            TTransaction transaction,
            Func<TTransaction, ITransactionMetadata, TOutcome> read,
            CancellationToken cancellationToken,
            Func<TTransaction, SimulateRequest> buildRequest = null)
            where TTransaction : ITransactionRequest
            where TOutcome : class
        {
            if (client == null)
                throw new ArgumentNullException(nameof(client));
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));

            TTransaction filled = await client.Autofill(transaction, cancellationToken: cancellationToken).ConfigureAwait(false);
            SimulateRequest request = buildRequest?.Invoke(filled) ?? new SimulateRequest { Transaction = filled };
            SimulateResponse response = await client
                .Simulate(request, cancellationToken)
                .Typed()
                .ConfigureAwait(false);

            bool succeeded = string.Equals(response.EngineResult, "tesSUCCESS", StringComparison.Ordinal);
            return new TransactionPreview<TOutcome>
            {
                Transaction = filled,
                EngineResult = response.EngineResult,
                EngineResultMessage = response.EngineResultMessage,
                Metadata = response.Meta,
                Outcome = succeeded && response.Meta != null ? read(filled, response.Meta) : null,
            };
        }
    }
}
