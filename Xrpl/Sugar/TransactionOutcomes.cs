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
    /// The loan a <c>LoanSet</c> created, read from its metadata - of a validated transaction, or of a
    /// <c>simulate</c> run (<see cref="PreviewSugar.PreviewLoanSet"/>).
    /// </summary>
    /// <remarks>
    /// Amounts are in the loan asset's own unit (drops for XRP), as the <c>Loan</c> fields are.
    /// The totals cover the regular schedule; late, full and overpayments change them.
    /// </remarks>
    public sealed class LoanSetOutcome
    {
        /// <summary>The new loan's ledger index.</summary>
        public string LoanId { get; init; }

        /// <summary>The loan asset.</summary>
        public IssuedCurrency Asset { get; init; }

        /// <summary>The <c>Loan</c> as created: <c>PeriodicPayment</c>, <c>TotalValueOutstanding</c> and the rest.</summary>
        public LOLoan Loan { get; init; }

        /// <summary>What reached the borrower: the principal less <c>LoanOriginationFee</c>, the transaction fee excluded.</summary>
        public decimal BorrowerReceives { get; init; }

        /// <summary>What left the vault: the principal.</summary>
        public decimal PaidFromVault { get; init; }

        /// <summary>Interest over the schedule, management fee included: <c>TotalValueOutstanding</c> less <c>PrincipalOutstanding</c>.</summary>
        public decimal InterestTotal { get; init; }

        /// <summary><c>LoanServiceFee</c> times the number of payments.</summary>
        public decimal ServiceFeesTotal { get; init; }

        /// <summary>What the borrower repays over the regular schedule: <c>TotalValueOutstanding</c> plus <see cref="ServiceFeesTotal"/>.</summary>
        public decimal TotalToRepay { get; init; }

        /// <summary>
        /// Reads the loan <paramref name="transaction"/> created from its metadata.
        /// </summary>
        /// <exception cref="ArgumentException">The metadata creates no loan, or modifies no vault.</exception>
        public static LoanSetOutcome FromMetadata(ILoanSet transaction, ITransactionMetadata metadata)
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            CreatedNode loanNode = MetadataReader.Created(metadata, LedgerEntryType.Loan)
                ?? throw new ArgumentException("The metadata creates no loan.", nameof(metadata));
            LOLoan loan = loanNode.NewFields as LOLoan
                ?? throw new ArgumentException("The Loan node carries no fields.", nameof(metadata));
            LOVault vault = MetadataReader.Final<LOVault>(metadata, LedgerEntryType.Vault)
                ?? throw new ArgumentException("The metadata does not modify the loan's vault.", nameof(metadata));

            Dictionary<string, List<Currency>> changes = BalanceChanges.GetBalanceChanges(metadata);
            decimal borrowerReceives = string.Equals(loan.Borrower, transaction.Account, StringComparison.Ordinal)
                ? MetadataReader.AssetChangeWithoutFee(changes, transaction, vault.Asset)
                : MetadataReader.AssetChange(changes, loan.Borrower, vault.Asset);

            decimal totalValue = MetadataReader.ToDecimal(loan.TotalValueOutstanding ?? XrplNumber.Zero);
            decimal principal = MetadataReader.ToDecimal(loan.PrincipalOutstanding ?? XrplNumber.Zero);
            decimal serviceFees = MetadataReader.ToDecimal(loan.LoanServiceFee ?? XrplNumber.Zero) * (loan.PaymentRemaining ?? 0);

            return new LoanSetOutcome
            {
                LoanId = loanNode.LedgerIndex,
                Asset = vault.Asset,
                Loan = loan,
                BorrowerReceives = borrowerReceives,
                PaidFromVault = -MetadataReader.AssetChange(changes, vault.Account, vault.Asset),
                InterestTotal = totalValue - principal,
                ServiceFeesTotal = serviceFees,
                TotalToRepay = totalValue + serviceFees,
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
        /// <exception cref="ArgumentException">
        /// The transaction is not a vault deposit, withdrawal or clawback, or the metadata does not
        /// modify its vault.
        /// </exception>
        public static VaultOutcome FromMetadata(ITransactionCommon transaction, ITransactionMetadata metadata)
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            string vaultId = transaction switch
            {
                IVaultDeposit deposit => deposit.VaultID,
                IVaultWithdraw withdraw => withdraw.VaultID,
                IVaultClawback clawback => clawback.VaultID,
                _ => throw new ArgumentException(
                    $"{transaction.GetType().Name} is not a VaultDeposit, VaultWithdraw or VaultClawback.", nameof(transaction)),
            };

            ModifiedNode vaultNode = MetadataReader.Modified(metadata, LedgerEntryType.Vault, vaultId)
                ?? throw new ArgumentException($"The metadata does not modify vault {vaultId}.", nameof(metadata));
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
    /// What a <c>LoanBrokerCoverDeposit</c>, <c>LoanBrokerCoverWithdraw</c> or
    /// <c>LoanBrokerCoverClawback</c> did to the broker's first-loss cover, read from its metadata -
    /// of a validated transaction, or of a <c>simulate</c> run
    /// (<see cref="PreviewSugar.PreviewLoanBrokerCoverDeposit"/>,
    /// <see cref="PreviewSugar.PreviewLoanBrokerCoverWithdraw"/>,
    /// <see cref="PreviewSugar.PreviewLoanBrokerCoverClawback"/>).
    /// </summary>
    /// <remarks>
    /// Amounts are in the vault asset's own unit - drops for XRP - as the <c>LoanBroker</c> fields
    /// are. The vault is not in the metadata of these transactions, so the minimum cover takes it as
    /// an argument.
    /// </remarks>
    public sealed class LoanBrokerCoverOutcome
    {
        /// <summary>The broker's ledger index.</summary>
        public string LoanBrokerId { get; init; }

        /// <summary>The vault asset.</summary>
        public IssuedCurrency Asset { get; init; }

        /// <summary>The broker's <c>CoverAvailable</c> before the transaction.</summary>
        public decimal CoverBefore { get; init; }

        /// <summary>The broker's <c>CoverAvailable</c> after the transaction.</summary>
        public decimal CoverAfter { get; init; }

        /// <summary>The minimum cover <c>LoanBrokerCoverWithdraw</c> requires after the transaction (<see cref="LoanBrokerCover.MinimumCover"/>).</summary>
        public decimal MinimumCover { get; init; }

        /// <summary>The most the owner can withdraw after the transaction (<see cref="LoanBrokerCover.MaxWithdrawable"/>).</summary>
        public decimal MaxWithdrawable { get; init; }

        /// <summary>The <c>LoanBroker</c> as the transaction left it.</summary>
        public LOLoanBroker BrokerAfter { get; init; }

        /// <summary>
        /// Reads the outcome of <paramref name="transaction"/> from its metadata.
        /// </summary>
        /// <param name="transaction">The cover deposit, withdrawal or clawback.</param>
        /// <param name="metadata">Its metadata.</param>
        /// <param name="vault">The broker's <c>Vault</c> entry, for the minimum cover.</param>
        /// <param name="rules">The amendments in force; the current rules when null.</param>
        /// <exception cref="ArgumentException">
        /// The transaction is not a cover deposit, withdrawal or clawback, the metadata does not
        /// modify its broker, or the vault is not the broker's.
        /// </exception>
        public static LoanBrokerCoverOutcome FromMetadata(
            ITransactionCommon transaction,
            ITransactionMetadata metadata,
            LOVault vault,
            LedgerRules rules = null)
        {
            if (vault == null)
                throw new ArgumentNullException(nameof(vault));

            ModifiedNode brokerNode = BrokerNode(transaction, metadata);
            LOLoanBroker before = brokerNode.PreviousFields as LOLoanBroker;
            LOLoanBroker after = brokerNode.FinalFields as LOLoanBroker
                ?? throw new ArgumentException("The LoanBroker node carries no final fields.", nameof(metadata));

            LoanBrokerCover.CoverState state = LoanBrokerCover.CoverState.Of(after, vault, rules);
            XrplNumber minimum = LoanBrokerCover.WithdrawMinimum(state);

            // A cover transaction that applied changed CoverAvailable, so a value missing from
            // PreviousFields is a field that was absent before: its default of zero.
            XrplNumber coverAfter = after.CoverAvailable ?? XrplNumber.Zero;
            XrplNumber coverBefore = before?.CoverAvailable ?? XrplNumber.Zero;

            return new LoanBrokerCoverOutcome
            {
                LoanBrokerId = brokerNode.LedgerIndex,
                Asset = vault.Asset,
                CoverBefore = MetadataReader.ToDecimal(coverBefore),
                CoverAfter = MetadataReader.ToDecimal(coverAfter),
                MinimumCover = MetadataReader.ToDecimal(minimum),
                MaxWithdrawable = MetadataReader.ToDecimal(LoanBrokerCover.MaxWithdrawableCore(state, minimum)),
                BrokerAfter = after,
            };
        }

        /// <summary>The <c>LoanBroker</c> node the transaction modified.</summary>
        internal static ModifiedNode BrokerNode(ITransactionCommon transaction, ITransactionMetadata metadata)
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            // A clawback can name the broker through its pseudo-account in Amount instead of
            // LoanBrokerID; it modifies exactly one broker either way.
            string brokerId = transaction switch
            {
                ILoanBrokerCoverDeposit deposit => deposit.LoanBrokerID,
                ILoanBrokerCoverWithdraw withdraw => withdraw.LoanBrokerID,
                ILoanBrokerCoverClawback clawback => clawback.LoanBrokerID,
                _ => throw new ArgumentException(
                    $"{transaction.GetType().Name} is not a LoanBrokerCoverDeposit, LoanBrokerCoverWithdraw or LoanBrokerCoverClawback.",
                    nameof(transaction)),
            };

            return MetadataReader.Modified(metadata, LedgerEntryType.LoanBroker, brokerId)
                ?? throw new ArgumentException($"The metadata does not modify loan broker {brokerId}.", nameof(metadata));
        }
    }

    /// <summary>
    /// What a <c>LoanManage</c> did to the loan, its vault and its broker, read from its metadata - of
    /// a validated transaction, or of a <c>simulate</c> run (<see cref="PreviewSugar.PreviewLoanManage"/>).
    /// Compare it with the <see cref="LoanManageQuote"/> shown before signing.
    /// </summary>
    /// <remarks>
    /// Amounts are in the vault asset's own unit - drops for XRP - as the <c>Vault</c> and
    /// <c>LoanBroker</c> fields are. A change is final less previous, so a default makes
    /// <see cref="AssetsTotalChange"/> negative and <see cref="AssetsAvailableChange"/> positive. An
    /// entry the transaction did not modify - the broker on an impairment - changes by zero and has
    /// no "after" entry. A <c>LoanManage</c> without an action flag changes no balance, but under
    /// <c>fixCleanup3_1_3</c> it still associates the loan, broker and vault with the vault asset,
    /// which rounds their amount fields to it; a field that rounding moves shows up as a small change.
    /// </remarks>
    public sealed class LoanManageOutcome
    {
        /// <summary>The managed loan's ledger index.</summary>
        public string LoanId { get; init; }

        /// <summary>The action the transaction's flags select.</summary>
        public LoanManageAction Action { get; init; }

        /// <summary>The loan's flags after the transaction; null when the metadata does not modify the loan.</summary>
        public LoanFlags? LoanFlagsAfter { get; init; }

        /// <summary>Whether the loan is impaired after the transaction.</summary>
        public bool IsImpaired => ((LoanFlagsAfter ?? 0) & LoanFlags.lsfLoanImpaired) != 0;

        /// <summary>Whether the loan is in default after the transaction.</summary>
        public bool IsDefaulted => ((LoanFlagsAfter ?? 0) & LoanFlags.lsfLoanDefault) != 0;

        /// <summary>The change in the vault's <c>AssetsTotal</c>: minus the loss a default realizes.</summary>
        public decimal AssetsTotalChange { get; init; }

        /// <summary>The change in the vault's <c>AssetsAvailable</c>: the cover a default pays in.</summary>
        public decimal AssetsAvailableChange { get; init; }

        /// <summary>The change in the vault's <c>LossUnrealized</c>: the exposure an impairment books or an unimpairment or a default of an impaired loan takes back.</summary>
        public decimal LossUnrealizedChange { get; init; }

        /// <summary>The change in the broker's <c>DebtTotal</c>: minus the exposure on a default.</summary>
        public decimal DebtTotalChange { get; init; }

        /// <summary>The change in the broker's <c>CoverAvailable</c>: minus the cover a default pays to the vault.</summary>
        public decimal CoverAvailableChange { get; init; }

        /// <summary>The <c>Loan</c> as the transaction left it; null when the metadata does not modify it.</summary>
        public LOLoan LoanAfter { get; init; }

        /// <summary>The <c>Vault</c> as the transaction left it; null when the metadata does not modify it.</summary>
        public LOVault VaultAfter { get; init; }

        /// <summary>The <c>LoanBroker</c> as the transaction left it; null when the metadata does not modify it.</summary>
        public LOLoanBroker BrokerAfter { get; init; }

        /// <summary>
        /// Reads the outcome of <paramref name="transaction"/> from its metadata.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The transaction impairs, unimpairs or defaults the loan, and the metadata does not modify
        /// it: the transaction did not apply, or the metadata belongs to another transaction.
        /// </exception>
        public static LoanManageOutcome FromMetadata(ILoanManage transaction, ITransactionMetadata metadata)
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));
            if (metadata == null)
                throw new ArgumentNullException(nameof(metadata));

            LoanManageAction action = ActionOf(transaction.Flags);
            ModifiedNode loanNode = MetadataReader.Modified(metadata, LedgerEntryType.Loan, transaction.LoanID);
            if (loanNode == null && action != LoanManageAction.None)
                throw new ArgumentException($"The metadata does not modify loan {transaction.LoanID}.", nameof(metadata));

            LOLoan loanAfter = loanNode?.FinalFields as LOLoan;
            ModifiedNode vaultNode = MetadataReader.Modified(metadata, LedgerEntryType.Vault, null);
            ModifiedNode brokerNode = MetadataReader.Modified(metadata, LedgerEntryType.LoanBroker, loanAfter?.LoanBrokerID);
            LOVault vaultBefore = vaultNode?.PreviousFields as LOVault;
            LOVault vaultAfter = vaultNode?.FinalFields as LOVault;
            LOLoanBroker brokerBefore = brokerNode?.PreviousFields as LOLoanBroker;
            LOLoanBroker brokerAfter = brokerNode?.FinalFields as LOLoanBroker;

            // The amounts fall only, except two that rise: AssetsAvailable by the cover a default
            // pays in, and LossUnrealized on an impairment.
            decimal coverChange = MetadataReader.Change(brokerBefore?.CoverAvailable, brokerAfter?.CoverAvailable);
            return new LoanManageOutcome
            {
                LoanId = loanNode?.LedgerIndex ?? transaction.LoanID,
                Action = action,
                LoanFlagsAfter = loanAfter == null ? null : loanAfter.Flags ?? 0,
                AssetsTotalChange = MetadataReader.Change(vaultBefore?.AssetsTotal, vaultAfter?.AssetsTotal),
                AssetsAvailableChange = Rise(vaultBefore?.AssetsAvailable, vaultAfter?.AssetsAvailable, rose: coverChange < 0),
                LossUnrealizedChange = Rise(vaultBefore?.LossUnrealized, vaultAfter?.LossUnrealized, rose: action == LoanManageAction.Impair),
                DebtTotalChange = MetadataReader.Change(brokerBefore?.DebtTotal, brokerAfter?.DebtTotal),
                CoverAvailableChange = coverChange,
                LoanAfter = loanAfter,
                VaultAfter = vaultAfter,
                BrokerAfter = brokerAfter,
            };
        }

        /// <summary>
        /// The change in a field that <paramref name="rose"/> or stayed. The <c>Vault</c> amounts are
        /// left out at zero, and a field that was absent is not in <c>PreviousFields</c>, exactly like
        /// a field that did not change; the side that moved tells the two apart. An impairment whose
        /// exposure rounds away at the vault's scale is the one case read wrongly: it reads as the
        /// whole <c>LossUnrealized</c> added.
        /// </summary>
        private static decimal Rise(XrplNumber? previous, XrplNumber? final, bool rose)
        {
            if (previous != null)
                return MetadataReader.Change(previous, final);

            return rose && final is { } added ? MetadataReader.ToDecimal(added) : 0;
        }

        private static LoanManageAction ActionOf(LoanManageFlags? flags)
        {
            LoanManageFlags value = flags ?? 0;
            if ((value & LoanManageFlags.tfLoanDefault) != 0)
                return LoanManageAction.Default;
            if ((value & LoanManageFlags.tfLoanImpair) != 0)
                return LoanManageAction.Impair;
            if ((value & LoanManageFlags.tfLoanUnimpair) != 0)
                return LoanManageAction.Unimpair;
            return LoanManageAction.None;
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

        public static CreatedNode Created(ITransactionMetadata metadata, LedgerEntryType type) =>
            metadata.AffectedNodes?
                .Select(node => node.CreatedNode)
                .FirstOrDefault(node => node != null && node.LedgerEntryType == type);

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

        public static decimal ToDecimal(XrplNumber number)
        {
            if (!number.TryToDecimal(out decimal value))
                throw new OverflowException($"{number} is outside the range of System.Decimal.");

            return value;
        }
    }
}
