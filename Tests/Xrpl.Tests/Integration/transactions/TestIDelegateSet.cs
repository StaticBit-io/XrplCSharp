using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

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

[TestClass]
[TestCategory("Delegate")]
public class TestIDelegateSet
{
    private static bool permissionDelegationActive;

    public TestContext TestContext { get; set; }
    private static IXrplClient client;
    private static TestNodeType nodeType = TestNodeType.Standalone;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(TestNodeType.Standalone);
        permissionDelegationActive = await AmendmentGuard.IsEnabledAsync(client, AmendmentGuard.PermissionDelegationV11);
    }

    [TestInitialize]
    public void CheckPermissionDelegationAmendment()
    {
        if (!permissionDelegationActive)
        {
            Assert.Inconclusive("PermissionDelegationV1_1 amendment is not enabled on the test node; start .ci-config/docker-compose.batchv11.yml to run these tests.");
        }
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    private static void ValidateResult(TransactionSummary res)
    {
        if (res is not { Meta: { TransactionResult: "tesSUCCESS" or "terQUEUED" } })
            throw new RippleException($"Transaction failed: {res.Meta?.TransactionResult}");
    }

    /// <summary>
    /// Retrieves the LODelegate ledger object for the given owner, filtering by Delegate type.
    /// Returns null if no Delegate object is found.
    /// </summary>
    private static async Task<LODelegate> GetDelegateObject(string ownerAddress)
    {
        AccountObjectsRequest request = new AccountObjectsRequest(ownerAddress)
        {
            Type = LedgerEntryType.Delegate,
        };
        AccountObjects response = (await client.AccountObjects(request)).Result;

        return response?.AccountObjectList?
            .OfType<LODelegate>()
            .FirstOrDefault();
    }

    [TestMethod]
    public async Task TestDelegateSet_Basic()
    {
        XrplWallet walletOwner = XrplWallet.Generate();
        XrplWallet walletDelegate = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletOwner, walletDelegate);

        DelegateSet tx = new DelegateSet
        {
            Account = walletOwner.ClassicAddress,
            Authorize = walletDelegate.ClassicAddress,
            Permissions = new List<PermissionWrapper>
            {
                new PermissionWrapper { Permission = new PermissionEntry { PermissionValue = 1 } },
            },
        };
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, walletOwner, true);
        ValidateResult(result);

        // Verify the LODelegate ledger object was created with correct fields
        LODelegate delegateObj = await GetDelegateObject(walletOwner.ClassicAddress);
        Assert.IsNotNull(delegateObj, "LODelegate object should exist after DelegateSet");
        Assert.AreEqual(walletOwner.ClassicAddress, delegateObj.Account);
        Assert.AreEqual(walletDelegate.ClassicAddress, delegateObj.Authorize);
        Assert.IsNotNull(delegateObj.Permissions);
        Assert.HasCount(1, delegateObj.Permissions);
        Assert.AreEqual((uint)1, delegateObj.Permissions[0].Permission.PermissionValue);
    }

    [TestMethod]
    public async Task TestDelegateSet_MultiplePermissions()
    {
        XrplWallet walletOwner = XrplWallet.Generate();
        XrplWallet walletDelegate = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletOwner, walletDelegate);

        DelegateSet tx = new DelegateSet
        {
            Account = walletOwner.ClassicAddress,
            Authorize = walletDelegate.ClassicAddress,
            Permissions = new List<PermissionWrapper>
            {
                new PermissionWrapper { Permission = new PermissionEntry { PermissionValue = 1 } },
                new PermissionWrapper { Permission = new PermissionEntry { PermissionValue = 2 } },
                new PermissionWrapper { Permission = new PermissionEntry { PermissionValue = 3 } },
            },
        };
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, walletOwner, true);
        ValidateResult(result);

        // Verify the LODelegate ledger object has all 3 permissions
        LODelegate delegateObj = await GetDelegateObject(walletOwner.ClassicAddress);
        Assert.IsNotNull(delegateObj, "LODelegate object should exist after DelegateSet");
        Assert.AreEqual(walletOwner.ClassicAddress, delegateObj.Account);
        Assert.AreEqual(walletDelegate.ClassicAddress, delegateObj.Authorize);
        Assert.IsNotNull(delegateObj.Permissions);
        Assert.HasCount(3, delegateObj.Permissions);

        List<uint> expectedValues = [1, 2, 3,];
        List<uint> actualValues = delegateObj.Permissions
            .Select(p => p.Permission.PermissionValue)
            .OrderBy(v => v)
            .ToList();
        CollectionAssert.AreEqual(expectedValues, actualValues);
    }

    /// <summary>
    /// The other half of delegation: not granting the permission, but exercising it.
    /// The delegate submits a transaction whose Account is the owner and whose sfDelegate
    /// names the delegate — the only field that distinguishes a delegated transaction from
    /// an ordinary one, and the reason it belongs on ITransactionCommon.
    /// </summary>
    [TestMethod]
    public async Task TestDelegatedPayment_DelegateFieldSurvivesTheLedgerRoundTrip()
    {
        XrplWallet walletOwner = XrplWallet.Generate();
        XrplWallet walletDelegate = XrplWallet.Generate();
        XrplWallet walletDestination = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletOwner, walletDelegate, walletDestination);

        // PermissionValue 1 == ttPAYMENT (0) + 1
        DelegateSet grant = new DelegateSet
        {
            Account = walletOwner.ClassicAddress,
            Authorize = walletDelegate.ClassicAddress,
            Permissions = new List<PermissionWrapper>
            {
                new PermissionWrapper { Permission = new PermissionEntry { PermissionValue = 1 } },
            },
        };
        grant = await client.Autofill(grant);
        ValidateResult(await client.SubmitAndWait(grant, walletOwner, true));

        // The delegate signs, but the transaction is the owner's
        Payment payment = new Payment
        {
            Account = walletOwner.ClassicAddress,
            Destination = walletDestination.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1 },
            Delegate = walletDelegate.ClassicAddress,
        };
        payment = await client.Autofill(payment);

        TransactionSummary result = await client.SubmitAndWait(payment, walletDelegate, true);
        ValidateResult(result);

        // Back out of the ledger into the typed model
        TransactionResponse readBack = (await client.Tx(new TxRequest(result.Hash))).Result;
        Assert.AreEqual(walletDelegate.ClassicAddress, readBack.Delegate, "Delegate must survive the ledger round trip");
        Assert.AreEqual(walletOwner.ClassicAddress, readBack.Account, "the transaction stays the owner's");

        // And through the interface, which is what generic code holds
        ITransactionCommon asCommon = readBack;
        Assert.AreEqual(walletDelegate.ClassicAddress, asCommon.Delegate);
    }
}
