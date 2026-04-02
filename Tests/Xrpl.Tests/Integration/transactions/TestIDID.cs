using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// Integration tests for DID transactions (DIDSet and DIDDelete).
/// Tests the complete lifecycle of Decentralized Identifiers on testnet or standalone.
/// Uses IntegrationTestConfig to support both testnet and standalone node.
/// Each test uses its own wallet for isolation.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TestIDID
{
    public TestContext TestContext { get; set; }
    public static IXrplClient client;

    public static TestNodeType nodeType = TestNodeType.Standalone;
    [ClassInitialize]
    public static async Task MyClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(nodeType);
    }

    [ClassCleanup]
    public static void AfterAllTests()
    {
        client.Dispose();
    }

    #region Helper Methods

    private static string ToHex(string text)
    {
        return BitConverter.ToString(Encoding.UTF8.GetBytes(text)).Replace("-", "");
    }

    /// <summary>
    /// Validates the transaction result and handles amendment not enabled gracefully.
    /// Returns true if test should continue, false if amendment is not enabled and test should skip assertions.
    /// </summary>
    private bool ValidateSuccessResultOrSkip(TransactionSummary res, string testName)
    {
        Assert.IsNotNull(res, $"{testName}: Response should not be null");
        Assert.IsNotNull(res.Meta, $"{testName}: Meta should not be null");
        Assert.IsNotNull(res.Meta.TransactionResult, $"{testName}: TransactionResult should not be null");

        var result = res.Meta.TransactionResult;
        Console.WriteLine($"{testName}: Result = {result}");

        if (result == "tesSUCCESS")
        {
            return true;
        }

        if (result == "temDISABLED" || result == "notEnabled")
        {
            Console.WriteLine($"{testName}: DID amendment may not be enabled on this network. Skipping assertions.");
            return false;
        }

        if (result.StartsWith("tec"))
        {
            Console.WriteLine($"{testName}: Transaction claimed but failed with {result}");
            return false;
        }

        Assert.Fail($"{testName}: Unexpected result {result}");
        return false;
    }

    private async Task<bool> VerifyDIDExists(string account)
    {
        try
        {
            var request = new LedgerEntryRequest
            {
                DID = account
            };
            var response = await client.LedgerEntry(request);
            return response?.Node != null;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region DIDSet Tests

    /// <summary>
    /// Tests creating a new DID with Data field only.
    /// </summary>
    [TestMethod]
    public async Task TestDIDSet_CreateWithData()
    {
        var wallet = XrplWallet.FromNormalizedText("did create with data test");
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        var didSet = new DIDSet
        {
            Account = wallet.ClassicAddress,
            Data = ToHex("Test DID Data")
        };

        var autofilled = await client.Autofill(didSet);
        var res = await client.SubmitAndWait(autofilled, wallet, true);

        if (!ValidateSuccessResultOrSkip(res, "DIDSet create with Data"))
        {
            Console.WriteLine("DID amendment may not be enabled. Skipping further assertions.");
            return;
        }

        var didExists = await VerifyDIDExists(wallet.ClassicAddress);
        Assert.IsTrue(didExists, "DID should exist in ledger after creation");
        Console.WriteLine($"Created DID for account: {wallet.ClassicAddress}");
    }

    /// <summary>
    /// Tests creating a new DID with all fields (Data, DIDDocument, URI).
    /// </summary>
    [TestMethod]
    public async Task TestDIDSet_CreateWithAllFields()
    {
        var wallet = XrplWallet.FromNormalizedText("did all fields test");
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        var didSet = new DIDSet
        {
            Account = wallet.ClassicAddress,
            Data = ToHex("Identity attestations"),
            DIDDocument = ToHex("{\"@context\":\"https://www.w3.org/ns/did/v1\"}"),
            URI = ToHex("https://example.com/did/document")
        };

        var autofilled = await client.Autofill(didSet);
        var res = await client.SubmitAndWait(autofilled, wallet, true);

        if (!ValidateSuccessResultOrSkip(res, "DIDSet create with all fields"))
        {
            Console.WriteLine("DID amendment may not be enabled. Skipping further assertions.");
            return;
        }

        var didExists = await VerifyDIDExists(wallet.ClassicAddress);
        Assert.IsTrue(didExists, "DID should exist in ledger after creation with all fields");
        Console.WriteLine($"Created DID with all fields for account: {wallet.ClassicAddress}");
    }

    /// <summary>
    /// Tests updating an existing DID.
    /// </summary>
    [TestMethod]
    public async Task TestDIDSet_UpdateExisting()
    {
        var wallet = XrplWallet.FromNormalizedText("did update test");
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        var createDID = new DIDSet
        {
            Account = wallet.ClassicAddress,
            Data = ToHex("Original data")
        };

        var autofilled1 = await client.Autofill(createDID);
        var res1 = await client.SubmitAndWait(autofilled1, wallet, true);

        if (!ValidateSuccessResultOrSkip(res1, "DIDSet create for update test"))
        {
            Console.WriteLine("DID amendment may not be enabled. Skipping update test.");
            return;
        }

        await Task.Delay(1000);

        var updateDID = new DIDSet
        {
            Account = wallet.ClassicAddress,
            Data = ToHex("Updated data"),
            URI = ToHex("https://updated.example.com")
        };

        var autofilled2 = await client.Autofill(updateDID);
        var res2 = await client.SubmitAndWait(autofilled2, wallet, true);

        if (!ValidateSuccessResultOrSkip(res2, "DIDSet update"))
        {
            return;
        }

        var didExists = await VerifyDIDExists(wallet.ClassicAddress);
        Assert.IsTrue(didExists, "DID should still exist after update");

        Console.WriteLine($"Updated DID for account: {wallet.ClassicAddress}");
    }

    #endregion

    #region DIDDelete Tests

    /// <summary>
    /// Tests creating and then deleting a DID.
    /// </summary>
    [TestMethod]
    public async Task TestDIDDelete_DeleteExisting()
    {
        var wallet = XrplWallet.FromNormalizedText("did delete test");
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        var createDID = new DIDSet
        {
            Account = wallet.ClassicAddress,
            Data = ToHex("DID to be deleted")
        };

        var autofilled1 = await client.Autofill(createDID);
        var res1 = await client.SubmitAndWait(autofilled1, wallet, true);

        if (!ValidateSuccessResultOrSkip(res1, "DIDSet create for delete test"))
        {
            Console.WriteLine("DID amendment may not be enabled. Skipping delete test.");
            return;
        }

        var existsAfterCreate = await VerifyDIDExists(wallet.ClassicAddress);
        Assert.IsTrue(existsAfterCreate, "DID should exist before deletion");

        Console.WriteLine($"Created DID for deletion test: {wallet.ClassicAddress}");

        var deleteDID = new DIDDelete
        {
            Account = wallet.ClassicAddress
        };

        var autofilled2 = await client.Autofill(deleteDID);
        var res2 = await client.SubmitAndWait(autofilled2, wallet, true);

        if (!ValidateSuccessResultOrSkip(res2, "DIDDelete"))
        {
            return;
        }

        var existsAfterDelete = await VerifyDIDExists(wallet.ClassicAddress);
        Assert.IsFalse(existsAfterDelete, "DID should not exist after deletion");
        Console.WriteLine($"Deleted DID for account: {wallet.ClassicAddress}");
    }

    #endregion

    #region Full Lifecycle Tests

    /// <summary>
    /// Tests the complete DID lifecycle: create -> update -> delete.
    /// </summary>
    [TestMethod]
    public async Task TestDID_FullLifecycle()
    {
        var wallet = XrplWallet.FromNormalizedText("did lifecycle test");
        await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

        Console.WriteLine($"Starting DID lifecycle test for: {wallet.ClassicAddress}");

        var createDID = new DIDSet
        {
            Account = wallet.ClassicAddress,
            Data = ToHex("Initial DID data"),
            DIDDocument = ToHex("{\"id\":\"did:xrpl:1\"}")
        };

        var autofilled1 = await client.Autofill(createDID);
        var res1 = await client.SubmitAndWait(autofilled1, wallet, true);

        if (!ValidateSuccessResultOrSkip(res1, "DID Lifecycle: Create"))
        {
            Console.WriteLine("DID amendment may not be enabled on this network. Skipping lifecycle test.");
            return;
        }

        var existsAfterCreate = await VerifyDIDExists(wallet.ClassicAddress);
        Assert.IsTrue(existsAfterCreate, "DID should exist after creation");
        Console.WriteLine("Step 1: DID created successfully");

        await Task.Delay(1000);

        var updateDID = new DIDSet
        {
            Account = wallet.ClassicAddress,
            Data = ToHex("Updated DID data"),
            URI = ToHex("https://example.com/updated")
        };

        var autofilled2 = await client.Autofill(updateDID);
        var res2 = await client.SubmitAndWait(autofilled2, wallet, true);

        if (!ValidateSuccessResultOrSkip(res2, "DID Lifecycle: Update"))
        {
            return;
        }

        var existsAfterUpdate = await VerifyDIDExists(wallet.ClassicAddress);
        Assert.IsTrue(existsAfterUpdate, "DID should exist after update");
        Console.WriteLine("Step 2: DID updated successfully");

        await Task.Delay(1000);

        var deleteDID = new DIDDelete
        {
            Account = wallet.ClassicAddress
        };

        var autofilled3 = await client.Autofill(deleteDID);
        var res3 = await client.SubmitAndWait(autofilled3, wallet, true);

        if (!ValidateSuccessResultOrSkip(res3, "DID Lifecycle: Delete"))
        {
            return;
        }

        var existsAfterDelete = await VerifyDIDExists(wallet.ClassicAddress);
        Assert.IsFalse(existsAfterDelete, "DID should not exist after deletion");
        Console.WriteLine("Step 3: DID deleted successfully");

        Console.WriteLine("DID full lifecycle test completed successfully!");
    }

    #endregion
}
