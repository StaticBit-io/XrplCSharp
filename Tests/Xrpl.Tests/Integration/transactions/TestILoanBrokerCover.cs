using System;
using System.Globalization;
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
/// <see cref="LoanBrokerCover"/> against the node: the largest withdrawal it computes is the one
/// the node takes, anything above it is refused the same way, and the cover previews read what
/// the submitted transactions do.
/// </summary>
[TestClass]
[TestCategory("Loan")]
public class TestILoanBrokerCover : TestIPreviewLoanBase
{
    private const uint CoverRateMinimum = 12_345;
    private const uint CoverRateLiquidation = 10_000;

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
    public async Task TestIou_MaxWithdrawableIsWhatTheNodeTakes()
    {
        XrplWallet issuer = XrplWallet.Generate();
        XrplWallet owner = XrplWallet.Generate();
        XrplWallet borrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, issuer, owner, borrower);

        IssuedCurrency usd = new IssuedCurrency { Currency = "USD", Issuer = issuer.ClassicAddress };
        Currency Usd(string value) => new Currency { CurrencyCode = usd.Currency, Issuer = usd.Issuer, Value = value };
        await FundIou(issuer, usd, owner, borrower);

        // AssetsTotal about 2,000,000 is held at exponent -9, DebtTotal about 1,234 at -12: the
        // minimum cover rounds differently on either side of fixCleanup3_2_0.
        (string brokerId, string vaultId) = await CreateCoveredBrokerWithLoan(owner, borrower, usd, Usd, "2000000.5", "500", XrplNumber.Parse("1234.56789"));
        (LOLoanBroker broker, LOVault vault) = await Read(brokerId, vaultId);
        LedgerRules rules = await LedgerRules.FromNodeAsync(client);
        Assert.IsTrue(rules.FixCleanup3_2_0, "the stand runs fixCleanup3_2_0");

        decimal minimum = LoanBrokerCover.MinimumCover(broker, vault, rules);
        decimal maximum = LoanBrokerCover.MaxWithdrawable(broker, vault, rules);
        decimal legacyMaximum = LoanBrokerCover.MaxWithdrawable(broker, vault, new LedgerRules { FixCleanup3_2_0 = false });
        Assert.IsTrue(legacyMaximum > maximum, $"the legacy rounding allows more: {legacyMaximum} vs {maximum}");

        // The maximum the old rounding allows is refused now, as the offline check says.
        await AssertWithdrawRefused(owner, brokerId, broker, vault, rules, legacyMaximum, Usd);
        await AssertWithdrawRefused(owner, brokerId, broker, vault, rules, maximum + 0.000000001m, Usd);

        LoanBrokerCoverOutcome withdrawn = await PreviewAndSubmitWithdraw(owner, brokerId, Text(maximum), Usd);
        Assert.AreEqual(minimum, withdrawn.MinimumCover);
        Assert.AreEqual(500m - maximum, withdrawn.CoverAfter);
        Assert.AreEqual(0m, withdrawn.MaxWithdrawable);

        // A deposit is credited what CheckDeposit says.
        (broker, vault) = await Read(brokerId, vaultId);
        LoanBrokerCoverQuote quote = LoanBrokerCover.CheckDeposit(broker, vault, XrplNumber.Parse("10.00000000000012"), rules);
        Assert.IsTrue(quote.IsAccepted, quote.RefusalReason);
        LoanBrokerCoverOutcome deposited = await PreviewAndSubmitDeposit(owner, brokerId, "10.00000000000012", Usd);
        Assert.AreEqual(quote.CoverAfter, deposited.CoverAfter);
        Assert.AreEqual(quote.CoverAfter - minimum, deposited.MaxWithdrawable);
    }

    [TestMethod]
    public async Task TestXrp_MaxWithdrawableIsWhatTheNodeTakes()
    {
        XrplWallet owner = XrplWallet.Generate();
        XrplWallet borrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, owner, borrower);
        await IntegrationTestConfig.EnsureBalanceAsync(client, owner, 200m);

        IssuedCurrency xrp = new IssuedCurrency { Currency = "XRP" };
        Currency Drops(string value) => new Currency { CurrencyCode = "XRP", Value = value };

        (string brokerId, string vaultId) = await CreateCoveredBrokerWithLoan(owner, borrower, xrp, Drops, "100000000", "50000000", (XrplNumber)1_234_567);
        (LOLoanBroker broker, LOVault vault) = await Read(brokerId, vaultId);
        LedgerRules rules = await LedgerRules.FromNodeAsync(client);

        decimal maximum = LoanBrokerCover.MaxWithdrawable(broker, vault, rules);
        Assert.IsTrue(maximum > 0m);
        await AssertWithdrawRefused(owner, brokerId, broker, vault, rules, maximum + 1m, Drops);

        LoanBrokerCoverOutcome withdrawn = await PreviewAndSubmitWithdraw(owner, brokerId, Text(maximum), Drops);
        Assert.AreEqual(LoanBrokerCover.MinimumCover(broker, vault, rules), withdrawn.CoverAfter);
        Assert.AreEqual(0m, withdrawn.MaxWithdrawable);
    }

    [TestMethod]
    public async Task TestMpt_ClawbackTakesWhatMaxClawbackSays()
    {
        XrplWallet issuer = XrplWallet.Generate();
        XrplWallet owner = XrplWallet.Generate();
        XrplWallet borrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, issuer, owner, borrower);

        string issuanceId = await CreateMpt(issuer, owner, borrower);
        IssuedCurrency mpt = new IssuedCurrency { MptIssuanceId = issuanceId };
        Currency Mpt(string value) => new Currency { MPTokenIssuanceID = issuanceId, Value = value };

        (string brokerId, string vaultId) = await CreateCoveredBrokerWithLoan(owner, borrower, mpt, Mpt, "1500000", "300000", (XrplNumber)1_234_567);
        (LOLoanBroker broker, LOVault vault) = await Read(brokerId, vaultId);
        LedgerRules rules = await LedgerRules.FromNodeAsync(client);

        decimal maximum = LoanBrokerCover.MaxWithdrawable(broker, vault, rules);
        await AssertWithdrawRefused(owner, brokerId, broker, vault, rules, maximum + 1m, Mpt);

        // The issuer claws back everything above the minimum when it names no amount.
        LoanBrokerCoverQuote expected = LoanBrokerCover.CheckClawback(broker, vault, rules: rules);
        Assert.IsTrue(expected.IsAccepted, expected.RefusalReason);
        Assert.AreEqual(maximum, expected.Amount);

        LoanBrokerCoverClawback Clawback() => new LoanBrokerCoverClawback
        {
            Account = issuer.ClassicAddress,
            LoanBrokerID = brokerId,
        };
        TransactionPreview<LoanBrokerCoverOutcome> preview = await client.PreviewLoanBrokerCoverClawback(Clawback(), rules);
        Assert.IsTrue(preview.WouldSucceed, preview.EngineResult);
        Assert.AreEqual(expected.CoverAfter, preview.Outcome.CoverAfter);

        TransactionSummary result = await client.SubmitAndWait(preview.Transaction, issuer, false);
        ValidateResult(result);
        LoanBrokerCoverOutcome actual = LoanBrokerCoverOutcome.FromMetadata((ITransactionCommon)preview.Transaction, result.Meta, vault, rules);
        AssertSame(preview.Outcome, actual);
        Assert.AreEqual(expected.CoverBefore, actual.CoverBefore);
        Assert.AreEqual(expected.MinimumCover, actual.CoverAfter);

        // Nothing is left above the minimum: the node refuses another clawback as the quote does.
        (broker, vault) = await Read(brokerId, vaultId);
        Assert.AreEqual("tecINSUFFICIENT_FUNDS", LoanBrokerCover.CheckClawback(broker, vault, rules: rules).Refusal);
        TransactionPreview<LoanBrokerCoverOutcome> again = await client.PreviewLoanBrokerCoverClawback(Clawback(), rules);
        Assert.AreEqual("tecINSUFFICIENT_FUNDS", again.EngineResult);
    }

    private async Task AssertWithdrawRefused(
        XrplWallet owner,
        string brokerId,
        LOLoanBroker broker,
        LOVault vault,
        LedgerRules rules,
        decimal amount,
        Func<string, Currency> currency)
    {
        LoanBrokerCoverQuote quote = LoanBrokerCover.CheckWithdraw(broker, vault, (XrplNumber)amount, rules);
        TransactionPreview<LoanBrokerCoverOutcome> preview = await client.PreviewLoanBrokerCoverWithdraw(
            new LoanBrokerCoverWithdraw { Account = owner.ClassicAddress, LoanBrokerID = brokerId, Amount = currency(Text(amount)) },
            rules);

        Assert.AreEqual("tecINSUFFICIENT_FUNDS", quote.Refusal, $"{amount}: {quote.RefusalReason}");
        Assert.AreEqual(quote.Refusal, preview.EngineResult, $"{amount}: the node answers as the quote does");
        Assert.IsNull(preview.Outcome);
    }

    private async Task<LoanBrokerCoverOutcome> PreviewAndSubmitWithdraw(XrplWallet owner, string brokerId, string amount, Func<string, Currency> currency)
    {
        TransactionPreview<LoanBrokerCoverOutcome> preview = await client.PreviewLoanBrokerCoverWithdraw(
            new LoanBrokerCoverWithdraw { Account = owner.ClassicAddress, LoanBrokerID = brokerId, Amount = currency(amount) });
        return await SubmitPreviewed(preview, owner);
    }

    private async Task<LoanBrokerCoverOutcome> PreviewAndSubmitDeposit(XrplWallet owner, string brokerId, string amount, Func<string, Currency> currency)
    {
        TransactionPreview<LoanBrokerCoverOutcome> preview = await client.PreviewLoanBrokerCoverDeposit(
            new LoanBrokerCoverDeposit { Account = owner.ClassicAddress, LoanBrokerID = brokerId, Amount = currency(amount) });
        return await SubmitPreviewed(preview, owner);
    }

    /// <summary>Submits the previewed transaction unchanged and checks the preview read the same outcome.</summary>
    private async Task<LoanBrokerCoverOutcome> SubmitPreviewed(TransactionPreview<LoanBrokerCoverOutcome> preview, XrplWallet wallet)
    {
        Assert.IsTrue(preview.WouldSucceed, preview.EngineResult);

        TransactionSummary result = await client.SubmitAndWait(preview.Transaction, wallet, false);
        ValidateResult(result);

        LoanBrokerCoverOutcome previewed = preview.Outcome;
        (_, LOVault vault) = await Read(previewed.LoanBrokerId, previewed.BrokerAfter.VaultID);
        LoanBrokerCoverOutcome actual = LoanBrokerCoverOutcome.FromMetadata(
            (ITransactionCommon)preview.Transaction, result.Meta, vault, await LedgerRules.FromNodeAsync(client));
        AssertSame(previewed, actual);
        return actual;
    }

    private static void AssertSame(LoanBrokerCoverOutcome expected, LoanBrokerCoverOutcome actual)
    {
        Assert.AreEqual(expected.LoanBrokerId, actual.LoanBrokerId);
        Assert.AreEqual(expected.CoverBefore, actual.CoverBefore, "cover before");
        Assert.AreEqual(expected.CoverAfter, actual.CoverAfter, "cover after");
        Assert.AreEqual(expected.MinimumCover, actual.MinimumCover, "minimum cover");
        Assert.AreEqual(expected.MaxWithdrawable, actual.MaxWithdrawable, "withdrawable");
    }

    private async Task<(LOLoanBroker Broker, LOVault Vault)> Read(string brokerId, string vaultId)
    {
        LOLoanBroker broker = (LOLoanBroker)(await client.LedgerEntry(new LedgerEntryRequest { Index = brokerId }).Typed()).Node;
        LOVault vault = (LOVault)(await client.LedgerEntry(new LedgerEntryRequest { Index = vaultId }).Typed()).Node;
        return (broker, vault);
    }

    /// <summary>
    /// A vault of <paramref name="asset"/> owned by <paramref name="owner"/>, a broker on it that
    /// must keep 12.345% of its debt as cover, the cover, and one loan to <paramref name="borrower"/>.
    /// </summary>
    private async Task<(string BrokerId, string VaultId)> CreateCoveredBrokerWithLoan(
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

        LoanSet loan = new LoanSet
        {
            Account = owner.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = borrower.ClassicAddress,
            PrincipalRequested = principal,
            InterestRate = 25_000,
            PaymentTotal = 4,
            PaymentInterval = 120,
            GracePeriod = 60,
        };
        ValidateResult(await SubmitLoanSetWithCounterpartySig(client, loan, owner, borrower));

        return (brokerId, vaultId);
    }

    private async Task FundIou(XrplWallet issuer, IssuedCurrency usd, params XrplWallet[] holders)
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

    /// <summary>An MPT the issuer can claw back, held and funded by every holder.</summary>
    private async Task<string> CreateMpt(XrplWallet issuer, params XrplWallet[] holders)
    {
        MPTokenIssuanceCreate create = await client.Autofill(new MPTokenIssuanceCreate
        {
            Account = issuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer | MPTokenIssuanceCreateFlags.tfMPTCanClawback,
        });
        TransactionSummary created = await client.SubmitAndWait(create, issuer, true);
        ValidateResult(created);
        string issuanceId = created.Meta?.MptIssuanceId;
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID should be present in metadata");

        foreach (XrplWallet holder in holders)
        {
            MPTokenAuthorize authorize = await client.Autofill(new MPTokenAuthorize
            {
                Account = holder.ClassicAddress,
                MPTokenIssuanceID = issuanceId,
            });
            ValidateResult(await client.SubmitAndWait(authorize, holder, true));

            Payment funding = await client.Autofill(new Payment
            {
                Account = issuer.ClassicAddress,
                Destination = holder.ClassicAddress,
                Amount = new Currency { MPTokenIssuanceID = issuanceId, Value = "5000000" },
            });
            ValidateResult(await client.SubmitAndWait(funding, issuer, true));
        }

        return issuanceId;
    }

    private static string Text(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
