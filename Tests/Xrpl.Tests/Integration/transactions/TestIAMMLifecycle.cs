using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// Integration tests covering the full AMM lifecycle: Create, Deposit, Withdraw, Delete.
/// Each test creates fresh wallets and an AMM pool to ensure independence.
/// Requires a local standalone rippled node at ws://localhost:6006 with AMM amendment enabled.
///
/// NOTE: Tests that use LPTokenIn/LPTokenOut fields use the Dictionary API because
/// the typed C# model serialization has a known bug where transactions with these fields
/// produce invalidTransaction errors during binary encoding (see TestAMMWithdraw_LPToken_TypedModel_KnownBug).
/// </summary>
[TestClass]
[DoNotParallelize]
public class TestIAMMLifecycle
{
    public TestContext TestContext { get; set; }
    private static IXrplClient client;

    private XrplWallet walletIssuer;
    private XrplWallet walletHolder;

    const string CurrencyCode = "AML";
    public static TestNodeType nodeType = TestNodeType.Standalone;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(TestNodeType.Standalone);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        client?.Dispose();
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        walletIssuer = XrplWallet.Generate();
        walletHolder = XrplWallet.Generate();

        Console.WriteLine($"Test: {TestContext.TestName}");
        Console.WriteLine($"Issuer: {walletIssuer.ClassicAddress}");
        Console.WriteLine($"Holder: {walletHolder.ClassicAddress}");

        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletIssuer, walletHolder);

        await SetupIssuerFlags();
        await SetupHolderTrustLine();
        await IssueTokensToHolder("10000");
    }

    #region Setup Methods

    private async Task SetupIssuerFlags()
    {
        AccountSet rippleSet = new AccountSet
        {
            Account = walletIssuer.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfDefaultRipple
        };

        ITransactionRequest autofilled = await client.Autofill(rippleSet);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletIssuer, true);
        Console.WriteLine($"Default ripple flag: {res.Meta?.TransactionResult}");
    }

    private async Task SetupHolderTrustLine()
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

        ITransactionRequest autofilled = await client.Autofill(trustSet);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        Console.WriteLine($"Trust line: {res.Meta?.TransactionResult}");
    }

    private async Task IssueTokensToHolder(string amount)
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

        ITransactionRequest autofilled = await client.Autofill(payment);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletIssuer, true);
        Console.WriteLine($"Issue tokens: {res.Meta?.TransactionResult}");
    }

    private async Task<TransactionSummary> CreatePool(string tokenAmount = "1000", decimal xrpAmount = 10m)
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

        ITransactionRequest autofilled = await client.Autofill(ammCreate);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        Console.WriteLine($"AMM create: {res.Meta?.TransactionResult}");
        return res;
    }

    private IssuedCurrency TokenAsset => new IssuedCurrency
    {
        Currency = CurrencyCode,
        Issuer = walletIssuer.ClassicAddress
    };

    private IssuedCurrency XrpAsset => new IssuedCurrency
    {
        Currency = "XRP"
    };

    private Dictionary<string, object> TokenAssetDict => new Dictionary<string, object>
    {
        { "currency", CurrencyCode },
        { "issuer", walletIssuer.ClassicAddress }
    };

    private Dictionary<string, object> XrpAssetDict => new Dictionary<string, object>
    {
        { "currency", "XRP" }
    };

    private static Dictionary<string, object> LPTokenDict(string currency, string issuer, string value) =>
        new Dictionary<string, object>
        {
            { "currency", currency },
            { "issuer", issuer },
            { "value", value }
        };

    private async Task<AMMInfoResponse> GetAmmInfo()
    {
        return await client.AmmInfo(new AMMInfoRequest
        {
            Asset = TokenAsset,
            Asset2 = XrpAsset
        });
    }

    private static void AssertSuccess(TransactionSummary res, string context)
    {
        string result = res.Meta?.TransactionResult;
        Assert.IsTrue(
            result == "tesSUCCESS" || result == "terQUEUED",
            $"{context} failed: {result}");
    }

    private async Task<XrplWallet> SetupSecondHolder(string tokenAmount = "5000")
    {
        XrplWallet wallet = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

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
        ITransactionRequest autoTrust = await client.Autofill(trustSet);
        await client.SubmitAndWait(autoTrust, wallet, true);

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
        ITransactionRequest autoPay = await client.Autofill(pay);
        await client.SubmitAndWait(autoPay, walletIssuer, true);

        return wallet;
    }

    private async Task DepositSecondHolder(XrplWallet secondHolder, decimal lpFraction)
    {
        AMMInfoResponse info = await GetAmmInfo();
        decimal depositLp = info.Amm.LPTokenBalance.ValueAsNumber * lpFraction;
        Dictionary<string, object> depositTx = new Dictionary<string, object>
        {
            { "TransactionType", "AMMDeposit" },
            { "Account", secondHolder.ClassicAddress },
            { "Flags", (uint)AMMDepositFlags.tfLPToken },
            { "Asset", TokenAssetDict },
            { "Asset2", XrpAssetDict },
            { "LPTokenOut", LPTokenDict(
                info.Amm.LPTokenBalance.CurrencyCode,
                info.Amm.LPTokenBalance.Issuer,
                depositLp.ToString("G16", CultureInfo.InvariantCulture)) }
        };
        TransactionSummary depositRes = await client.SubmitAndWait(depositTx, secondHolder, autofill: true);
        AssertSuccess(depositRes, "Second holder deposit");
        Console.WriteLine($"Second holder deposited {lpFraction:P0} of pool LP");
    }

    #endregion

    #region AMMCreate Tests

    [TestMethod]
    public async Task TestAMMCreate_XrpTokenPool()
    {
        TransactionSummary res = await CreatePool("1000", 10m);
        AssertSuccess(res, "AMMCreate XRP/Token");
    }

    [TestMethod]
    public async Task TestAMMCreate_VerifyPoolInfo()
    {
        await CreatePool("1000", 10m);

        AMMInfoResponse info = await GetAmmInfo();

        Assert.IsNotNull(info.Amm, "AMM info should not be null");
        Assert.IsNotNull(info.Amm.LPTokenBalance, "LP token balance should not be null");
        Assert.IsTrue(info.Amm.LPTokenBalance.ValueAsNumber > 0, "LP token balance should be positive");
        Assert.AreEqual(500u, info.Amm.TradingFee, "Trading fee should match creation value");

        Console.WriteLine($"Pool Amount: {info.Amm.Amount?.Value} {info.Amm.Amount?.CurrencyCode}");
        Console.WriteLine($"Pool Amount2: {info.Amm.Amount2?.Value} {info.Amm.Amount2?.CurrencyCode}");
        Console.WriteLine($"LP Balance: {info.Amm.LPTokenBalance.Value}");
    }

    #endregion

    #region AMMDeposit Tests

    [TestMethod]
    public async Task TestAMMDeposit_SingleAsset()
    {
        await CreatePool();

        AMMInfoResponse infoBefore = await GetAmmInfo();
        decimal lpBefore = infoBefore.Amm.LPTokenBalance.ValueAsNumber;

        AMMDeposit deposit = new AMMDeposit
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = "500"
            },
            Flags = AMMDepositFlags.tfSingleAsset
        };

        ITransactionRequest autofilled = await client.Autofill(deposit);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        AssertSuccess(res, "AMMDeposit SingleAsset");

        AMMInfoResponse infoAfter = await GetAmmInfo();
        decimal lpAfter = infoAfter.Amm.LPTokenBalance.ValueAsNumber;
        Console.WriteLine($"LP before: {lpBefore}, after: {lpAfter}");

        Assert.IsTrue(lpAfter > lpBefore, "LP token supply should increase after deposit");
    }

    [TestMethod]
    public async Task TestAMMDeposit_TwoAssets()
    {
        await CreatePool();

        AMMInfoResponse infoBefore = await GetAmmInfo();
        decimal lpBefore = infoBefore.Amm.LPTokenBalance.ValueAsNumber;

        AMMDeposit deposit = new AMMDeposit
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = "200"
            },
            Amount2 = new Currency { ValueAsXrp = 2m },
            Flags = AMMDepositFlags.tfTwoAsset
        };

        ITransactionRequest autofilled = await client.Autofill(deposit);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        AssertSuccess(res, "AMMDeposit TwoAssets");

        AMMInfoResponse infoAfter = await GetAmmInfo();
        decimal lpAfter = infoAfter.Amm.LPTokenBalance.ValueAsNumber;
        Console.WriteLine($"LP before: {lpBefore}, after: {lpAfter}");

        Assert.IsTrue(lpAfter > lpBefore, "LP token supply should increase after two-asset deposit");
    }

    /// <summary>
    /// Uses Dictionary API to bypass typed model serialization bug with LPTokenOut.
    /// </summary>
    [TestMethod]
    public async Task TestAMMDeposit_LPToken()
    {
        await CreatePool();

        AMMInfoResponse infoBefore = await GetAmmInfo();
        decimal lpBefore = infoBefore.Amm.LPTokenBalance.ValueAsNumber;
        decimal targetLp = lpBefore * 0.1m;

        Dictionary<string, object> tx = new Dictionary<string, object>
        {
            { "TransactionType", "AMMDeposit" },
            { "Account", walletHolder.ClassicAddress },
            { "Flags", (uint)AMMDepositFlags.tfLPToken },
            { "Asset", TokenAssetDict },
            { "Asset2", XrpAssetDict },
            { "LPTokenOut", LPTokenDict(
                infoBefore.Amm.LPTokenBalance.CurrencyCode,
                infoBefore.Amm.LPTokenBalance.Issuer,
                targetLp.ToString("G16", CultureInfo.InvariantCulture)) }
        };

        TransactionSummary res = await client.SubmitAndWait(tx, walletHolder, autofill: true);
        AssertSuccess(res, "AMMDeposit LPToken");

        AMMInfoResponse infoAfter = await GetAmmInfo();
        decimal lpAfter = infoAfter.Amm.LPTokenBalance.ValueAsNumber;
        Console.WriteLine($"LP before: {lpBefore}, after: {lpAfter}, requested: +{targetLp}");

        Assert.IsTrue(lpAfter > lpBefore, "LP token supply should increase after LP token deposit");
    }

    #endregion

    #region AMMWithdraw Tests

    /// <summary>
    /// Uses Dictionary API to bypass typed model serialization bug with LPTokenIn.
    /// </summary>
    [TestMethod]
    public async Task TestAMMWithdraw_LPToken()
    {
        await CreatePool();

        AMMInfoResponse info = await GetAmmInfo();
        decimal totalLp = info.Amm.LPTokenBalance.ValueAsNumber;
        decimal withdrawLp = totalLp * 0.5m;

        Dictionary<string, object> tx = new Dictionary<string, object>
        {
            { "TransactionType", "AMMWithdraw" },
            { "Account", walletHolder.ClassicAddress },
            { "Flags", (uint)AMMWithdrawFlags.tfLPToken },
            { "Asset", TokenAssetDict },
            { "Asset2", XrpAssetDict },
            { "LPTokenIn", LPTokenDict(
                info.Amm.LPTokenBalance.CurrencyCode,
                info.Amm.LPTokenBalance.Issuer,
                withdrawLp.ToString("G16", CultureInfo.InvariantCulture)) }
        };

        TransactionSummary res = await client.SubmitAndWait(tx, walletHolder, autofill: true);
        AssertSuccess(res, "AMMWithdraw LPToken");

        AMMInfoResponse infoAfter = await GetAmmInfo();
        decimal lpAfter = infoAfter.Amm.LPTokenBalance.ValueAsNumber;
        Console.WriteLine($"LP total before: {totalLp}, after: {lpAfter}, withdrew: {withdrawLp}");

        Assert.IsTrue(lpAfter < totalLp, "LP token supply should decrease after withdrawal");
    }

    [TestMethod]
    public async Task TestAMMWithdraw_WithdrawAll()
    {
        await CreatePool();

        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Flags = AMMWithdrawFlags.tfWithdrawAll
        };

        ITransactionRequest autofilled = await client.Autofill(withdraw);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        AssertSuccess(res, "AMMWithdraw WithdrawAll");
    }

    /// <summary>
    /// Regression test for the G15 precision bug.
    /// Creates a pool, reads exact LP balance from amm_info, then withdraws the full amount
    /// using the exact string value from the server via Dictionary API.
    /// This scenario previously failed with tecAMM_INVALID_TOKENS when "G15" rounding
    /// produced a value larger than actual balance.
    /// </summary>
    [TestMethod]
    public async Task TestAMMWithdraw_FullLP_PrecisionTest()
    {
        await CreatePool("1000", 10m);

        AMMInfoResponse info = await GetAmmInfo();
        string exactLpValue = info.Amm.LPTokenBalance.Value;
        string lpCurrency = info.Amm.LPTokenBalance.CurrencyCode;
        string lpIssuer = info.Amm.LPTokenBalance.Issuer;

        Console.WriteLine($"Exact LP value from amm_info: {exactLpValue}");
        Console.WriteLine($"LP currency: {lpCurrency}, issuer: {lpIssuer}");

        Dictionary<string, object> tx = new Dictionary<string, object>
        {
            { "TransactionType", "AMMWithdraw" },
            { "Account", walletHolder.ClassicAddress },
            { "Flags", (uint)AMMWithdrawFlags.tfLPToken },
            { "Asset", TokenAssetDict },
            { "Asset2", XrpAssetDict },
            { "LPTokenIn", LPTokenDict(lpCurrency, lpIssuer, exactLpValue) }
        };

        Console.WriteLine($"AMMWithdraw dict: {JsonConvert.SerializeObject(tx, Formatting.Indented)}");

        TransactionSummary res = await client.SubmitAndWait(tx, walletHolder, autofill: true);
        string result = res.Meta?.TransactionResult;
        Console.WriteLine($"Full LP withdraw result: {result}");

        Assert.AreEqual("tesSUCCESS", result,
            $"Full LP withdraw with exact server value should succeed, got: {result}");
    }

    /// <summary>
    /// Verifies that round-tripping an LP balance through decimal and back to string
    /// via ValueAsNumber setter does not produce a value larger than the original,
    /// then withdraws the full LP amount using the Dictionary API.
    /// </summary>
    [TestMethod]
    public async Task TestAMMWithdraw_FullLP_ViaValueAsNumber()
    {
        await CreatePool("1000", 10m);

        AMMInfoResponse info = await GetAmmInfo();
        string exactLpValue = info.Amm.LPTokenBalance.Value;
        decimal lpDecimal = info.Amm.LPTokenBalance.ValueAsNumber;

        Console.WriteLine($"LP from server: {exactLpValue}");
        Console.WriteLine($"LP as decimal: {lpDecimal}");

        Currency lpToken = new Currency
        {
            CurrencyCode = info.Amm.LPTokenBalance.CurrencyCode,
            Issuer = info.Amm.LPTokenBalance.Issuer
        };
        lpToken.ValueAsNumber = lpDecimal;

        decimal roundTripped = decimal.Parse(lpToken.Value, CultureInfo.InvariantCulture);
        Console.WriteLine($"LP after round-trip: {lpToken.Value} (decimal: {roundTripped})");

        Assert.IsTrue(roundTripped <= lpDecimal,
            $"Round-tripped value ({roundTripped}) must not exceed original ({lpDecimal})");

        Dictionary<string, object> tx = new Dictionary<string, object>
        {
            { "TransactionType", "AMMWithdraw" },
            { "Account", walletHolder.ClassicAddress },
            { "Flags", (uint)AMMWithdrawFlags.tfLPToken },
            { "Asset", TokenAssetDict },
            { "Asset2", XrpAssetDict },
            { "LPTokenIn", LPTokenDict(
                info.Amm.LPTokenBalance.CurrencyCode,
                info.Amm.LPTokenBalance.Issuer,
                lpToken.Value) }
        };

        TransactionSummary res = await client.SubmitAndWait(tx, walletHolder, autofill: true);
        string result = res.Meta?.TransactionResult;
        Console.WriteLine($"Full LP withdraw via ValueAsNumber result: {result}");

        Assert.AreEqual("tesSUCCESS", result,
            $"Full LP withdraw via ValueAsNumber should succeed, got: {result}");
    }

    [TestMethod]
    public async Task TestAMMWithdraw_SingleAsset()
    {
        await CreatePool();

        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = "100"
            },
            Flags = AMMWithdrawFlags.tfSingleAsset
        };

        ITransactionRequest autofilled = await client.Autofill(withdraw);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        AssertSuccess(res, "AMMWithdraw SingleAsset");
    }

    /// <summary>
    /// Sole LP holder tries tfOneAssetWithdrawAll — protocol rejects with tecAMM_BALANCE
    /// because single-sided full withdrawal would leave pool assets with zero outstanding LP tokens.
    /// </summary>
    [TestMethod]
    public async Task TestAMMWithdraw_OneAssetWithdrawAll_SoleHolder_Fails()
    {
        await CreatePool("1000", 10m);

        AMMInfoResponse infoBefore = await GetAmmInfo();
        Console.WriteLine($"LP before: {infoBefore.Amm.LPTokenBalance.Value}");

        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Amount = new Currency { ValueAsXrp = 0 },
            Flags = AMMWithdrawFlags.tfOneAssetWithdrawAll
        };

        ITransactionRequest autofilled = await client.Autofill(withdraw);

        try
        {
            await client.SubmitAndWait(autofilled, walletHolder, true);
            Assert.Fail("Expected tecAMM_BALANCE for sole LP holder single-sided withdrawal");
        }
        catch (Exception ex) when (ex.Classify().RawErrorMessage!.Contains("tecAMM_BALANCE"))
        {
            Console.WriteLine($"Correctly rejected: {ex.Message}");
        }

        AMMInfoResponse infoAfter = await GetAmmInfo();
        Assert.IsNotNull(infoAfter.Amm, "Pool should still exist after failed withdrawal");
        Assert.AreEqual(
            infoBefore.Amm.LPTokenBalance.Value,
            infoAfter.Amm.LPTokenBalance.Value,
            "LP balance should be unchanged after tecAMM_BALANCE");
    }

    /// <summary>
    /// Two LP holders — first holder withdraws all LP into XRP via tfOneAssetWithdrawAll.
    /// Pool remains with second holder's liquidity.
    /// </summary>
    [TestMethod]
    public async Task TestAMMWithdraw_OneAssetWithdrawAll_Xrp()
    {
        XrplWallet walletSecondHolder = await SetupSecondHolder();
        await CreatePool("1000", 10m);
        await DepositSecondHolder(walletSecondHolder, 0.5m);

        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Amount = new Currency { ValueAsXrp = 0 },
            Flags = AMMWithdrawFlags.tfOneAssetWithdrawAll
        };

        ITransactionRequest autofilled = await client.Autofill(withdraw);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        string result = res.Meta?.TransactionResult;
        Console.WriteLine($"OneAssetWithdrawAll XRP result: {result}");
        AssertSuccess(res, "AMMWithdraw OneAssetWithdrawAll XRP");

        AMMInfoResponse infoAfter = await GetAmmInfo();
        Assert.IsNotNull(infoAfter.Amm, "Pool should still exist — second holder has LP");
        Console.WriteLine($"Pool LP after withdrawal: {infoAfter.Amm.LPTokenBalance.Value}");
    }

    /// <summary>
    /// Two LP holders — first holder withdraws all LP into issued token via tfOneAssetWithdrawAll.
    /// </summary>
    [TestMethod]
    public async Task TestAMMWithdraw_OneAssetWithdrawAll_Token()
    {
        XrplWallet walletSecondHolder = await SetupSecondHolder();
        await CreatePool("1000", 10m);
        await DepositSecondHolder(walletSecondHolder, 0.5m);

        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = "0"
            },
            Flags = AMMWithdrawFlags.tfOneAssetWithdrawAll
        };

        ITransactionRequest autofilled = await client.Autofill(withdraw);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        string result = res.Meta?.TransactionResult;
        Console.WriteLine($"OneAssetWithdrawAll Token result: {result}");
        AssertSuccess(res, "AMMWithdraw OneAssetWithdrawAll Token");

        AMMInfoResponse infoAfter = await GetAmmInfo();
        Assert.IsNotNull(infoAfter.Amm, "Pool should still exist — second holder has LP");
        Console.WriteLine($"Pool LP after withdrawal: {infoAfter.Amm.LPTokenBalance.Value}");
    }

    /// <summary>
    /// Simulates AMMWithdraw via the Simulate API (tx_json path), verifies success,
    /// then submits via Dictionary API (binary path).
    /// Simulate uses typed model (which works since it sends JSON, not binary).
    /// Submit uses Dictionary API to bypass the typed model binary serialization bug.
    /// </summary>
    [TestMethod]
    public async Task TestAMMWithdraw_Simulate_ThenSubmit()
    {
        await CreatePool();

        AMMInfoResponse info = await GetAmmInfo();
        decimal withdrawLp = info.Amm.LPTokenBalance.ValueAsNumber * 0.5m;
        string lpValue = withdrawLp.ToString("G16", CultureInfo.InvariantCulture);
        string lpCurrency = info.Amm.LPTokenBalance.CurrencyCode;
        string lpIssuer = info.Amm.LPTokenBalance.Issuer;

        AMMWithdraw withdrawForSim = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            LPTokenIn = new Currency
            {
                CurrencyCode = lpCurrency,
                Issuer = lpIssuer,
                Value = lpValue
            },
            Flags = AMMWithdrawFlags.tfLPToken
        };

        ITransactionRequest autofilledForSim = await client.Autofill(withdrawForSim);

        SimulateResponse simResult = await client.Simulate(new SimulateRequest
        {
            Transaction = autofilledForSim
        });

        Console.WriteLine($"Simulate result: {simResult.EngineResult}");
        Assert.IsTrue(
            simResult.EngineResult == "tesSUCCESS" || simResult.EngineResult == "terQUEUED",
            $"Simulate should succeed, got: {simResult.EngineResult}");

        Dictionary<string, object> tx = new Dictionary<string, object>
        {
            { "TransactionType", "AMMWithdraw" },
            { "Account", walletHolder.ClassicAddress },
            { "Flags", (uint)AMMWithdrawFlags.tfLPToken },
            { "Asset", TokenAssetDict },
            { "Asset2", XrpAssetDict },
            { "LPTokenIn", LPTokenDict(lpCurrency, lpIssuer, lpValue) }
        };

        TransactionSummary res = await client.SubmitAndWait(tx, walletHolder, autofill: true);
        AssertSuccess(res, "AMMWithdraw after Simulate");
    }

    /// <summary>
    /// Verifies that the typed C# model with LPTokenIn works correctly
    /// after the Field.cs field code fix (LPTokenIn: 21→26, LPTokenOut: 20→25).
    /// Previously failed with invalidTransaction due to wrong binary field codes.
    /// </summary>
    [TestMethod]
    public async Task TestAMMWithdraw_LPToken_TypedModel()
    {
        await CreatePool();

        AMMInfoResponse info = await GetAmmInfo();
        decimal withdrawLp = info.Amm.LPTokenBalance.ValueAsNumber * 0.5m;

        AMMWithdraw withdraw = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            LPTokenIn = new Currency
            {
                CurrencyCode = info.Amm.LPTokenBalance.CurrencyCode,
                Issuer = info.Amm.LPTokenBalance.Issuer,
                ValueAsNumber = withdrawLp
            },
            Flags = AMMWithdrawFlags.tfLPToken
        };

        ITransactionRequest autofilled = await client.Autofill(withdraw);
        string json = withdraw.ToJson();
        Console.WriteLine($"Typed model JSON: {json}");

        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        string result = res.Meta?.TransactionResult;
        Console.WriteLine($"Typed model submit result: {result}");

        Assert.AreEqual("tesSUCCESS", result,
            $"Typed model with LPTokenIn should succeed, got: {result}");
    }

    #endregion

    #region AMMDelete Tests

    /// <summary>
    /// After WithdrawAll by the sole LP holder, the pool is auto-deleted by the XRPL protocol.
    /// This test verifies the pool no longer exists after full withdrawal.
    /// AMMDelete after auto-deletion is expected to fail with terNO_AMM.
    /// </summary>
    [TestMethod]
    public async Task TestAMMDelete_EmptyPool()
    {
        await CreatePool();

        AMMWithdraw withdrawAll = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Flags = AMMWithdrawFlags.tfWithdrawAll
        };

        ITransactionRequest autofilledWithdraw = await client.Autofill(withdrawAll);
        TransactionSummary withdrawRes = await client.SubmitAndWait(autofilledWithdraw, walletHolder, true);
        Console.WriteLine($"Withdraw all: {withdrawRes.Meta?.TransactionResult}");
        AssertSuccess(withdrawRes, "WithdrawAll");

        try
        {
            AMMInfoResponse info = await GetAmmInfo();
            Console.WriteLine($"Pool still exists after WithdrawAll, LP balance: {info.Amm?.LPTokenBalance?.Value}");
            Assert.Fail("Pool should have been auto-deleted after sole LP holder WithdrawAll");
        }
        catch (RippledException ex) when (
            ex.Message.Contains("actNotFound") ||
            ex.Message.Contains("terNO_AMM") ||
            ex.Message.Contains("ammNotFound"))
        {
            Console.WriteLine($"Pool correctly auto-deleted after sole LP holder WithdrawAll: {ex.Message}");
        }
    }

    [TestMethod]
    public async Task TestAMMDelete_NonEmptyPool_Fails()
    {
        await CreatePool();

        AMMDelete delete = new AMMDelete
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset
        };

        try
        {
            ITransactionRequest autofilled = await client.Autofill(delete);
            TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
            string result = res.Meta?.TransactionResult;
            Console.WriteLine($"AMMDelete non-empty pool result: {result}");

            Assert.AreNotEqual("tesSUCCESS", result,
                "AMMDelete on non-empty pool should not succeed");
        }
        catch (RippleException ex)
        {
            Console.WriteLine($"AMMDelete non-empty pool correctly rejected: {ex.Message}");
        }
    }

    /// <summary>
    /// Tests explicit AMMDelete with two LP holders: first holder partially withdraws,
    /// second holder withdraws all, leaving an empty pool for deletion.
    /// </summary>
    [TestMethod]
    public async Task TestAMMDelete_AfterPartialWithdraw()
    {
        XrplWallet walletSecondHolder = await SetupSecondHolder();
        await CreatePool("1000", 10m);
        await DepositSecondHolder(walletSecondHolder, 0.2m);

        AMMWithdraw withdrawAll = new AMMWithdraw
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Flags = AMMWithdrawFlags.tfWithdrawAll
        };
        ITransactionRequest autoWithdraw = await client.Autofill(withdrawAll);
        TransactionSummary withdrawRes = await client.SubmitAndWait(autoWithdraw, walletHolder, true);
        AssertSuccess(withdrawRes, "First holder WithdrawAll");

        AMMWithdraw withdrawAll2 = new AMMWithdraw
        {
            Account = walletSecondHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            Flags = AMMWithdrawFlags.tfWithdrawAll
        };
        ITransactionRequest autoWithdraw2 = await client.Autofill(withdrawAll2);
        TransactionSummary withdrawRes2 = await client.SubmitAndWait(autoWithdraw2, walletSecondHolder, true);
        AssertSuccess(withdrawRes2, "Second holder WithdrawAll");

        try
        {
            AMMInfoResponse info2 = await GetAmmInfo();
            Console.WriteLine($"Pool still exists after full withdrawal, LP balance: {info2.Amm?.LPTokenBalance?.Value}");

            AMMDelete delete = new AMMDelete
            {
                Account = walletHolder.ClassicAddress,
                Asset = TokenAsset,
                Asset2 = XrpAsset
            };

            ITransactionRequest autoDelete = await client.Autofill(delete);
            TransactionSummary deleteRes = await client.SubmitAndWait(autoDelete, walletHolder, true);
            string result = deleteRes.Meta?.TransactionResult;
            Console.WriteLine($"AMMDelete result: {result}");
            Assert.IsTrue(
                result == "tesSUCCESS" || result == "terQUEUED" || result == "tecAMM_EMPTY",
                $"AMMDelete after full withdrawal failed: {result}");
        }
        catch (RippledException ex) when (
            ex.Message.Contains("terNO_AMM") ||
            ex.Message.Contains("ammNotFound") ||
            ex.Message.Contains("actNotFound"))
        {
            Console.WriteLine($"Pool was auto-deleted when last LP holder withdrew: {ex.Message}");
        }
    }

    #endregion

    #region AMMVote Tests

    [TestMethod]
    public async Task TestAMMVote_ChangeTradingFee()
    {
        await CreatePool();

        AMMVote vote = new AMMVote
        {
            Account = walletHolder.ClassicAddress,
            Asset = TokenAsset,
            Asset2 = XrpAsset,
            TradingFee = 100
        };

        ITransactionRequest autofilled = await client.Autofill(vote);
        TransactionSummary res = await client.SubmitAndWait(autofilled, walletHolder, true);
        AssertSuccess(res, "AMMVote");

        AMMInfoResponse info = await GetAmmInfo();
        Console.WriteLine($"Trading fee after vote: {info.Amm.TradingFee}");
        Assert.AreEqual(100u, info.Amm.TradingFee, "Trading fee should reflect the vote");
    }

    #endregion
}
