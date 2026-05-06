using System;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// Shared setup and helpers for AMM lifecycle integration tests.
/// Each derived class gets its own static IXrplClient via <see cref="GetClient"/>.
/// </summary>
public abstract class TestIAMMBase
{
    public TestContext TestContext { get; set; }

    protected abstract IXrplClient GetClient();

    protected XrplWallet walletIssuer;
    protected XrplWallet walletHolder;
    protected const string CurrencyCode = "AML";
    protected static TestNodeType nodeType = TestNodeType.Standalone;

    [TestInitialize]
    public async Task TestInitialize()
    {
        walletIssuer = XrplWallet.Generate();
        walletHolder = XrplWallet.Generate();

        Console.WriteLine($"Test: {TestContext.TestName}");
        Console.WriteLine($"Issuer: {walletIssuer.ClassicAddress}");
        Console.WriteLine($"Holder: {walletHolder.ClassicAddress}");

        await IntegrationTestConfig.TryFundWalletsAsync(GetClient(), nodeType, walletIssuer, walletHolder);

        await SetupIssuerFlags();
        await SetupHolderTrustLine();
        await IssueTokensToHolder("10000");
    }

    #region Setup Methods

    protected async Task SetupIssuerFlags()
    {
        AccountSet rippleSet = new AccountSet
        {
            Account = walletIssuer.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfDefaultRipple
        };

        ITransactionRequest autofilled = await GetClient().Autofill(rippleSet);
        TransactionSummary res = await GetClient().SubmitAndWait(autofilled, walletIssuer, true);
        Console.WriteLine($"Default ripple flag: {res.Meta?.TransactionResult}");
    }

    protected async Task SetupHolderTrustLine()
    {
        TrustSet trustSet = new TrustSet
        {
            Account = walletHolder.ClassicAddress,
            LimitAmount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = "1000000000"
            }
        };

        ITransactionRequest autofilled = await GetClient().Autofill(trustSet);
        TransactionSummary res = await GetClient().SubmitAndWait(autofilled, walletHolder, true);
        Console.WriteLine($"Trust line: {res.Meta?.TransactionResult}");
    }

    protected async Task IssueTokensToHolder(string amount)
    {
        Payment payment = new Payment
        {
            Account = walletIssuer.ClassicAddress,
            Destination = walletHolder.ClassicAddress,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = amount
            }
        };

        ITransactionRequest autofilled = await GetClient().Autofill(payment);
        TransactionSummary res = await GetClient().SubmitAndWait(autofilled, walletIssuer, true);
        Console.WriteLine($"Issue tokens: {res.Meta?.TransactionResult}");
    }

    protected async Task<TransactionSummary> CreatePool(string tokenAmount = "1000", decimal xrpAmount = 10m)
    {
        AMMCreate ammCreate = new AMMCreate
        {
            Account = walletHolder.ClassicAddress,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = tokenAmount
            },
            Amount2 = new Currency { ValueAsXrp = xrpAmount },
            TradingFee = 500
        };

        ITransactionRequest autofilled = await GetClient().Autofill(ammCreate);
        TransactionSummary res = await GetClient().SubmitAndWait(autofilled, walletHolder, true);
        Console.WriteLine($"AMM create: {res.Meta?.TransactionResult}");
        return res;
    }

    protected IssuedCurrency TokenAsset => new IssuedCurrency
    {
        Currency = CurrencyCode,
        Issuer = walletIssuer.ClassicAddress
    };

    protected IssuedCurrency XrpAsset => new IssuedCurrency
    {
        Currency = "XRP"
    };

    protected async Task<AMMInfoResponse> GetAmmInfo()
    {
        return await GetClient().AmmInfo(new AMMInfoRequest
        {
            Asset = TokenAsset,
            Asset2 = XrpAsset
        });
    }

    protected static void AssertSuccess(TransactionSummary res, string context)
    {
        string result = res.Meta?.TransactionResult;
        Assert.IsTrue(
            result == "tesSUCCESS" || result == "terQUEUED",
            $"{context} failed: {result}");
    }

    protected async Task<XrplWallet> SetupSecondHolder(string tokenAmount = "5000")
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(GetClient(), wallet, nodeType);

        TrustSet trustSet = new TrustSet
        {
            Account = wallet.ClassicAddress,
            LimitAmount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = "1000000000"
            }
        };
        ITransactionRequest autoTrust = await GetClient().Autofill(trustSet);
        await GetClient().SubmitAndWait(autoTrust, wallet, true);

        Payment pay = new Payment
        {
            Account = walletIssuer.ClassicAddress,
            Destination = wallet.ClassicAddress,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = tokenAmount
            }
        };
        ITransactionRequest autoPay = await GetClient().Autofill(pay);
        await GetClient().SubmitAndWait(autoPay, walletIssuer, true);

        return wallet;
    }

    protected async Task DepositSecondHolder(XrplWallet secondHolder, decimal lpFraction)
    {
        AMMInfoResponse info = await GetAmmInfo();
        decimal depositLp = info.Amm.LPTokenBalance.ValueAsNumber * lpFraction;

        AMMDeposit deposit = new AMMDeposit
        {
            Account = secondHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            LPTokenOut = new Currency
            {
                CurrencyCode = info.Amm.LPTokenBalance.CurrencyCode,
                Issuer = info.Amm.LPTokenBalance.Issuer,
                ValueAsNumber = depositLp
            },
            Flags = AMMDepositFlags.tfLPToken
        };

        ITransactionRequest autofilled = await GetClient().Autofill(deposit);
        TransactionSummary depositRes = await GetClient().SubmitAndWait(autofilled, secondHolder, true);
        AssertSuccess(depositRes, "Second holder deposit");
        Console.WriteLine($"Second holder deposited {lpFraction:P0} of pool LP");
    }

    #endregion

    protected static async Task<IXrplClient> CreateStandaloneClient()
    {
        return await IntegrationTestConfig.CreateClientAsync(TestNodeType.Standalone);
    }
}
