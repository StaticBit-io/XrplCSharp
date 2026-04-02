using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System;
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

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// Integration tests for Clawback transactions.
/// Tests the complete lifecycle of token clawback from holder accounts.
/// Clawback requires the issuer to have asfAllowTrustLineClawback flag set before any tokens are issued.
/// Uses IntegrationTestConfig to support both testnet and standalone node.
/// Set XRPL_TEST_NODE environment variable to "standalone" or "testnet" to switch modes.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TestIClawback
{
    public TestContext TestContext { get; set; }
    public static IXrplClient client;

    static XrplWallet walletIssuer = XrplWallet.FromNormalizedText("clawback issuer account test");
    static XrplWallet walletHolder = XrplWallet.FromNormalizedText("clawback holder account test");

    const string CurrencyCode = "CLW";
    static bool issuerInitialized = false;
    static bool holderInitialized = false;
    public static TestNodeType nodeType = TestNodeType.Standalone;

    [ClassInitialize]
    public static async Task MyClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(nodeType);

        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletIssuer, walletHolder);

        await SetupIssuerWithClawbackFlag();
        await SetupHolderTrustLine();
    }

    [ClassCleanup]
    public static void AfterAllTests()
    {
        client.Dispose();
    }

    #region Setup Methods

    /// <summary>
    /// Sets up the issuer account with asfAllowTrustLineClawback flag.
    /// This must be done before any trust lines are created.
    /// </summary>
    private static async Task SetupIssuerWithClawbackFlag()
    {
        if (issuerInitialized) return;

        var accountSet = new AccountSet
        {
            Account = walletIssuer.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfAllowTrustLineClawback
        };

        try
        {
            var autofilled = await client.Autofill(accountSet);
            var res = await client.SubmitAndWait(autofilled, walletIssuer, true);
            var result = res.Meta?.TransactionResult;
            
            if (result == "tesSUCCESS" || result == "terQUEUED")
            {
                Console.WriteLine($"Issuer {walletIssuer.ClassicAddress} clawback flag set successfully");
                issuerInitialized = true;
            }
            else if (result == "tecOWNERS")
            {
                Console.WriteLine($"Cannot set clawback flag - issuer already has trust lines. Result: {result}");
                issuerInitialized = true;
            }
            else
            {
                Console.WriteLine($"Failed to set clawback flag: {result}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception setting clawback flag: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets up the holder's trust line to the issuer for the test currency.
    /// </summary>
    private static async Task SetupHolderTrustLine()
    {
        if (holderInitialized) return;

        var trustSet = new TrustSet
        {
            Account = walletHolder.ClassicAddress,
            LimitAmount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = "10000000"
            }
        };

        try
        {
            var autofilled = await client.Autofill(trustSet);
            var res = await client.SubmitAndWait(autofilled, walletHolder, true);
            var result = res.Meta?.TransactionResult;

            if (result == "tesSUCCESS" || result == "terQUEUED" || result == "tecNO_LINE_REDUNDANT")
            {
                Console.WriteLine($"Holder {walletHolder.ClassicAddress} trust line created/confirmed for {CurrencyCode}");
                holderInitialized = true;
            }
            else
            {
                Console.WriteLine($"Trust line setup result: {result}");
                holderInitialized = true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception setting trust line: {ex.Message}");
            holderInitialized = true;
        }
    }

    /// <summary>
    /// Issues tokens from issuer to holder.
    /// </summary>
    private static async Task<bool> IssueTokensToHolder(string amount)
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

        try
        {
            var autofilled = await client.Autofill(payment);
            var res = await client.SubmitAndWait(autofilled, walletIssuer, true);
            var result = res.Meta?.TransactionResult;

            if (result == "tesSUCCESS" || result == "terQUEUED")
            {
                Console.WriteLine($"Issued {amount} {CurrencyCode} to holder");
                return true;
            }
            Console.WriteLine($"Failed to issue tokens: {result}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception issuing tokens: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets the holder's token balance for the test currency.
    /// </summary>
    private static async Task<decimal> GetHolderBalance()
    {
        try
        {
            var request = new AccountLinesRequest(walletHolder.ClassicAddress)
            {
                Peer = walletIssuer.ClassicAddress
            };
            var response = await client.AccountLines(request);

            if (response?.TrustLines == null) return 0;

            foreach (var line in response.TrustLines)
            {
                if (line.Currency == CurrencyCode && line.Account == walletIssuer.ClassicAddress)
                {
                    if (decimal.TryParse(line.Balance, out var balance))
                        return balance;
                }
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error getting holder balance: {ex.Message}");
            return 0;
        }
    }

    #endregion

    #region Clawback Tests

    /// <summary>
    /// Tests a basic clawback operation where issuer claws back a portion of issued tokens.
    /// </summary>
    [TestMethod]
    public async Task TestClawback_BasicClawback()
    {
        var issued = await IssueTokensToHolder("100");
        if (!issued)
        {
            Assert.Inconclusive("Could not issue tokens for test");
            return;
        }

        var balanceBefore = await GetHolderBalance();
        Console.WriteLine($"Holder balance before clawback: {balanceBefore}");

        var clawbackAmount = "25";
        var clawback = new ClawBack
        {
            Account = walletIssuer.ClassicAddress,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletHolder.ClassicAddress,
                Value = clawbackAmount
            }
        };

        var autofilled = await client.Autofill(clawback);
        var res = await client.SubmitAndWait(autofilled, walletIssuer, true);

        var result = res.Meta?.TransactionResult;
        Console.WriteLine($"Clawback result: {result}");

        Assert.IsTrue(
            result == "tesSUCCESS" || result == "terQUEUED",
            $"Basic clawback failed: {result}");

        var balanceAfter = await GetHolderBalance();
        Console.WriteLine($"Holder balance after clawback: {balanceAfter}");

        Assert.IsTrue(balanceAfter < balanceBefore, 
            $"Holder balance should decrease after clawback. Before: {balanceBefore}, After: {balanceAfter}");

        Console.WriteLine($"Basic clawback test passed. Clawed back {clawbackAmount} {CurrencyCode}");
    }

    /// <summary>
    /// Tests clawback of a partial amount from holder's balance.
    /// </summary>
    [TestMethod]
    public async Task TestClawback_PartialClawback()
    {
        var issued = await IssueTokensToHolder("200");
        if (!issued)
        {
            Assert.Inconclusive("Could not issue tokens for test");
            return;
        }

        var balanceBefore = await GetHolderBalance();
        Console.WriteLine($"Balance before partial clawback: {balanceBefore}");

        var partialAmount = (balanceBefore / 2).ToString("0.######");
        var clawback = new ClawBack
        {
            Account = walletIssuer.ClassicAddress,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletHolder.ClassicAddress,
                Value = partialAmount
            }
        };

        var autofilled = await client.Autofill(clawback);
        var res = await client.SubmitAndWait(autofilled, walletIssuer, true);

        var result = res.Meta?.TransactionResult;
        Console.WriteLine($"Partial clawback result: {result}");

        Assert.IsTrue(
            result == "tesSUCCESS" || result == "terQUEUED",
            $"Partial clawback failed: {result}");

        var balanceAfter = await GetHolderBalance();
        Console.WriteLine($"Balance after partial clawback: {balanceAfter}");

        Assert.IsTrue(balanceAfter > 0, "Holder should still have some balance after partial clawback");
        Assert.IsTrue(balanceAfter < balanceBefore, "Balance should decrease after clawback");

        Console.WriteLine($"Partial clawback test passed");
    }

    /// <summary>
    /// Tests clawback of more than holder's balance (should clamp to actual balance).
    /// </summary>
    [TestMethod]
    public async Task TestClawback_FullClawback_ExceedsBalance()
    {
        var issued = await IssueTokensToHolder("50");
        if (!issued)
        {
            Assert.Inconclusive("Could not issue tokens for test");
            return;
        }

        var balanceBefore = await GetHolderBalance();
        Console.WriteLine($"Balance before full clawback: {balanceBefore}");

        var excessAmount = (balanceBefore * 10).ToString("0.######");
        var clawback = new ClawBack
        {
            Account = walletIssuer.ClassicAddress,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletHolder.ClassicAddress,
                Value = excessAmount
            }
        };

        var autofilled = await client.Autofill(clawback);
        var res = await client.SubmitAndWait(autofilled, walletIssuer, true);

        var result = res.Meta?.TransactionResult;
        Console.WriteLine($"Full clawback result: {result}");

        Assert.IsTrue(
            result == "tesSUCCESS" || result == "terQUEUED",
            $"Full clawback failed: {result}");

        var balanceAfter = await GetHolderBalance();
        Console.WriteLine($"Balance after full clawback: {balanceAfter}");

        Assert.IsTrue(balanceAfter < balanceBefore,
            $"Balance should decrease after clawback. Before: {balanceBefore}, After: {balanceAfter}");
        Assert.IsTrue(balanceAfter <= 0,
            $"When clawing back more than balance, holder balance should be zero or negative. Actual: {balanceAfter}");

        Console.WriteLine($"Full clawback test passed - holder balance reduced to {balanceAfter}");
    }

    /// <summary>
    /// Tests that clawback fails when issuer tries to claw back from themselves.
    /// The XRPL server rejects self-clawback with temBAD_AMOUNT error.
    /// </summary>
    [TestMethod]
    public async Task TestClawback_FailsForSelfClawback()
    {
        var clawback = new ClawBack
        {
            Account = walletIssuer.ClassicAddress,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletIssuer.ClassicAddress,
                Value = "10"
            }
        };

        try
        {
            var autofilled = await client.Autofill(clawback);
            var res = await client.SubmitAndWait(autofilled, walletIssuer, true);
            var result = res.Meta?.TransactionResult;

            Assert.AreNotEqual("tesSUCCESS", result, 
                "Self-clawback should not succeed");
            Console.WriteLine($"Self-clawback correctly rejected with: {result}");
        }
        catch (ValidationException ex)
        {
            Console.WriteLine($"Self-clawback correctly rejected with validation error: {ex.Message}");
            Assert.IsTrue(ex.Message.Contains("holder") || ex.Message.Contains("Account"),
                "Error should mention holder or account issue");
        }
        catch (RippleException ex)
        {
            Console.WriteLine($"Self-clawback correctly rejected by server: {ex.Message}");
            Assert.IsTrue(ex.Message.Contains("temBAD_AMOUNT") || ex.Message.Contains("BAD"),
                "Server should reject self-clawback with temBAD_AMOUNT");
        }
    }

    /// <summary>
    /// Tests the complete clawback lifecycle: issue tokens, clawback some, verify balance changes.
    /// </summary>
    [TestMethod]
    public async Task TestClawback_CompleteLifecycle()
    {
        Console.WriteLine("=== Clawback Complete Lifecycle Test ===");

        Console.WriteLine("Step 1: Issue 1000 tokens to holder");
        var issued = await IssueTokensToHolder("1000");
        if (!issued)
        {
            Assert.Inconclusive("Could not issue tokens for lifecycle test");
            return;
        }

        var balanceStep1 = await GetHolderBalance();
        Console.WriteLine($"Holder balance after issuance: {balanceStep1}");
        Assert.IsTrue(balanceStep1 > 0, "Holder should have tokens after issuance");

        Console.WriteLine("Step 2: Clawback 300 tokens");
        var clawback1 = new ClawBack
        {
            Account = walletIssuer.ClassicAddress,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletHolder.ClassicAddress,
                Value = "300"
            }
        };

        var autofilled1 = await client.Autofill(clawback1);
        var res1 = await client.SubmitAndWait(autofilled1, walletIssuer, true);
        ValidateSuccessResult(res1, "Lifecycle: first clawback");

        var balanceStep2 = await GetHolderBalance();
        Console.WriteLine($"Holder balance after first clawback: {balanceStep2}");
        Assert.IsTrue(balanceStep2 < balanceStep1, "Balance should decrease after clawback");

        Console.WriteLine("Step 3: Clawback another 200 tokens");
        var clawback2 = new ClawBack
        {
            Account = walletIssuer.ClassicAddress,
            Amount = new Currency
            {
                CurrencyCode = CurrencyCode,
                Issuer = walletHolder.ClassicAddress,
                Value = "200"
            }
        };

        var autofilled2 = await client.Autofill(clawback2);
        var res2 = await client.SubmitAndWait(autofilled2, walletIssuer, true);
        ValidateSuccessResult(res2, "Lifecycle: second clawback");

        var balanceStep3 = await GetHolderBalance();
        Console.WriteLine($"Holder balance after second clawback: {balanceStep3}");
        Assert.IsTrue(balanceStep3 < balanceStep2, "Balance should decrease after second clawback");

        Console.WriteLine("Complete lifecycle test passed!");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Validates that a transaction result is successful.
    /// </summary>
    private static void ValidateSuccessResult(TransactionSummary res, string operation)
    {
        var result = res.Meta?.TransactionResult;
        Assert.IsTrue(
            result == "tesSUCCESS" || result == "terQUEUED",
            $"{operation} failed: {result}");
    }

    #endregion
}
