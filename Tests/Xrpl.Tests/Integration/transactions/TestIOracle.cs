using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Xrpl.AddressCodec;
using Xrpl.BinaryCodec.Hashing;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// Integration tests for Oracle transactions (OracleSet and OracleDelete).
/// Tests the complete lifecycle of Oracle price feeds on testnet or standalone.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TestIOracle
{
    public TestContext TestContext { get; set; }
    public static IXrplClient client;

    static XrplWallet walletOracle = XrplWallet.FromNormalizedText("oracle test account");

    [ClassInitialize]
    public static async Task MyClassInitializeAsync(TestContext testContext)
    {
        client = new XrplClient("wss://s.altnet.rippletest.net:51233");
        client.connection.OnConnected += () =>
        {
            Console.WriteLine($"SetupIntegration CONNECTED");
            return Task.CompletedTask;
        };
        client.connection.OnDisconnect += (code, description) =>
        {
            Console.WriteLine($"SetupIntegration DISCONNECTED: {code}, description: {description}");
            return Task.CompletedTask;
        };
        client.connection.OnError += (error, errorMessage, message, data) =>
        {
            Console.WriteLine($"SetupIntegration ERROR: {message}");
            return Task.CompletedTask;
        };
        await client.Connect();

        await TryFillAccount(walletOracle);
    }

    [ClassCleanup]
    public static void AfterAllTests()
    {
        client.Dispose();
    }

    #region OracleSet Tests

    /// <summary>
    /// Tests creating a new Oracle with a single price data entry and verifies ledger state.
    /// </summary>
    [TestMethod]
    public async Task TestOracleSet_CreateOracle_SinglePriceData()
    {
        var oracleDocumentId = (uint)new Random().Next(1, 100000);
        var lastUpdateTime = await GetLedgerCloseTimeAsync();

        var oracleSet = new OracleSet
        {
            Account = walletOracle.ClassicAddress,
            OracleDocumentID = oracleDocumentId,
            LastUpdateTime = lastUpdateTime,
            Provider = "TestProvider",
            AssetClass = "currency",
            PriceDataSeries = new List<PriceDataWrapper>
            {
                new PriceDataWrapper
                {
                    PriceData = new PriceData
                    {
                        BaseAsset = "XRP",
                        QuoteAsset = "USD",
                        AssetPrice = 740,
                        Scale = 3
                    }
                }
            }
        };

        var autofilled = await client.Autofill(oracleSet);
        var res = await client.SubmitAndWait(autofilled, walletOracle, true);

        ValidateSuccessResult(res, "OracleSet create single price");

        var oracleExists = await VerifyOracleExists(walletOracle.ClassicAddress, oracleDocumentId);
        Assert.IsTrue(oracleExists, $"Oracle {oracleDocumentId} should exist in ledger after creation");

        Console.WriteLine($"Created and verified Oracle with DocumentID: {oracleDocumentId}");
    }

    /// <summary>
    /// Tests creating a new Oracle with multiple price data entries.
    /// </summary>
    [TestMethod]
    public async Task TestOracleSet_CreateOracle_MultiplePriceData()
    {
        var oracleDocumentId = (uint)new Random().Next(100001, 200000);
        var lastUpdateTime = await GetLedgerCloseTimeAsync();

        var oracleSet = new OracleSet
        {
            Account = walletOracle.ClassicAddress,
            OracleDocumentID = oracleDocumentId,
            LastUpdateTime = lastUpdateTime,
            Provider = "MultiPriceProvider",
            AssetClass = "currency",
            PriceDataSeries = new List<PriceDataWrapper>
            {
                new PriceDataWrapper
                {
                    PriceData = new PriceData
                    {
                        BaseAsset = "XRP",
                        QuoteAsset = "BTC",
                        AssetPrice = 35000,
                        Scale = 1
                    }
                },
                new PriceDataWrapper
                {
                    PriceData = new PriceData
                    {
                        BaseAsset = "XRP",
                        QuoteAsset = "ETH",
                        AssetPrice = 500,
                        //Scale = 0
                    }
                },
                new PriceDataWrapper
                {
                    PriceData = new PriceData
                    {
                        BaseAsset = "XRP",
                        QuoteAsset = "RLUSD",
                        AssetPrice = 2,
                        Scale = 20
                    }
                },
                new PriceDataWrapper
                {
                    PriceData = new PriceData
                    {
                        BaseAsset = "XRP",
                        QuoteAsset = "USD",
                        AssetPrice = 740,
                        Scale = 3
                    }
                },
                new PriceDataWrapper
                {
                    PriceData = new PriceData
                    {
                        BaseAsset = "XRP",
                        QuoteAsset = "TST",
                        AssetPrice = 740,
                        Scale = 3
                    }
                }
            }
        };

        var autofilled = await client.Autofill(oracleSet);
        var res = await client.SubmitAndWait(autofilled, walletOracle, true);

        ValidateSuccessResult(res, "OracleSet create multiple prices");

        var oracleExists = await VerifyOracleExists(walletOracle.ClassicAddress, oracleDocumentId);
        Assert.IsTrue(oracleExists, $"Oracle {oracleDocumentId} should exist in ledger");

        Console.WriteLine($"Created Oracle with {oracleSet.PriceDataSeries.Count} price entries, verified in ledger");
    }

    /// <summary>
    /// Tests updating an existing Oracle with new price data.
    /// </summary>
    [TestMethod]
    public async Task TestOracleSet_UpdateOracle()
    {
        var oracleDocumentId = (uint)new Random().Next(200001, 300000);
        var lastUpdateTime = await GetLedgerCloseTimeAsync();

        var createOracle = new OracleSet
        {
            Account = walletOracle.ClassicAddress,
            OracleDocumentID = oracleDocumentId,
            LastUpdateTime = lastUpdateTime,
            Provider = "UpdateTestProvider",
            AssetClass = "currency",
            PriceDataSeries = new List<PriceDataWrapper>
            {
                new PriceDataWrapper
                {
                    PriceData = new PriceData
                    {
                        BaseAsset = "XRP",
                        QuoteAsset = "USD",
                        AssetPrice = 500,
                        Scale = 3
                    }
                }
            }
        };

        var autofilled1 = await client.Autofill(createOracle);
        var res1 = await client.SubmitAndWait(autofilled1, walletOracle, true);
        ValidateSuccessResult(res1, "OracleSet create for update test");

        await Task.Delay(1000);

        var updateOracle = new OracleSet
        {
            Account = walletOracle.ClassicAddress,
            OracleDocumentID = oracleDocumentId,
            LastUpdateTime = await GetLedgerCloseTimeAsync(),
            PriceDataSeries = new List<PriceDataWrapper>
            {
                new PriceDataWrapper
                {
                    PriceData = new PriceData
                    {
                        BaseAsset = "XRP",
                        QuoteAsset = "USD",
                        AssetPrice = 750,
                        Scale = 3
                    }
                }
            }
        };

        var autofilled2 = await client.Autofill(updateOracle);
        var res2 = await client.SubmitAndWait(autofilled2, walletOracle, true);

        ValidateSuccessResult(res2, "OracleSet update");

        var oracleExists = await VerifyOracleExists(walletOracle.ClassicAddress, oracleDocumentId);
        Assert.IsTrue(oracleExists, $"Oracle {oracleDocumentId} should still exist after update");

        Console.WriteLine($"Updated Oracle {oracleDocumentId} with new price, verified in ledger");
    }

    #endregion

    #region OracleDelete Tests

    /// <summary>
    /// Tests creating and then deleting an Oracle, verifying ledger state at each step.
    /// </summary>
    [TestMethod]
    public async Task TestOracleDelete_DeleteExistingOracle()
    {
        var oracleDocumentId = (uint)new Random().Next(300001, 400000);
        var lastUpdateTime = await GetLedgerCloseTimeAsync();

        var createOracle = new OracleSet
        {
            Account = walletOracle.ClassicAddress,
            OracleDocumentID = oracleDocumentId,
            LastUpdateTime = lastUpdateTime,
            Provider = "DeleteTestProvider",
            AssetClass = "currency",
            PriceDataSeries = new List<PriceDataWrapper>
            {
                new PriceDataWrapper
                {
                    PriceData = new PriceData
                    {
                        BaseAsset = "XRP",
                        QuoteAsset = "EUR",
                        AssetPrice = 680,
                        Scale = 3
                    }
                }
            }
        };

        var autofilled1 = await client.Autofill(createOracle);
        var res1 = await client.SubmitAndWait(autofilled1, walletOracle, true);
        ValidateSuccessResult(res1, "OracleSet create for delete test");

        var existsAfterCreate = await VerifyOracleExists(walletOracle.ClassicAddress, oracleDocumentId);
        Assert.IsTrue(existsAfterCreate, $"Oracle {oracleDocumentId} should exist before deletion");

        Console.WriteLine($"Created Oracle {oracleDocumentId} for deletion test, verified in ledger");

        var deleteOracle = new OracleDelete
        {
            Account = walletOracle.ClassicAddress,
            OracleDocumentID = oracleDocumentId
        };

        var autofilled2 = await client.Autofill(deleteOracle);
        var res2 = await client.SubmitAndWait(autofilled2, walletOracle, true);

        ValidateSuccessResult(res2, "OracleDelete");

        var existsAfterDelete = await VerifyOracleExists(walletOracle.ClassicAddress, oracleDocumentId);
        Assert.IsFalse(existsAfterDelete, $"Oracle {oracleDocumentId} should NOT exist after deletion");

        Console.WriteLine($"Deleted Oracle {oracleDocumentId}, verified removal from ledger");
    }

    /// <summary>
    /// Tests that deleting a non-existent Oracle fails with tecNO_ENTRY.
    /// Uses Submit instead of SubmitAndWait to avoid exception on non-success result.
    /// </summary>
    [TestMethod]
    public async Task TestOracleDelete_NonExistentOracle()
    {
        var oracleDocumentId = (uint)new Random().Next(900000, 999999);

        var existsBefore = await VerifyOracleExists(walletOracle.ClassicAddress, oracleDocumentId);
        Assert.IsFalse(existsBefore, "Oracle should not exist before attempting delete");

        var deleteOracle = new OracleDelete
        {
            Account = walletOracle.ClassicAddress,
            OracleDocumentID = oracleDocumentId
        };

        var autofilled = await client.Autofill(deleteOracle);
        var signed = walletOracle.Sign(autofilled);
        var res = await client.SubmitRequest(signed.TxBlob, failHard: false);

        Assert.AreEqual("tecNO_ENTRY", res.EngineResult,
            $"Expected tecNO_ENTRY for non-existent oracle, got: {res.EngineResult}");

        Console.WriteLine($"Correctly failed to delete non-existent Oracle {oracleDocumentId} with tecNO_ENTRY");
    }

    #endregion

    #region Full Lifecycle Test

    /// <summary>
    /// Tests the complete Oracle lifecycle: create, verify, update, verify, delete, verify removal.
    /// </summary>
    [TestMethod]
    public async Task TestOracle_FullLifecycle()
    {
        var oracleDocumentId = (uint)new Random().Next(400001, 500000);

        var createOracle = new OracleSet
        {
            Account = walletOracle.ClassicAddress,
            OracleDocumentID = oracleDocumentId,
            LastUpdateTime = await GetLedgerCloseTimeAsync(),
            Provider = "LifecycleProvider",
            AssetClass = "currency",
            URI = "https://example.com/oracle",
            PriceDataSeries = new List<PriceDataWrapper>
            {
                new PriceDataWrapper
                {
                    PriceData = new PriceData
                    {
                        BaseAsset = "XRP",
                        QuoteAsset = "JPY",
                        AssetPrice = 110000,
                        Scale = 3
                    }
                }
            }
        };

        var autofilled1 = await client.Autofill(createOracle);
        var res1 = await client.SubmitAndWait(autofilled1, walletOracle, true);
        ValidateSuccessResult(res1, "Lifecycle: create");

        var existsStep1 = await VerifyOracleExists(walletOracle.ClassicAddress, oracleDocumentId);
        Assert.IsTrue(existsStep1, "Step 1: Oracle should exist after creation");
        Console.WriteLine($"Step 1: Created Oracle {oracleDocumentId}, verified in ledger");

        await Task.Delay(1000);

        var updateOracle = new OracleSet
        {
            Account = walletOracle.ClassicAddress,
            OracleDocumentID = oracleDocumentId,
            LastUpdateTime = await GetLedgerCloseTimeAsync(),
            PriceDataSeries = new List<PriceDataWrapper>
            {
                new PriceDataWrapper
                {
                    PriceData = new PriceData
                    {
                        BaseAsset = "XRP",
                        QuoteAsset = "JPY",
                        AssetPrice = 115000,
                        Scale = 3
                    }
                }
            }
        };

        var autofilled2 = await client.Autofill(updateOracle);
        var res2 = await client.SubmitAndWait(autofilled2, walletOracle, true);
        ValidateSuccessResult(res2, "Lifecycle: update");

        var existsStep2 = await VerifyOracleExists(walletOracle.ClassicAddress, oracleDocumentId);
        Assert.IsTrue(existsStep2, "Step 2: Oracle should still exist after update");
        Console.WriteLine($"Step 2: Updated Oracle {oracleDocumentId}, verified in ledger");

        var deleteOracle = new OracleDelete
        {
            Account = walletOracle.ClassicAddress,
            OracleDocumentID = oracleDocumentId
        };

        var autofilled3 = await client.Autofill(deleteOracle);
        var res3 = await client.SubmitAndWait(autofilled3, walletOracle, true);
        ValidateSuccessResult(res3, "Lifecycle: delete");

        var existsStep3 = await VerifyOracleExists(walletOracle.ClassicAddress, oracleDocumentId);
        Assert.IsFalse(existsStep3, "Step 3: Oracle should NOT exist after deletion");
        Console.WriteLine($"Step 3: Deleted Oracle {oracleDocumentId}, verified removal from ledger");

        Console.WriteLine("Full lifecycle test completed successfully!");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Gets the ledger close_time from the XRPL server (validated ledger) and converts to Unix epoch.
    /// XRPL close_time is in Ripple epoch (seconds since 2000-01-01).
    /// LastUpdateTime for Oracle must be in Unix epoch (seconds since 1970-01-01).
    /// LastUpdateTime must be within ±300 seconds of ledger close time.
    /// </summary>
    private static async Task<uint> GetLedgerCloseTimeAsync()
    {
        const uint RippleEpochOffset = 946684800; // Seconds between Unix epoch (1970) and Ripple epoch (2000)
        
        var ledgerRequest = new LedgerRequest { LedgerIndex = new LedgerIndex(LedgerIndexType.Validated) };
        var ledgerResponse = await client.Ledger(ledgerRequest);
        
        // close_time in response is Ripple epoch - convert to Unix epoch for LastUpdateTime
        var ledgerEntity = ledgerResponse.LedgerEntity as LedgerEntity;
        var rippleCloseTime = ledgerEntity?.CloseTime ?? 0;
        var unixCloseTime = rippleCloseTime + RippleEpochOffset;
        
        Console.WriteLine($"Ledger close_time (Ripple): {rippleCloseTime}, Unix: {unixCloseTime}");
        return unixCloseTime;
    }

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

    /// <summary>
    /// Queries the ledger to verify if an Oracle with the specified DocumentID exists.
    /// Throws on ledger query failure to prevent false positives.
    /// The Oracle ID is computed as SHA-512Half of (0x52 + Owner AccountID + OracleDocumentID).
    /// </summary>
    private static async Task<bool> VerifyOracleExists(string account, uint oracleDocumentId)
    {
        var expectedOracleId = ComputeOracleId(account, oracleDocumentId);
        
        var request = new AccountObjectsRequest(account)
        {
            Type = LedgerEntryType.Oracle,
            Limit = 200
        };

        AccountObjects response;
        try
        {
            response = await client.AccountObjects(request);
        }
        catch (Exception ex)
        {
            Assert.Fail($"Failed to query ledger for Oracle verification: {ex.Message}");
            return false;
        }
        
        if (response?.AccountObjectList == null || response.AccountObjectList.Count == 0)
            return false;

        foreach (var obj in response.AccountObjectList)
        {
            if (obj.LedgerEntryType == LedgerEntryType.Oracle)
            {
                if (string.Equals(obj.Index, expectedOracleId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Computes the Oracle ledger entry ID from Owner + OracleDocumentID.
    /// Format: SHA-512Half(SpaceKey (2 bytes) + Owner AccountID (20 bytes) + OracleDocumentID (4 bytes big-endian))
    /// SpaceKey for Oracle is 0x0052 ('R').
    /// </summary>
    private static string ComputeOracleId(string ownerAddress, uint oracleDocumentId)
    {
        byte[] accountId = XrplCodec.DecodeAccountID(ownerAddress);
        
        byte[] oracleDocBytes = new byte[4];
        oracleDocBytes[0] = (byte)(oracleDocumentId >> 24);
        oracleDocBytes[1] = (byte)(oracleDocumentId >> 16);
        oracleDocBytes[2] = (byte)(oracleDocumentId >> 8);
        oracleDocBytes[3] = (byte)(oracleDocumentId);
        
        // Build input: 2-byte space key + 20-byte AccountID + 4-byte OracleDocumentID
        // Oracle space key is 0x0052 (uint16 big-endian)
        byte[] input = new byte[2 + accountId.Length + oracleDocBytes.Length];
        input[0] = 0x00;
        input[1] = 0x52;
        Array.Copy(accountId, 0, input, 2, accountId.Length);
        Array.Copy(oracleDocBytes, 0, input, 2 + accountId.Length, oracleDocBytes.Length);
        
        byte[] hash = Sha512.Half(input);
        
        return BitConverter.ToString(hash).Replace("-", "").ToUpperInvariant();
    }

    /// <summary>
    /// Attempts to fund the account if balance is low.
    /// </summary>
    private static async Task TryFillAccount(XrplWallet wallet)
    {
        try
        {
            var balance = await client.GetXrpFreeBalance(wallet.ClassicAddress);
            Console.WriteLine($"Balance {wallet.ClassicAddress} - {balance} XRP");

            if (balance <= 50)
            {
                await client.FundWallet(wallet);
                Console.WriteLine($"Funded {wallet.ClassicAddress}");
            }
        }
        catch (Exception)
        {
            await client.FundWallet(wallet);
            Console.WriteLine($"Funded new account {wallet.ClassicAddress}");
        }
    }

    #endregion
}
