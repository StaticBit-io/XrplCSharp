using System;
using System.Collections.Generic;
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
/// <see cref="VaultShares"/> against the node: every deposit and withdrawal is quoted offline,
/// previewed through <c>simulate</c>, compared, and - when the node accepts it - submitted, so the
/// next step starts from the vault the previous one left.
/// </summary>
[TestClass]
[TestCategory("Vault")]
public class TestIVaultShares : TestILoanBase
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

    private enum Kind
    {
        Deposit,
        Withdraw,
        Redeem,
    }

    [TestMethod]
    public async Task TestVaultShares_Xrp_MatchWhatTheNodeMoves()
    {
        XrplWallet owner = XrplWallet.Generate();
        XrplWallet alice = XrplWallet.Generate();
        XrplWallet bob = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, owner, alice, bob);
        IssuedCurrency xrp = new IssuedCurrency { Currency = "XRP" };
        string vaultId = await CreateVault(owner, xrp);

        await Exercise(vaultId, value => new Currency { Value = value, CurrencyCode = "XRP" }, new[]
        {
            (Kind.Deposit, alice, "12345679"),
            (Kind.Deposit, bob, "3333333"),
            (Kind.Withdraw, alice, "1000001"),
            (Kind.Redeem, bob, "1111111"),
            (Kind.Withdraw, bob, "99999999"),
            (Kind.Deposit, alice, "1"),
            (Kind.Redeem, bob, "0"),
            (Kind.Redeem, alice, "ALL"),
            (Kind.Redeem, bob, "ALL"),
        });
    }

    [TestMethod]
    public async Task TestVaultShares_Iou_MatchWhatTheNodeMoves()
    {
        XrplWallet issuer = XrplWallet.Generate();
        XrplWallet alice = XrplWallet.Generate();
        XrplWallet bob = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, issuer, alice, bob);
        IssuedCurrency usd = new IssuedCurrency { Currency = "USD", Issuer = issuer.ClassicAddress };
        await FundIou(issuer, usd, alice, bob);
        string vaultId = await CreateVault(issuer, usd);

        // Deposits finer than the vault's scale once its total is large, so the amount credited is
        // clamped below the amount the shares were minted for and the share price drifts.
        await Exercise(vaultId, value => new Currency { Value = value, CurrencyCode = usd.Currency, Issuer = usd.Issuer }, new[]
        {
            (Kind.Deposit, alice, "1234.5678"),
            (Kind.Deposit, bob, "987654.321"),
            (Kind.Deposit, alice, "0.123456789"),
            (Kind.Deposit, alice, "0.0000001"),
            (Kind.Deposit, bob, "33.333333333"),
            (Kind.Withdraw, alice, "100.0000009"),
            (Kind.Redeem, bob, "123456789"),
            (Kind.Withdraw, alice, "0.00000001"),
            (Kind.Deposit, alice, "7777.777777"),
            (Kind.Withdraw, bob, "5000000"),
            (Kind.Redeem, alice, "ALL"),
            (Kind.Redeem, bob, "ALL"),
        });
    }

    [TestMethod]
    public async Task TestVaultShares_Mpt_MatchWhatTheNodeMoves()
    {
        XrplWallet issuer = XrplWallet.Generate();
        XrplWallet alice = XrplWallet.Generate();
        XrplWallet bob = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, issuer, alice, bob);
        string mptId = await FundMpt(issuer, alice, bob);
        string vaultId = await CreateVault(issuer, new IssuedCurrency { MptIssuanceId = mptId });

        await Exercise(vaultId, value => new Currency { Value = value, MPTokenIssuanceID = mptId }, new[]
        {
            (Kind.Deposit, alice, "997"),
            (Kind.Deposit, bob, "3"),
            (Kind.Withdraw, alice, "13"),
            (Kind.Redeem, bob, "1"),
            (Kind.Withdraw, bob, "5"),
            (Kind.Redeem, alice, "ALL"),
            (Kind.Redeem, bob, "ALL"),
        });
    }

    [TestMethod]
    public async Task TestVaultShares_WithUnrealizedLoss_MatchWhatTheNodeMoves()
    {
        // An impaired loan books its exposure as LossUnrealized, and withdrawals are priced on the
        // assets less that loss. Withdrawals open in the redemption phase of a closed-ended vault,
        // so the investment window is kept to the minimum.
        XrplWallet owner = XrplWallet.Generate();
        XrplWallet alice = XrplWallet.Generate();
        XrplWallet bob = XrplWallet.Generate();
        XrplWallet borrower = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, owner, alice, bob, borrower);
        await IntegrationTestConfig.EnsureBalanceAsync(client, alice, 200m);
        Func<string, Currency> xrp = value => new Currency { Value = value, CurrencyCode = "XRP" };

        VaultCreate create = await client.Autofill(await BuildBrokerVaultAsync(
            client, owner.ClassicAddress, new IssuedCurrency { Currency = "XRP" }, investmentWindowSeconds: 190));
        TransactionSummary created = await client.SubmitAndWait(create, owner, true);
        ValidateResult(created);
        string vaultId = GetCreatedObjectId(created, LedgerEntryType.Vault);

        Dictionary<string, ulong> shares = new Dictionary<string, ulong>();
        await Exercise(vaultId, xrp, new[]
        {
            (Kind.Deposit, alice, "60000000"),
            (Kind.Deposit, bob, "40000007"),
        }, shares);

        LoanBrokerSet brokerTx = await client.Autofill(new LoanBrokerSet { Account = owner.ClassicAddress, VaultID = vaultId });
        TransactionSummary brokerResult = await client.SubmitAndWait(brokerTx, owner, true);
        ValidateResult(brokerResult);
        string brokerId = GetCreatedObjectId(brokerResult, LedgerEntryType.LoanBroker);
        await EnterInvestmentPhaseAsync(client, vaultId);

        LoanSet loanTx = new LoanSet
        {
            Account = owner.ClassicAddress,
            LoanBrokerID = brokerId,
            Counterparty = borrower.ClassicAddress,
            PrincipalRequested = 30_000_000,
            InterestRate = 50_000,
            PaymentTotal = 1,
            PaymentInterval = 60,
            GracePeriod = 60,
        };
        TransactionSummary loan = await SubmitLoanSetWithCounterpartySig(client, loanTx, owner, borrower);
        ValidateResult(loan);
        string loanId = LoanSetOutcome.FromMetadata(loanTx, loan.Meta).LoanId;

        // Since fixCleanup3_4_0 only a late loan can be impaired.
        LOLoan entry = (LOLoan)(await client.LedgerEntry(new LedgerEntryRequest { Index = loanId }).Typed()).Node;
        await IntegrationTestConfig.WaitForCloseTimeAsync(client, entry.NextPaymentDueDate.Value.AddSeconds(2), nodeType);

        LoanManage impair = await client.Autofill(new LoanManage
        {
            Account = owner.ClassicAddress,
            LoanID = loanId,
            Flags = LoanManageFlags.tfLoanImpair,
        });
        ValidateResult(await client.SubmitAndWait(impair, owner, true));

        LOVault vault = (LOVault)(await client.LedgerEntry(new LedgerEntryRequest { Index = vaultId }).Typed()).Node;
        Assert.IsTrue((vault.LossUnrealized ?? XrplNumber.Zero) > XrplNumber.Zero, "the impaired loan books a loss");
        if (vault.RedemptionDate is DateTime redemption)
            await IntegrationTestConfig.WaitForCloseTimeAsync(client, redemption.AddSeconds(1), nodeType);

        await Exercise(vaultId, xrp, new[]
        {
            (Kind.Withdraw, alice, "5000001"),
            (Kind.Redeem, bob, "1234567"),
            (Kind.Withdraw, bob, "99999999"),
            (Kind.Withdraw, alice, "1"),
            (Kind.Redeem, alice, "ALL"),
        }, shares);
    }

    /// <summary>
    /// Runs the steps against one vault. "ALL" redeems every share the account holds; a step the
    /// node refuses must be refused offline with the same result. <paramref name="shares"/> carries
    /// the holdings from an earlier run on the same vault.
    /// </summary>
    private static async Task Exercise(
        string vaultId,
        Func<string, Currency> assetAmount,
        (Kind Kind, XrplWallet Account, string Value)[] steps,
        Dictionary<string, ulong> shares = null)
    {
        LedgerRules rules = await LedgerRules.FromNodeAsync(client);
        shares ??= new Dictionary<string, ulong>();
        int accepted = 0;

        for (int i = 0; i < steps.Length; i++)
        {
            (Kind kind, XrplWallet account, string value) = steps[i];
            LOVault vault = (LOVault)(await client.LedgerEntry(new LedgerEntryRequest { Index = vaultId }).Typed()).Node;
            LOMPTokenIssuance issuance = (LOMPTokenIssuance)(await client.LedgerEntry(new LedgerEntryRequest { MptIssuance = vault.ShareMPTID }).Typed()).Node;
            ulong outstanding = issuance.OutstandingAmount ?? 0;
            ulong held = shares.TryGetValue(account.ClassicAddress, out ulong h) ? h : 0;
            if (value == "ALL")
                value = held.ToString(CultureInfo.InvariantCulture);

            string at = $"step {i + 1}: {kind} {value}";
            VaultQuote quote;
            TransactionPreview<VaultOutcome> preview;
            if (kind == Kind.Deposit)
            {
                XrplNumber? balance = vault.Asset.Issuer != null && vault.Asset.MptIssuanceId == null
                    ? await IouBalance(account, vault.Asset)
                    : null;
                quote = VaultShares.Deposit(vault, outstanding, XrplNumber.Parse(value), balance, rules);
                preview = await client.PreviewVaultDeposit(new VaultDeposit
                {
                    Account = account.ClassicAddress,
                    VaultID = vaultId,
                    Amount = assetAmount(value),
                });
            }
            else
            {
                quote = kind == Kind.Withdraw
                    ? VaultShares.WithdrawAssets(vault, outstanding, held, XrplNumber.Parse(value), rules)
                    : VaultShares.RedeemShares(vault, outstanding, held, ulong.Parse(value, CultureInfo.InvariantCulture), rules);
                if (value == "0")
                {
                    // A zero amount never reaches the node: preflight refuses it.
                    Assert.AreEqual("temBAD_AMOUNT", quote.Refusal, at);
                    continue;
                }

                preview = await client.PreviewVaultWithdraw(new VaultWithdraw
                {
                    Account = account.ClassicAddress,
                    VaultID = vaultId,
                    Amount = kind == Kind.Withdraw
                        ? assetAmount(value)
                        : new Currency { Value = value, MPTokenIssuanceID = vault.ShareMPTID },
                });
            }

            if (!preview.WouldSucceed)
            {
                Assert.AreEqual(preview.EngineResult, quote.Refusal, $"{at}: {quote.RefusalReason}");
                continue;
            }

            Assert.IsTrue(quote.IsAccepted, $"{at}: offline {quote.Refusal} ({quote.RefusalReason})");
            Assert.AreEqual(Math.Abs(preview.Outcome.AccountShareChange), quote.Shares, $"{at}: shares");
            Assert.AreEqual(Math.Abs(preview.Outcome.VaultAssetChange), quote.Assets, $"{at}: assets");

            TransactionSummary result = await client.SubmitAndWait(preview.Transaction, account, false);
            ValidateResult(result);
            VaultOutcome done = VaultOutcome.FromMetadata((ITransactionCommon)preview.Transaction, result.Meta);
            shares[account.ClassicAddress] = (ulong)((decimal)held + done.AccountShareChange);
            accepted++;
        }

        Assert.IsTrue(accepted >= steps.Length / 2, $"only {accepted} of {steps.Length} steps went through");
    }

    private static async Task<string> CreateVault(XrplWallet owner, IssuedCurrency asset)
    {
        VaultCreate create = await client.Autofill(new VaultCreate { Account = owner.ClassicAddress, Asset = asset });
        TransactionSummary created = await client.SubmitAndWait(create, owner, true);
        ValidateResult(created);
        return GetCreatedObjectId(created, LedgerEntryType.Vault);
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

    private static async Task<string> FundMpt(XrplWallet issuer, params XrplWallet[] holders)
    {
        MPTokenIssuanceCreate create = await client.Autofill(new MPTokenIssuanceCreate
        {
            Account = issuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer,
        });
        TransactionSummary created = await client.SubmitAndWait(create, issuer, true);
        ValidateResult(created);
        string mptId = created.Meta.MptIssuanceId;

        foreach (XrplWallet holder in holders)
        {
            MPTokenAuthorize authorize = await client.Autofill(new MPTokenAuthorize
            {
                Account = holder.ClassicAddress,
                MPTokenIssuanceID = mptId,
            });
            ValidateResult(await client.SubmitAndWait(authorize, holder, true));

            Payment funding = await client.Autofill(new Payment
            {
                Account = issuer.ClassicAddress,
                Destination = holder.ClassicAddress,
                Amount = new Currency { Value = "10000", MPTokenIssuanceID = mptId },
            });
            ValidateResult(await client.SubmitAndWait(funding, issuer, true));
        }

        return mptId;
    }

    /// <summary>The account's stored balance on its trust line to the issuer.</summary>
    private static async Task<XrplNumber> IouBalance(XrplWallet account, IssuedCurrency asset)
    {
        AccountLines lines = await client.AccountLines(new AccountLinesRequest(account.ClassicAddress) { Peer = asset.Issuer }).Typed();
        foreach (TrustLine line in lines.TrustLines)
        {
            if (line.Currency == asset.Currency)
                return XrplNumber.Parse(line.Balance);
        }

        return XrplNumber.Zero;
    }
}
