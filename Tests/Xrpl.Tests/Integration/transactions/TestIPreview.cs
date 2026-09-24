using System;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// A preview through <c>simulate</c> must read exactly what the same transaction does once
/// submitted. Each test previews, submits the previewed transaction unchanged, and compares the
/// two outcomes field by field.
/// </summary>
[TestClass]
[TestCategory("Loan")]
public class TestIPreviewLoanVault : TestILoanBase
{
    private static IXrplClient client;
    protected override IXrplClient GetClient() => client;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await CreateStandaloneClient();
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    [TestMethod]
    public async Task TestPreviewLoanSet_MatchesTheLoanTheSignedTransactionCreates()
    {
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower);
        string brokerId = await CreateBroker(client, walletBroker);

        LoanSet Terms() => new LoanSet
        {
            Account = walletBroker.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = 10_000_000,
            InterestRate = 25_000,
            PaymentTotal = 4,
            PaymentInterval = 120,
            GracePeriod = 60,
            LoanServiceFee = 100,
            LoanOriginationFee = 1_000,
        };

        // No signature from either party: the borrower has not seen the terms yet.
        TransactionPreview<LoanSetOutcome> preview = await client.PreviewLoanSet(Terms());
        Assert.IsTrue(preview.WouldSucceed, preview.EngineResult);
        LoanSetOutcome expected = preview.Outcome;

        TransactionSummary created = await SubmitLoanSetWithCounterpartySig(client, Terms(), walletBroker, walletBorrower);
        ValidateResult(created);
        LoanSetOutcome actual = LoanSetOutcome.FromMetadata(Terms(), created.Meta);

        Assert.AreEqual(expected.LoanId, actual.LoanId, "the loan ID follows the broker's LoanSequence");
        Assert.AreEqual(expected.Loan.PeriodicPayment, actual.Loan.PeriodicPayment);
        Assert.AreEqual(expected.Loan.TotalValueOutstanding, actual.Loan.TotalValueOutstanding);
        Assert.AreEqual(expected.Loan.PrincipalOutstanding, actual.Loan.PrincipalOutstanding);
        Assert.AreEqual(expected.Loan.ManagementFeeOutstanding, actual.Loan.ManagementFeeOutstanding);
        Assert.AreEqual(expected.Loan.PaymentRemaining, actual.Loan.PaymentRemaining);
        Assert.AreEqual(expected.BorrowerReceives, actual.BorrowerReceives);
        Assert.AreEqual(expected.TotalToRepay, actual.TotalToRepay);
        Assert.AreEqual(9_999_000m, actual.BorrowerReceives);
    }

    [TestMethod]
    public async Task TestPreviewLoanPay_MatchesTheSubmittedPayment()
    {
        XrplWallet walletBroker = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletBroker, walletBorrower);

        string brokerId = await CreateBroker(client, walletBroker);
        LoanSet loanTx = new LoanSet
        {
            Account = walletBroker.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = 10_000_000,
            InterestRate = 10_000,
            PaymentTotal = 3,
            PaymentInterval = 120,
            GracePeriod = 60,
            LoanServiceFee = 100,
        };
        TransactionSummary loanResult = await SubmitLoanSetWithCounterpartySig(client, loanTx, walletBroker, walletBorrower);
        ValidateResult(loanResult);
        string loanId = GetCreatedObjectId(loanResult, LedgerEntryType.Loan);

        LOLoan loan = (LOLoan)(await client.LedgerEntry(new LedgerEntryRequest { Index = loanId }).Typed()).Node;
        IssuedCurrency xrp = new IssuedCurrency { Currency = "XRP" };
        decimal due = LoanPayments.RegularPaymentCap(loan, xrp).Value;

        LoanPay pay = new LoanPay
        {
            Account = walletBorrower.ClassicAddress,
            LoanID = loanId,
            Amount = new Currency { Value = due.ToString(System.Globalization.CultureInfo.InvariantCulture), CurrencyCode = "XRP" },
        };

        TransactionPreview<LoanPaymentOutcome> preview = await client.PreviewLoanPay(pay);
        Assert.IsTrue(preview.WouldSucceed, preview.EngineResult);
        LoanPaymentOutcome expected = preview.Outcome;

        TransactionSummary submitted = await client.SubmitAndWait(preview.Transaction, walletBorrower, false);
        ValidateResult(submitted);
        LoanPaymentOutcome actual = LoanPaymentOutcome.FromMetadata((ILoanPay)preview.Transaction, submitted.Meta);

        Assert.AreEqual(expected.PaymentsMade, actual.PaymentsMade);
        Assert.AreEqual(expected.PrincipalPaid, actual.PrincipalPaid);
        Assert.AreEqual(expected.PaidToVault, actual.PaidToVault);
        Assert.AreEqual(expected.TotalPaid, actual.TotalPaid);
        Assert.AreEqual(expected.LoanAfter.PrincipalOutstanding, actual.LoanAfter.PrincipalOutstanding);
        Assert.AreEqual(expected.LoanAfter.TotalValueOutstanding, actual.LoanAfter.TotalValueOutstanding);

        // One regular payment; here the schedule charged the whole cap.
        Assert.AreEqual(1u, actual.PaymentsMade);
        Assert.AreEqual(due, actual.TotalPaid);
        Assert.AreEqual(100m, actual.PaidToBroker);
    }

    [TestMethod]
    public async Task TestPreviewLoanPay_Mpt_MatchesTheSubmittedPayment()
    {
        // The borrower's MPToken is authorized empty and then filled by the LoanSet, so its amount
        // appears in metadata with no previous value - the case the MPT balance reading has to
        // resolve against the issuance's OutstandingAmount. The broker's fee goes to its owner,
        // who is the MPT issuer: it leaves circulation.
        XrplWallet walletIssuer = XrplWallet.Generate();
        XrplWallet walletHolder = XrplWallet.Generate();
        XrplWallet walletBorrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletIssuer, walletHolder, walletBorrower);

        (string brokerId, string mptIssuanceId) = await CreateMptBroker(client, walletIssuer, walletHolder);

        MPTokenAuthorize authorize = await client.Autofill(new MPTokenAuthorize
        {
            Account = walletBorrower.ClassicAddress,
            MPTokenIssuanceID = mptIssuanceId,
        });
        ValidateResult(await client.SubmitAndWait(authorize, walletBorrower, true));

        LoanSet loanTx = new LoanSet
        {
            Account = walletIssuer.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = walletBorrower.ClassicAddress,
            PrincipalRequested = 90,
            InterestRate = 10_000,
            PaymentTotal = 3,
            PaymentInterval = 120,
            GracePeriod = 60,
            LoanServiceFee = 1,
        };
        TransactionSummary loanResult = await SubmitLoanSetWithCounterpartySig(client, loanTx, walletIssuer, walletBorrower);
        ValidateResult(loanResult);
        string loanId = GetCreatedObjectId(loanResult, LedgerEntryType.Loan);

        LOLoan loan = (LOLoan)(await client.LedgerEntry(new LedgerEntryRequest { Index = loanId }).Typed()).Node;
        IssuedCurrency asset = new IssuedCurrency { MptIssuanceId = mptIssuanceId };
        decimal due = LoanPayments.RegularPaymentCap(loan, asset).Value;

        LoanPay pay = new LoanPay
        {
            Account = walletBorrower.ClassicAddress,
            LoanID = loanId,
            Amount = new Currency { Value = due.ToString(System.Globalization.CultureInfo.InvariantCulture), MPTokenIssuanceID = mptIssuanceId },
        };

        TransactionPreview<LoanPaymentOutcome> preview = await client.PreviewLoanPay(pay);
        Assert.IsTrue(preview.WouldSucceed, preview.EngineResult);

        TransactionSummary submitted = await client.SubmitAndWait(preview.Transaction, walletBorrower, false);
        ValidateResult(submitted);
        LoanPaymentOutcome actual = LoanPaymentOutcome.FromMetadata((ILoanPay)preview.Transaction, submitted.Meta);

        Assert.AreEqual(preview.Outcome.PrincipalPaid, actual.PrincipalPaid);
        Assert.AreEqual(preview.Outcome.PaidToVault, actual.PaidToVault);
        Assert.AreEqual(preview.Outcome.TotalPaid, actual.TotalPaid);
        Assert.AreEqual(1u, actual.PaymentsMade);
        Assert.AreEqual(1m, actual.PaidToBroker, "the LoanServiceFee");

        // PeriodicPayment 30.00002 rounds up to 31, but the schedule charges this period 30: the
        // cap covered the payment and the node took one unit less.
        Assert.AreEqual(31m, actual.TotalPaid);
        Assert.AreEqual(32m, due);
        Assert.AreEqual(mptIssuanceId, actual.Asset.MptIssuanceId);
    }

    [TestMethod]
    public async Task TestPreviewVaultDepositAndWithdraw_MatchTheSubmittedTransactions()
    {
        XrplWallet owner = XrplWallet.Generate();
        XrplWallet depositor = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, owner, depositor);

        VaultCreate create = await client.Autofill(new VaultCreate
        {
            Account = owner.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
        });
        TransactionSummary created = await client.SubmitAndWait(create, owner, true);
        ValidateResult(created);
        string vaultId = GetCreatedObjectId(created, LedgerEntryType.Vault);

        VaultDeposit deposit = new VaultDeposit
        {
            Account = depositor.ClassicAddress,
            VaultID = vaultId,
            Amount = new Currency { Value = "10000000", CurrencyCode = "XRP" },
        };
        TransactionPreview<VaultOutcome> depositPreview = await client.PreviewVaultDeposit(deposit);
        Assert.IsTrue(depositPreview.WouldSucceed, depositPreview.EngineResult);
        TransactionSummary deposited = await client.SubmitAndWait(depositPreview.Transaction, depositor, false);
        ValidateResult(deposited);
        AssertSame(depositPreview.Outcome, VaultOutcome.FromMetadata((ITransactionCommon)depositPreview.Transaction, deposited.Meta));
        Assert.AreEqual(-10_000_000m, depositPreview.Outcome.AccountAssetChange);
        Assert.IsTrue(depositPreview.Outcome.AccountShareChange > 0);

        VaultWithdraw withdraw = new VaultWithdraw
        {
            Account = depositor.ClassicAddress,
            VaultID = vaultId,
            Amount = new Currency { Value = "3000000", CurrencyCode = "XRP" },
        };
        TransactionPreview<VaultOutcome> withdrawPreview = await client.PreviewVaultWithdraw(withdraw);
        Assert.IsTrue(withdrawPreview.WouldSucceed, withdrawPreview.EngineResult);
        TransactionSummary withdrawn = await client.SubmitAndWait(withdrawPreview.Transaction, depositor, false);
        ValidateResult(withdrawn);
        AssertSame(withdrawPreview.Outcome, VaultOutcome.FromMetadata((ITransactionCommon)withdrawPreview.Transaction, withdrawn.Meta));
        Assert.AreEqual(3_000_000m, withdrawPreview.Outcome.AccountAssetChange);
        Assert.IsTrue(withdrawPreview.Outcome.AccountShareChange < 0);
    }

    [TestMethod]
    public async Task TestPreviewLoanPay_Failure_CarriesTheResultAndNoOutcome()
    {
        XrplWallet stranger = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, stranger, nodeType);

        LoanPay pay = new LoanPay
        {
            Account = stranger.ClassicAddress,
            LoanID = new string('0', 63) + "1",
            Amount = new Currency { Value = "1000", CurrencyCode = "XRP" },
        };

        TransactionPreview<LoanPaymentOutcome> preview = await client.PreviewLoanPay(pay);

        Assert.IsFalse(preview.WouldSucceed);
        Assert.IsFalse(string.IsNullOrEmpty(preview.EngineResult));
        Assert.IsNull(preview.Outcome);
    }

    private static void AssertSame(VaultOutcome expected, VaultOutcome actual)
    {
        Assert.AreEqual(expected.VaultId, actual.VaultId);
        Assert.AreEqual(expected.AccountAssetChange, actual.AccountAssetChange);
        Assert.AreEqual(expected.AccountShareChange, actual.AccountShareChange);
        Assert.AreEqual(expected.VaultAssetChange, actual.VaultAssetChange);
        Assert.AreEqual(expected.VaultAfter.AssetsTotal, actual.VaultAfter.AssetsTotal);
    }
}

/// <summary>
/// AMM previews against the submitted transactions; see <see cref="TestIPreviewLoanVault"/>.
/// </summary>
[TestClass]
public class TestIPreviewAmm : TestIAMMBase
{
    private static IXrplClient client;
    protected override IXrplClient GetClient() => client;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await CreateStandaloneClient();
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    [TestMethod]
    public async Task TestPreviewAMMDepositAndWithdraw_MatchTheSubmittedTransactions()
    {
        AssertSuccess(await CreatePool("1000", 10m), "AMMCreate");

        // Single-sided: the trading fee applies, so the LP amount is not a simple proportion.
        AMMDeposit deposit = new AMMDeposit
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Amount = new Currency { ValueAsXrp = 1m },
            Flags = AMMDepositFlags.tfSingleAsset,
        };
        TransactionPreview<AmmOutcome> depositPreview = await client.PreviewAMMDeposit(deposit);
        Assert.IsTrue(depositPreview.WouldSucceed, depositPreview.EngineResult);
        TransactionSummary deposited = await client.SubmitAndWait(depositPreview.Transaction, walletHolder, false);
        AssertSuccess(deposited, "AMMDeposit");
        AmmOutcome depositActual = AmmOutcome.FromMetadata((IAMMDeposit)depositPreview.Transaction, deposited.Meta);
        AssertSame(depositPreview.Outcome, depositActual);
        Assert.AreEqual(-1_000_000m, depositActual.Asset2Change, "1 XRP left the account");
        Assert.AreEqual(0m, depositActual.AssetChange);
        Assert.IsTrue(depositActual.LpTokenChange > 0);

        AMMInfoResponse info = await GetAmmInfo();
        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            LPTokenIn = new Currency
            {
                CurrencyCode = info.Amm.LPTokenBalance.CurrencyCode,
                Issuer = info.Amm.LPTokenBalance.Issuer,
                ValueAsNumber = depositActual.LpTokenChange,
            },
            Flags = AMMWithdrawFlags.tfLPToken,
        };
        TransactionPreview<AmmOutcome> withdrawPreview = await client.PreviewAMMWithdraw(withdraw);
        Assert.IsTrue(withdrawPreview.WouldSucceed, withdrawPreview.EngineResult);
        TransactionSummary withdrawn = await client.SubmitAndWait(withdrawPreview.Transaction, walletHolder, false);
        AssertSuccess(withdrawn, "AMMWithdraw");
        AmmOutcome withdrawActual = AmmOutcome.FromMetadata((IAMMWithdraw)withdrawPreview.Transaction, withdrawn.Meta);
        AssertSame(withdrawPreview.Outcome, withdrawActual);
        Assert.AreEqual(-depositActual.LpTokenChange, withdrawActual.LpTokenChange);
        Assert.IsTrue(withdrawActual.AssetChange > 0 && withdrawActual.Asset2Change > 0);
    }

    private static void AssertSame(AmmOutcome expected, AmmOutcome actual)
    {
        Assert.AreEqual(expected.AmmAccount, actual.AmmAccount);
        Assert.AreEqual(expected.AssetChange, actual.AssetChange);
        Assert.AreEqual(expected.Asset2Change, actual.Asset2Change);
        Assert.AreEqual(expected.LpTokenChange, actual.LpTokenChange);
        Assert.AreEqual(expected.AmmAfter?.LPTokenBalance?.Value, actual.AmmAfter?.LPTokenBalance?.Value);
    }
}
