using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// Integration tests for AMMClawback transactions.
/// Tests clawback of tokens from AMM pools where holders have deposited liquidity.
/// Requires the issuer to have asfAllowTrustLineClawback flag set before any tokens are issued.
/// Each test creates its own fresh wallets and AMM pool to ensure independence.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TestIAMMClawback
{
    public TestContext TestContext { get; set; }
    private static IXrplClient client;

    private XrplWallet walletIssuer;
    private XrplWallet walletHolder;

    const string CurrencyCode = "AMC";
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
        await CreateAMMPool();
    }

    #region Setup Methods

    private async Task SetupIssuerFlags()
    {
        var clawbackSet = new AccountSet
        {
            Account = walletIssuer.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfAllowTrustLineClawback
        };

        var autofilled1 = await client.Autofill(clawbackSet);
        var res1 = await client.SubmitAndWait(autofilled1, walletIssuer, true);
        Console.WriteLine($"Clawback flag: {res1.Meta?.TransactionResult}");

        var rippleSet = new AccountSet
        {
            Account = walletIssuer.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfDefaultRipple
        };

        var autofilled2 = await client.Autofill(rippleSet);
        var res2 = await client.SubmitAndWait(autofilled2, walletIssuer, true);
        Console.WriteLine($"Default ripple flag: {res2.Meta?.TransactionResult}");
    }

    private async Task SetupHolderTrustLine()
    {
        var trustSet = new TrustSet
        {
            Account = walletHolder.ClassicAddress,
            LimitAmount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = "1000000000"
            }
        };

        var autofilled = await client.Autofill(trustSet);
        var res = await client.SubmitAndWait(autofilled, walletHolder, true);
        Console.WriteLine($"Trust line: {res.Meta?.TransactionResult}");
    }

    private async Task IssueTokensToHolder(string amount)
    {
        var payment = new Payment
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

        var autofilled = await client.Autofill(payment);
        var res = await client.SubmitAndWait(autofilled, walletIssuer, true);
        Console.WriteLine($"Issue tokens: {res.Meta?.TransactionResult}");
    }

    private async Task CreateAMMPool()
    {
        var ammCreate = new AMMCreate
        {
            Account = walletHolder.ClassicAddress,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = "1000"
            },
            Amount2 = new Currency { ValueAsXrp = 1 },
            TradingFee = 500
        };

        var autofilled = await client.Autofill(ammCreate);
        var res = await client.SubmitAndWait(autofilled, walletHolder, true);
        Console.WriteLine($"AMM create: {res.Meta?.TransactionResult}");
    }

    private async Task DepositToAMM(string amount)
    {
        var ammDeposit = new AMMDeposit
        {
            Account = walletHolder.ClassicAddress,
            Asset = new IssuedCurrency
            {
                Currency = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress
            },
            Asset2 = new IssuedCurrency
            {
                Currency = "XRP"
            },
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = amount
            },
            Flags = AMMDepositFlags.tfSingleAsset
        };

        var autofilled = await client.Autofill(ammDeposit);
        var res = await client.SubmitAndWait(autofilled, walletHolder, true);
        Console.WriteLine($"AMM deposit: {res.Meta?.TransactionResult}");
    }

    #endregion

    #region Test Methods

    /// <summary>
    /// Tests basic AMMClawback without specifying amount (full clawback).
    /// </summary>
    [TestMethod]
    public async Task TestAMMClawbackBasic()
    {
        var ammClawback = new AMMClawBack
        {
            Account = walletIssuer.ClassicAddress,
            Holder = walletHolder.ClassicAddress,
            Asset = new IssuedCurrency
            {
                Currency = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress
            },
            Asset2 = new IssuedCurrency
            {
                Currency = "XRP"
            }
        };

        var autofilled = await client.Autofill(ammClawback);
        Console.WriteLine($"AMMClawback tx: {JsonSerializer.Serialize(autofilled, new JsonSerializerOptions { WriteIndented = true })}");

        var res = await client.SubmitAndWait(autofilled, walletIssuer, true);
        var result = res.Meta?.TransactionResult;
        Console.WriteLine($"AMMClawback result: {result}");

        Assert.IsTrue(
            result == "tesSUCCESS" || result == "terQUEUED" || result == "tecAMM_EMPTY",
            $"AMMClawback failed: {result}");
    }

    /// <summary>
    /// Tests AMMClawback with specified amount (partial clawback).
    /// </summary>
    [TestMethod]
    public async Task TestAMMClawbackWithAmount()
    {
        var ammClawback = new AMMClawBack
        {
            Account = walletIssuer.ClassicAddress,
            Holder = walletHolder.ClassicAddress,
            Asset = new IssuedCurrency
            {
                Currency = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress
            },
            Asset2 = new IssuedCurrency
            {
                Currency = "XRP"
            },
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = "100"
            }
        };

        var autofilled = await client.Autofill(ammClawback);
        Console.WriteLine($"AMMClawback with amount tx: {JsonSerializer.Serialize(autofilled, new JsonSerializerOptions { WriteIndented = true })}");

        var res = await client.SubmitAndWait(autofilled, walletIssuer, true);
        var result = res.Meta?.TransactionResult;
        Console.WriteLine($"AMMClawback with amount result: {result}");

        Assert.IsTrue(
            result == "tesSUCCESS" || result == "terQUEUED" || result == "tecAMM_EMPTY",
            $"AMMClawback with amount failed: {result}");
    }

    /// <summary>
    /// Tests that non-issuer cannot clawback from AMM.
    /// Expected to fail with temMALFORMED or similar error.
    /// </summary>
    [TestMethod]
    public async Task TestAMMClawbackNonIssuerFails()
    {
        var ammClawback = new AMMClawBack
        {
            Account = walletHolder.ClassicAddress,
            Holder = walletIssuer.ClassicAddress,
            Asset = new IssuedCurrency
            {
                Currency = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress
            },
            Asset2 = new IssuedCurrency
            {
                Currency = "XRP"
            }
        };

        try
        {
            var autofilled = await client.Autofill(ammClawback);
            var res = await client.SubmitAndWait(autofilled, walletHolder, true);
            var result = res.Meta?.TransactionResult;
            Console.WriteLine($"Non-issuer AMMClawback result: {result}");

            Assert.AreNotEqual("tesSUCCESS", result, "Non-issuer should not be able to clawback");
        }
        catch (RippleException ex)
        {
            Console.WriteLine($"Non-issuer AMMClawback correctly rejected: {ex.Message}");
            Assert.IsTrue(
                ex.Message.Contains("temMALFORMED") || 
                ex.Message.Contains("tecNO_PERMISSION") ||
                ex.Message.Contains("terNO_AMM") ||
                ex.Message.Contains("tecNO_AUTH"),
                $"Expected rejection error, got: {ex.Message}");
        }
    }

    #endregion
}
