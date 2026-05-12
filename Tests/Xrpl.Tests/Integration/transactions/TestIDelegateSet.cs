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
[Ignore("PermissionDelegation amendment removed in v2.6.1 due to a bug; replacement PermissionDelegationV1_1 not yet released (XLS-75)")]
public class TestIDelegateSet
{
    public TestContext TestContext { get; set; }
    private static IXrplClient client;
    private static TestNodeType nodeType = TestNodeType.Standalone;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(TestNodeType.Standalone);
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
        AccountObjects response = await client.AccountObjects(request);

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
        Assert.AreEqual(walletDelegate.ClassicAddress, delegateObj.Delegate);
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
        Assert.AreEqual(walletDelegate.ClassicAddress, delegateObj.Delegate);
        Assert.IsNotNull(delegateObj.Permissions);
        Assert.HasCount(3, delegateObj.Permissions);

        List<uint> expectedValues = [1, 2, 3,];
        List<uint> actualValues = delegateObj.Permissions
            .Select(p => p.Permission.PermissionValue)
            .OrderBy(v => v)
            .ToList();
        CollectionAssert.AreEqual(expectedValues, actualValues);
    }
}
