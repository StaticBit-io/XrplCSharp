using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
[TestCategory("Delegate")]
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

    [TestMethod]
    public async Task TestDelegateSet_Basic()
    {
        XrplWallet walletOwner = XrplWallet.Generate();
        XrplWallet walletDelegate = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, walletOwner, walletDelegate);

        DelegateSet tx = new DelegateSet
        {
            Account = walletOwner.ClassicAddress,
            Delegate = walletDelegate.ClassicAddress,
            Permissions = new List<PermissionWrapper>
            {
                new PermissionWrapper { Permission = new PermissionEntry { PermissionValue = 1 } },
            },
        };
        tx = await client.Autofill(tx);

        TransactionSummary result = await client.SubmitAndWait(tx, walletOwner, true);
        ValidateResult(result);
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
            Delegate = walletDelegate.ClassicAddress,
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
    }
}
