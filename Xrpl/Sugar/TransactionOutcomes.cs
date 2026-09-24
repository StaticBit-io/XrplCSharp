using System;
using System.Collections.Generic;
using System.Linq;

using Xrpl.BinaryCodec.Numbers;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Transactions;
using Xrpl.Utils;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Sugar
{
    /// <summary>
    /// What a <c>LoanPay</c> did, read from its metadata - of a validated transaction, or of a
    /// <c>simulate</c> run (<see cref="PreviewSugar.PreviewLoanPay"/>).
    /// </summary>
    /// <remarks>
    /// Amounts are in the loan asset's own unit - drops for XRP - as the <c>Loan</c> fields are.
    /// The split comes from where the money went, not from the protocol's formulas: the vault
    /// receives principal and interest, the broker the rest (management, service, late and
    /// close fees).
    /// </remarks>
    public sealed class LoanPaymentOutcome
    {
        /// <summary>The paid loan's ledger index.</summary>
        public string LoanId { get; init; }

        /// <summary>The loan asset.</summary>
        public IssuedCurrency Asset { get; init; }

        /// <summary>Payments the transaction settled: the drop in <c>PaymentRemaining</c>.</summary>
        public uint PaymentsMade { get; init; }

        /// <summary>The drop in <c>PrincipalOutstanding</c>.</summary>
        public decimal PrincipalPaid { get; init; }

        /// <summary>What the vault received: principal and interest.</summary>
        public decimal PaidToVault { get; init; }

        /// <summary>Interest the vault received: <see cref="PaidToVault"/> less <see cref="PrincipalPaid"/>.</summary>
        public decimal InterestToVault => PaidToVault - PrincipalPaid;

        /// <summary>What the broker received: <see cref="TotalPaid"/> less <see cref="PaidToVault"/>.</summary>
        public decimal PaidToBroker => TotalPaid - PaidToVault;

        /// <summary>What left the payer's balance, the transaction fee excluded.</summary>
        public decimal TotalPaid { get; init; }

        /// <summary>The <c>Loan</c> as the transaction left it.</summary>
        public LOLoan LoanAfter { get; init; }

        /// <summary>Whether no payment remains.</summary>
        public bool IsPaidOff => (LoanAfter?.PaymentRemaining ?? 0) == 0;

        /// <summary>
        /// Reads the outcome of <paramref name="transaction"/> from its metadata.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The metadata does not modify the transaction's <c>Loan</c> or its <c>Vault</c>: the payment
        /// did not apply, or the metadata belongs to another transaction.
        /// </exception>
        public static LoanPaymentOutcome FromMetadata(ILoanPay transaction, ITransactionMetadata metadata)
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            ModifiedNode loanNode = MetadataReader.Modified(metadata, LedgerEntryType.Loan, transaction.LoanID)
                ?? throw new ArgumentException($"The metadata does not modify loan {transaction.LoanID}.", nameof(metadata));
            LOLoan before = loanNode.PreviousFields as LOLoan;
            LOLoan after = loanNode.FinalFields as LOLoan
                ?? throw new ArgumentException("The Loan node carries no final fields.", nameof(metadata));

            LOVault vault = MetadataReader.Final<LOVault>(metadata, LedgerEntryType.Vault)
                ?? throw new ArgumentException("The metadata does not modify the loan's vault.", nameof(metadata));

            Dictionary<string, List<Currency>> changes = BalanceChanges.GetBalanceChanges(metadata);

            uint remainingBefore = before?.PaymentRemaining ?? after.PaymentRemaining ?? 0;
            uint remainingAfter = before?.PaymentRemaining != null ? after.PaymentRemaining ?? 0 : remainingBefore;

            return new LoanPaymentOutcome
            {
                LoanId = loanNode.LedgerIndex,
                Asset = vault.Asset,
                PaymentsMade = remainingBefore - remainingAfter,
                PrincipalPaid = -MetadataReader.Change(before?.PrincipalOutstanding, after.PrincipalOutstanding),
                PaidToVault = MetadataReader.AssetChange(changes, vault.Account, vault.Asset),
                TotalPaid = -MetadataReader.AssetChangeWithoutFee(changes, transaction, vault.Asset),
                LoanAfter = after,
            };
        }
    }

    /// <summary>
    /// What a <c>VaultDeposit</c>, <c>VaultWithdraw</c> or <c>VaultClawback</c> did to the vault
    /// and to the account that sent it, read from the metadata.
    /// </summary>
    /// <remarks>
    /// Asset amounts are in the vault asset's own unit (drops for XRP); share amounts are whole
    /// shares of the vault's <c>ShareMPTID</c>. Changes are signed from the named party's side:
    /// a deposit makes <see cref="AccountAssetChange"/> negative and <see cref="AccountShareChange"/>
    /// positive.
    /// </remarks>
    public sealed class VaultOutcome
    {
        /// <summary>The vault's ledger index.</summary>
        public string VaultId { get; init; }

        /// <summary>The vault asset.</summary>
        public IssuedCurrency Asset { get; init; }

        /// <summary>The vault's share issuance.</summary>
        public string ShareMptId { get; init; }

        /// <summary>
        /// The change in the sending account's asset balance, the transaction fee excluded. Zero on a
        /// withdrawal paid to another <c>Destination</c>.
        /// </summary>
        public decimal AccountAssetChange { get; init; }

        /// <summary>The change in the sending account's shares.</summary>
        public decimal AccountShareChange { get; init; }

        /// <summary>The change in the assets the vault holds.</summary>
        public decimal VaultAssetChange { get; init; }

        /// <summary>The <c>Vault</c> as the transaction left it.</summary>
        public LOVault VaultAfter { get; init; }

        /// <summary>
        /// Reads the outcome of <paramref name="transaction"/> from its metadata.
        /// </summary>
        /// <exception cref="ArgumentException">The metadata does not modify a vault.</exception>
        public static VaultOutcome FromMetadata(ITransactionCommon transaction, ITransactionMetadata metadata)
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            ModifiedNode vaultNode = MetadataReader.Modified(metadata, LedgerEntryType.Vault, null)
                ?? throw new ArgumentException("The metadata does not modify a vault.", nameof(metadata));
            LOVault vault = vaultNode.FinalFields as LOVault
                ?? throw new ArgumentException("The Vault node carries no final fields.", nameof(metadata));

            Dictionary<string, List<Currency>> changes = BalanceChanges.GetBalanceChanges(metadata);

            return new VaultOutcome
            {
                VaultId = vaultNode.LedgerIndex,
                Asset = vault.Asset,
                ShareMptId = vault.ShareMPTID,
                AccountAssetChange = MetadataReader.AssetChangeWithoutFee(changes, transaction, vault.Asset),
                AccountShareChange = MetadataReader.AssetChange(
                    changes, transaction.Account, new IssuedCurrency { MptIssuanceId = vault.ShareMPTID }),
                VaultAssetChange = MetadataReader.AssetChange(changes, vault.Account, vault.Asset),
                VaultAfter = vault,
            };
        }
    }

    /// <summary>
    /// What an <c>AMMDeposit</c> or <c>AMMWithdraw</c> did to the sending account, read from the
    /// metadata.
    /// </summary>
    /// <remarks>
    /// Amounts are in each asset's own unit (drops for XRP) and signed from the account's side: a
    /// deposit makes the asset changes negative and <see cref="LpTokenChange"/> positive.
    /// </remarks>
    public sealed class AmmOutcome
    {
        /// <summary>The pool's AMM account.</summary>
        public string AmmAccount { get; init; }

        /// <summary>The first pool asset, as the transaction names it.</summary>
        public IssuedCurrency Asset { get; init; }

        /// <summary>The second pool asset, as the transaction names it.</summary>
        public IssuedCurrency Asset2 { get; init; }

        /// <summary>The change in the account's <see cref="Asset"/>, the transaction fee excluded.</summary>
        public decimal AssetChange { get; init; }

        /// <summary>The change in the account's <see cref="Asset2"/>, the transaction fee excluded.</summary>
        public decimal Asset2Change { get; init; }

        /// <summary>The change in the account's LP tokens.</summary>
        public decimal LpTokenChange { get; init; }

        /// <summary>The <c>AMM</c> entry as the transaction left it; null when the pool was deleted.</summary>
        public LOAmm AmmAfter { get; init; }

        /// <summary>Reads the outcome of an <c>AMMDeposit</c> from its metadata.</summary>
        /// <exception cref="ArgumentException">The metadata does not touch an AMM.</exception>
        public static AmmOutcome FromMetadata(IAMMDeposit transaction, ITransactionMetadata metadata) =>
            Read(transaction, transaction?.Asset, transaction?.Asset2, metadata);

        /// <summary>Reads the outcome of an <c>AMMWithdraw</c> from its metadata.</summary>
        /// <exception cref="ArgumentException">The metadata does not touch an AMM.</exception>
        public static AmmOutcome FromMetadata(IAMMWithdraw transaction, ITransactionMetadata metadata) =>
            Read(transaction, transaction?.Asset, transaction?.Asset2, metadata);

        private static AmmOutcome Read(
            ITransactionCommon transaction,
            IssuedCurrency asset,
            IssuedCurrency asset2,
            ITransactionMetadata metadata)
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            LOAmm amm = MetadataReader.Final<LOAmm>(metadata, LedgerEntryType.AMM)
                ?? MetadataReader.Deleted<LOAmm>(metadata, LedgerEntryType.AMM)
                ?? throw new ArgumentException("The metadata does not touch an AMM.", nameof(metadata));
            bool deleted = MetadataReader.Final<LOAmm>(metadata, LedgerEntryType.AMM) == null;

            Dictionary<string, List<Currency>> changes = BalanceChanges.GetBalanceChanges(metadata);
            IssuedCurrency lpToken = new IssuedCurrency
            {
                Currency = amm.LPTokenBalance?.CurrencyCode,
                Issuer = amm.AMMAccount,
            };

            return new AmmOutcome
            {
                AmmAccount = amm.AMMAccount,
                Asset = asset,
                Asset2 = asset2,
                AssetChange = MetadataReader.AssetChangeWithoutFee(changes, transaction, asset),
                Asset2Change = MetadataReader.AssetChangeWithoutFee(changes, transaction, asset2),
                LpTokenChange = MetadataReader.AssetChange(changes, transaction.Account, lpToken),
                AmmAfter = deleted ? null : amm,
            };
        }
    }

    /// <summary>
    /// Reads nodes and per-asset balance changes out of transaction metadata.
    /// </summary>
    internal static class MetadataReader
    {
        public static ModifiedNode Modified(ITransactionMetadata metadata, LedgerEntryType type, string ledgerIndex) =>
            metadata.AffectedNodes?
                .Select(node => node.ModifiedNode)
                .FirstOrDefault(node => node != null
                    && node.LedgerEntryType == type
                    && (ledgerIndex == null || string.Equals(node.LedgerIndex, ledgerIndex, StringComparison.OrdinalIgnoreCase)));

        public static T Final<T>(ITransactionMetadata metadata, LedgerEntryType type) where T : BaseLedgerEntry =>
            Modified(metadata, type, null)?.FinalFields as T
            ?? metadata.AffectedNodes?
                .Select(node => node.CreatedNode)
                .FirstOrDefault(node => node != null && node.LedgerEntryType == type)?.NewFields as T;

        public static T Deleted<T>(ITransactionMetadata metadata, LedgerEntryType type) where T : BaseLedgerEntry =>
            metadata.AffectedNodes?
                .Select(node => node.DeletedNode)
                .FirstOrDefault(node => node != null && node.LedgerEntryType == type)?.FinalFields as T;

        /// <summary>
        /// Final less previous. A field missing from <c>PreviousFields</c> did not change - or did not
        /// exist, which the metadata cannot tell apart; a field missing from <c>FinalFields</c> after a
        /// previous value is zero.
        /// </summary>
        public static decimal Change(XrplNumber? previous, XrplNumber? final)
        {
            if (previous is not { } before)
                return 0;

            return ToDecimal(final ?? XrplNumber.Zero) - ToDecimal(before);
        }

        /// <summary>The account's balance change in <paramref name="asset"/>, in the asset's own unit.</summary>
        public static decimal AssetChange(Dictionary<string, List<Currency>> changes, string account, IssuedCurrency asset)
        {
            if (asset == null || account == null || !changes.TryGetValue(account, out List<Currency> accountChanges))
                return 0;

            // An issuer's own currency shows up with the holder as the counterparty, one entry per
            // trust line, so for the issuer every line in that currency counts.
            bool isIssuer = asset.Issuer != null && string.Equals(account, asset.Issuer, StringComparison.Ordinal);
            return accountChanges
                .Where(change => isIssuer ? MatchesCurrency(change, asset) : Matches(change, asset))
                .Sum(change => change.ValueAsNumber);
        }

        /// <summary>
        /// The sender's balance change in <paramref name="asset"/> with the transaction fee added back
        /// when the asset is XRP.
        /// </summary>
        public static decimal AssetChangeWithoutFee(
            Dictionary<string, List<Currency>> changes,
            ITransactionCommon transaction,
            IssuedCurrency asset)
        {
            decimal change = AssetChange(changes, transaction.Account, asset);
            if (asset != null && asset.IsXrp() && transaction.Fee is { } fee)
                change += fee.ValueAsNumber;

            return change;
        }

        private static bool Matches(Currency change, IssuedCurrency asset)
        {
            if (asset.MptIssuanceId != null)
                return string.Equals(change.MPTokenIssuanceID, asset.MptIssuanceId, StringComparison.OrdinalIgnoreCase);

            if (change.MPTokenIssuanceID != null)
                return false;

            if (asset.IsXrp())
                return change.CurrencyCode == "XRP";

            return MatchesCurrency(change, asset)
                && string.Equals(change.Issuer, asset.Issuer, StringComparison.Ordinal);
        }

        private static bool MatchesCurrency(Currency change, IssuedCurrency asset) =>
            change.MPTokenIssuanceID == null
            && string.Equals(change.CurrencyCode, asset.Currency, StringComparison.Ordinal);

        private static decimal ToDecimal(XrplNumber number)
        {
            if (!number.TryToDecimal(out decimal value))
                throw new OverflowException($"{number} is outside the range of System.Decimal.");

            return value;
        }
    }
}
