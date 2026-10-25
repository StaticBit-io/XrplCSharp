using System;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec.Numbers;
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
/// <see cref="LoanManagement"/> against the node: every impairment, unimpairment and default is
/// checked offline, quoted, previewed through <c>simulate</c>, compared, and submitted, so the next
/// step starts from the loan the previous one left.
/// </summary>
[TestClass]
[TestCategory("Loan")]
public class TestILoanManagement : TestILoanBase
{
    private const uint CoverRateMinimum = 12_345;
    private const uint CoverRateLiquidation = 33_333;

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
    public async Task TestXrp_ImpairUnimpairDefault_MatchWhatTheNodeDoes()
    {
        XrplWallet owner = XrplWallet.Generate();
        XrplWallet borrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, owner, borrower);
        await IntegrationTestConfig.EnsureBalanceAsync(client, owner, 200m);

        IssuedCurrency xrp = new IssuedCurrency { Currency = "XRP" };
        Currency Drops(string value) => new Currency { CurrencyCode = "XRP", Value = value };

        (string loanId, string brokerId, string vaultId) = await CreateCoveredLoan(owner, borrower, xrp, Drops, "100000000", "5000000", (XrplNumber)12_345_679);
        await Exercise(owner, loanId, brokerId, vaultId);
    }

    [TestMethod]
    public async Task TestIou_ImpairUnimpairDefault_MatchWhatTheNodeDoes()
    {
        XrplWallet issuer = XrplWallet.Generate();
        XrplWallet owner = XrplWallet.Generate();
        XrplWallet borrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, issuer, owner, borrower);

        IssuedCurrency usd = new IssuedCurrency { Currency = "USD", Issuer = issuer.ClassicAddress };
        Currency Usd(string value) => new Currency { CurrencyCode = usd.Currency, Issuer = usd.Issuer, Value = value };
        await FundIou(issuer, usd, owner, borrower);

        // A vault total held at a coarser scale than the loan, so the losses round at the vault's scale.
        (string loanId, string brokerId, string vaultId) = await CreateCoveredLoan(owner, borrower, usd, Usd, "2000000.5", "500", XrplNumber.Parse("1234.56789"));
        await Exercise(owner, loanId, brokerId, vaultId);
    }

    /// <summary>
    /// Impairs the loan once it is late, unimpairs it, impairs it again and defaults it after the
    /// grace period, comparing each quote with the preview and the submitted metadata.
    /// </summary>
    private static async Task Exercise(XrplWallet owner, string loanId, string brokerId, string vaultId)
    {
        LedgerRules rules = await LedgerRules.FromNodeAsync(client);
        (LOLoan loan, LOLoanBroker broker, LOVault _) = await Read(loanId, brokerId, vaultId);

        LoanManageAvailability early = LoanManagement.Availability(loan, broker, owner.ClassicAddress, await IntegrationTestConfig.ValidatedCloseTimeAsync(client), rules);
        Assert.AreEqual("tecTOO_SOON", early.Default.Refusal, early.Default.RefusalReason);
        if (!early.Impair.IsAllowed)
            await AssertRefusedAsTheQuoteSays(owner, loanId, LoanManageFlags.tfLoanImpair, early.Impair.Refusal);

        await IntegrationTestConfig.WaitForCloseTimeAsync(client, (early.ImpairableAt ?? loan.NextPaymentDueDate.Value).AddSeconds(1), nodeType);
        await Step(owner, loanId, brokerId, vaultId, LoanManageFlags.tfLoanImpair, rules);
        await Step(owner, loanId, brokerId, vaultId, LoanManageFlags.tfLoanUnimpair, rules);
        await Step(owner, loanId, brokerId, vaultId, LoanManageFlags.tfLoanImpair, rules);

        await IntegrationTestConfig.WaitForCloseTimeAsync(client, early.DefaultableAt.Value.AddSeconds(1), nodeType);
        await Step(owner, loanId, brokerId, vaultId, LoanManageFlags.tfLoanDefault, rules);

        (loan, broker, _) = await Read(loanId, brokerId, vaultId);
        LoanManageAvailability after = LoanManagement.Availability(loan, broker, owner.ClassicAddress, await IntegrationTestConfig.ValidatedCloseTimeAsync(client), rules);
        Assert.AreEqual("tecNO_PERMISSION", after.Unimpair.Refusal);
        await AssertRefusedAsTheQuoteSays(owner, loanId, LoanManageFlags.tfLoanImpair, after.Impair.Refusal);
    }

    private static async Task Step(XrplWallet owner, string loanId, string brokerId, string vaultId, LoanManageFlags flag, LedgerRules rules)
    {
        (LOLoan loan, LOLoanBroker broker, LOVault vault) = await Read(loanId, brokerId, vaultId);
        DateTime now = await IntegrationTestConfig.ValidatedCloseTimeAsync(client);
        LOMPTokenIssuance issuance = (LOMPTokenIssuance)(await client.LedgerEntry(new LedgerEntryRequest { MptIssuance = vault.ShareMPTID }).Typed()).Node;
        ulong shares = issuance.OutstandingAmount ?? 0;

        LoanManageAvailability availability = LoanManagement.Availability(loan, broker, owner.ClassicAddress, now, rules);
        LoanManageQuote quote = flag switch
        {
            LoanManageFlags.tfLoanImpair => LoanManagement.Impair(loan, vault, now, rules, shares),
            LoanManageFlags.tfLoanUnimpair => LoanManagement.Unimpair(loan, vault, now, rules, shares),
            _ => LoanManagement.Default(loan, broker, vault, rules, now, shares),
        };
        string at = $"{flag}: {quote.Refusal} ({quote.RefusalReason})";
        Assert.IsTrue(quote.IsAccepted, at);

        TransactionPreview<LoanManageOutcome> preview = await client.PreviewLoanManage(new LoanManage
        {
            Account = owner.ClassicAddress,
            LoanID = loanId,
            Flags = flag,
        });
        Assert.IsTrue(preview.WouldSucceed, $"{flag}: {preview.EngineResult}");
        bool wasImpaired = ((loan.Flags ?? 0) & LoanFlags.lsfLoanImpaired) != 0;
        AssertMatches(quote, preview.Outcome, wasImpaired, flag.ToString());

        TransactionSummary result = await client.SubmitAndWait(preview.Transaction, owner, false);
        ValidateResult(result);
        LoanManageOutcome actual = LoanManageOutcome.FromMetadata((ILoanManage)preview.Transaction, result.Meta);
        AssertMatches(quote, actual, wasImpaired, $"{flag} submitted");

        LoanManageCheck check = flag switch
        {
            LoanManageFlags.tfLoanImpair => availability.Impair,
            LoanManageFlags.tfLoanUnimpair => availability.Unimpair,
            _ => availability.Default,
        };
        Assert.IsTrue(check.IsAllowed, $"{flag}: {check.Refusal} ({check.RefusalReason})");
    }

    private static void AssertMatches(LoanManageQuote quote, LoanManageOutcome outcome, bool wasImpaired, string at)
    {
        Assert.AreEqual(quote.AssetsTotalAfter - quote.AssetsTotalBefore, outcome.AssetsTotalChange, $"{at}: AssetsTotal");
        Assert.AreEqual(quote.AssetsAvailableAfter - quote.AssetsAvailableBefore, outcome.AssetsAvailableChange, $"{at}: AssetsAvailable");
        Assert.AreEqual(quote.LossUnrealizedAfter - quote.LossUnrealizedBefore, outcome.LossUnrealizedChange, $"{at}: LossUnrealized");
        Assert.AreEqual((quote.DebtTotalAfter ?? 0) - (quote.DebtTotalBefore ?? 0), outcome.DebtTotalChange, $"{at}: DebtTotal");
        Assert.AreEqual((quote.CoverAvailableAfter ?? 0) - (quote.CoverAvailableBefore ?? 0), outcome.CoverAvailableChange, $"{at}: CoverAvailable");
        Assert.AreEqual(quote.Action, outcome.Action, $"{at}: action");
        // A default sets lsfLoanDefault and leaves lsfLoanImpaired as it was; only an unimpairment clears it.
        bool impairedAfter = quote.Action == LoanManageAction.Impair || (quote.Action == LoanManageAction.Default && wasImpaired);
        Assert.AreEqual(impairedAfter, outcome.IsImpaired, $"{at}: impaired");
        Assert.AreEqual(quote.Action == LoanManageAction.Default, outcome.IsDefaulted, $"{at}: defaulted");
        Assert.AreEqual(quote.NextPaymentDueDateAfter, outcome.LoanAfter?.NextPaymentDueDate, $"{at}: NextPaymentDueDate");
    }

    private static async Task AssertRefusedAsTheQuoteSays(XrplWallet owner, string loanId, LoanManageFlags flag, string refusal)
    {
        TransactionPreview<LoanManageOutcome> preview = await client.PreviewLoanManage(new LoanManage
        {
            Account = owner.ClassicAddress,
            LoanID = loanId,
            Flags = flag,
        });
        Assert.AreEqual(refusal, preview.EngineResult, $"{flag}: the node answers as the availability says");
        Assert.IsNull(preview.Outcome);
    }

    private static async Task<(LOLoan Loan, LOLoanBroker Broker, LOVault Vault)> Read(string loanId, string brokerId, string vaultId)
    {
        LOLoan loan = (LOLoan)(await client.LedgerEntry(new LedgerEntryRequest { Index = loanId }).Typed()).Node;
        LOLoanBroker broker = (LOLoanBroker)(await client.LedgerEntry(new LedgerEntryRequest { Index = brokerId }).Typed()).Node;
        LOVault vault = (LOVault)(await client.LedgerEntry(new LedgerEntryRequest { Index = vaultId }).Typed()).Node;
        return (loan, broker, vault);
    }

    /// <summary>
    /// A vault of <paramref name="asset"/> owned by <paramref name="owner"/>, a broker on it with
    /// first-loss cover, and one loan to <paramref name="borrower"/> due a minute after it starts.
    /// </summary>
    private static async Task<(string LoanId, string BrokerId, string VaultId)> CreateCoveredLoan(
        XrplWallet owner,
        XrplWallet borrower,
        IssuedCurrency asset,
        Func<string, Currency> amount,
        string vaultDeposit,
        string cover,
        XrplNumber principal)
    {
        VaultCreate vaultTx = await client.Autofill(await BuildBrokerVaultAsync(client, owner.ClassicAddress, asset));
        TransactionSummary vaultResult = await client.SubmitAndWait(vaultTx, owner, true);
        ValidateResult(vaultResult);
        string vaultId = GetCreatedObjectId(vaultResult, LedgerEntryType.Vault);

        VaultDeposit deposit = await client.Autofill(new VaultDeposit
        {
            Account = owner.ClassicAddress,
            VaultID = vaultId,
            Amount = amount(vaultDeposit),
        });
        ValidateResult(await client.SubmitAndWait(deposit, owner, true));

        LoanBrokerSet brokerTx = await client.Autofill(new LoanBrokerSet
        {
            Account = owner.ClassicAddress,
            VaultID = vaultId,
            CoverRateMinimum = CoverRateMinimum,
            CoverRateLiquidation = CoverRateLiquidation,
        });
        TransactionSummary brokerResult = await client.SubmitAndWait(brokerTx, owner, true);
        ValidateResult(brokerResult);
        string brokerId = GetCreatedObjectId(brokerResult, LedgerEntryType.LoanBroker);

        LoanBrokerCoverDeposit coverTx = await client.Autofill(new LoanBrokerCoverDeposit
        {
            Account = owner.ClassicAddress,
            LoanBrokerID = brokerId,
            Amount = amount(cover),
        });
        ValidateResult(await client.SubmitAndWait(coverTx, owner, true));

        await EnterInvestmentPhaseAsync(client, vaultId);

        LoanSet loanTx = new LoanSet
        {
            Account = owner.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = borrower.ClassicAddress,
            PrincipalRequested = principal,
            InterestRate = 25_000,
            PaymentTotal = 4,
            PaymentInterval = 60,
            GracePeriod = 60,
        };
        TransactionSummary loan = await SubmitLoanSetWithCounterpartySig(client, loanTx, owner, borrower);
        ValidateResult(loan);
        string loanId = LoanSetOutcome.FromMetadata(loanTx, loan.Meta).LoanId;

        return (loanId, brokerId, vaultId);
    }

    private static async Task FundIou(XrplWallet issuer, IssuedCurrency usd, params XrplWallet[] holders)
    {
        AccountSet rippling = await client.Autofill(new AccountSet
        {
            Account = issuer.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfDefaultRipple,
        });
        ValidateResult(await client.SubmitAndWait(rippling, issuer, true));

        foreach (XrplWallet holder in holders)
        {
            TrustSet trust = await client.Autofill(new TrustSet
            {
                Account = holder.ClassicAddress,
                LimitAmount = new Currency { CurrencyCode = usd.Currency, Issuer = usd.Issuer, Value = "100000000" },
            });
            ValidateResult(await client.SubmitAndWait(trust, holder, true));

            Payment funding = await client.Autofill(new Payment
            {
                Account = issuer.ClassicAddress,
                Destination = holder.ClassicAddress,
                Amount = new Currency { CurrencyCode = usd.Currency, Issuer = usd.Issuer, Value = "5000000" },
            });
            ValidateResult(await client.SubmitAndWait(funding, issuer, true));
        }
    }
}
