using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Newtonsoft.Json;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

using Common = Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
[DoNotParallelize]
public class TestIMPToken
{
    public TestContext TestContext { get; set; }
    public static IXrplClient client;

    static XrplWallet walletIssuer = XrplWallet.FromNormalizedText("mpt issuer test account");
    static XrplWallet walletHolder1 = XrplWallet.FromNormalizedText("mpt holder test account 1");
    static XrplWallet walletHolder2 = XrplWallet.FromNormalizedText("mpt holder test account 2");

    private static string lastIssuanceId;

    [ClassInitialize]
    public static async Task MyClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(TestNodeType.DevNet);

        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer);
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder1);
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder2);
    }

    [ClassCleanup]
    public static void AfterAllTests()
    {
        client.Dispose();
    }

    #region MPTokenIssuanceCreate Tests

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_Basic()
    {
        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);

        lastIssuanceId = GetMPTokenIssuanceIdFromMeta(result);
        Assert.IsNotNull(lastIssuanceId, "MPTokenIssuanceID should be returned in metadata");
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithAssetScale()
    {
        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            AssetScale = 2,
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithTransferFee()
    {
        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            TransferFee = 1000,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer,
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithMaximumAmount()
    {
        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            MaximumAmount = "1000000000",
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithMetadata()
    {
        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenMetadata = "48656C6C6F20576F726C64",
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithCanTransferFlag()
    {
        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer,
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithRequireAuthFlag()
    {
        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTRequireAuth,
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);

        lastIssuanceId = GetMPTokenIssuanceIdFromMeta(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithAllFlags()
    {
        var allFlags = MPTokenIssuanceCreateFlags.tfMPTCanLock |
                       MPTokenIssuanceCreateFlags.tfMPTCanTransfer |
                       MPTokenIssuanceCreateFlags.tfMPTCanTrade |
                       MPTokenIssuanceCreateFlags.tfMPTCanClawback;

        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = allFlags,
            AssetScale = 6,
            TransferFee = 500,
            MaximumAmount = "9223372036854775807",
            MPTokenMetadata = "4D5054",
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);

        lastIssuanceId = GetMPTokenIssuanceIdFromMeta(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithCanEscrowFlag()
    {
        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanEscrow,
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(result);
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID should be returned for tfMPTCanEscrow");
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithCanTradeFlag()
    {
        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTrade,
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(result);
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID should be returned for tfMPTCanTrade");
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceCreate_WithCanClawbackFlag()
    {
        var tx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanClawback,
        };
        tx = await client.Autofill(tx);

        var result = await client.SubmitAndWait(tx, walletIssuer, true);
        ValidateResult(result);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(result);
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID should be returned for tfMPTCanClawback");
    }

    #endregion

    #region MPTokenIssuanceDestroy Tests

    [TestMethod]
    public async Task TestMPTokenIssuanceDestroy_Basic()
    {
        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);
        Assert.IsNotNull(issuanceId);

        var destroyTx = new MPTokenIssuanceDestroy
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        destroyTx = await client.Autofill(destroyTx);

        var result = await client.SubmitAndWait(destroyTx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceDestroy_FailsWithOutstandingHolders()
    {
        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);
        Assert.IsNotNull(issuanceId);

        var authTx = new MPTokenAuthorize
        {
            Account = walletHolder1.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        authTx = await client.Autofill(authTx);
        var authResult = await client.SubmitAndWait(authTx, walletHolder1, true);
        ValidateResult(authResult);

        var paymentTx = new Payment
        {
            Account = walletIssuer.ClassicAddress,
            Destination = walletHolder1.ClassicAddress,
            Amount = new Currency() { MPTokenIssuanceID = issuanceId, Value = "1000"},
        };
        paymentTx = await client.Autofill(paymentTx);
        var paymentResult = await client.SubmitAndWait(paymentTx, walletIssuer, true);
        ValidateResult(paymentResult);

        var destroyTx = new MPTokenIssuanceDestroy
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        destroyTx = await client.Autofill(destroyTx);

        await Helper.ThrowsExceptionAsync<RippleException>(
            () => client.SubmitAndWait(destroyTx, walletIssuer, true),
            "Final tx result is not success: tecHAS_OBLIGATIONS");
    }

    #endregion

    #region MPTokenIssuanceSet Tests

    [TestMethod]
    public async Task TestMPTokenIssuanceSet_GlobalLock()
    {
        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanLock,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);

        var lockTx = new MPTokenIssuanceSet
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Flags = MPTokenIssuanceSetFlags.tfMPTLock,
        };
        lockTx = await client.Autofill(lockTx);

        var result = await client.SubmitAndWait(lockTx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceSet_GlobalUnlock()
    {
        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanLock,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);

        var lockTx = new MPTokenIssuanceSet
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Flags = MPTokenIssuanceSetFlags.tfMPTLock,
        };
        lockTx = await client.Autofill(lockTx);
        await client.SubmitAndWait(lockTx, walletIssuer, true);

        var unlockTx = new MPTokenIssuanceSet
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Flags = MPTokenIssuanceSetFlags.tfMPTUnlock,
        };
        unlockTx = await client.Autofill(unlockTx);

        var result = await client.SubmitAndWait(unlockTx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceSet_LockSpecificHolder()
    {
        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = (MPTokenIssuanceCreateFlags.tfMPTCanLock | MPTokenIssuanceCreateFlags.tfMPTCanTransfer),
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);

        var authTx = new MPTokenAuthorize
        {
            Account = walletHolder1.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        authTx = await client.Autofill(authTx);
        await client.SubmitAndWait(authTx, walletHolder1, true);

        var lockTx = new MPTokenIssuanceSet
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Holder = walletHolder1.ClassicAddress,
            Flags = MPTokenIssuanceSetFlags.tfMPTLock,
        };
        lockTx = await client.Autofill(lockTx);

        var result = await client.SubmitAndWait(lockTx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenIssuanceSet_UnlockSpecificHolder()
    {
        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = (MPTokenIssuanceCreateFlags.tfMPTCanLock | MPTokenIssuanceCreateFlags.tfMPTCanTransfer),
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);

        var authTx = new MPTokenAuthorize
        {
            Account = walletHolder1.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        authTx = await client.Autofill(authTx);
        await client.SubmitAndWait(authTx, walletHolder1, true);

        var lockTx = new MPTokenIssuanceSet
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Holder = walletHolder1.ClassicAddress,
            Flags = MPTokenIssuanceSetFlags.tfMPTLock,
        };
        lockTx = await client.Autofill(lockTx);
        var lockResult = await client.SubmitAndWait(lockTx, walletIssuer, true);
        ValidateResult(lockResult);

        var lockedFlags = GetMPTokenFlagsFromMeta(lockResult);
        Assert.IsTrue(lockedFlags.HasValue, "MPToken flags should exist in lock tx metadata");
        Assert.IsTrue((lockedFlags.Value & MPTokenFlags.lsfMPTLocked) != 0, "MPToken should have lock flag set after lock");

        var unlockTx = new MPTokenIssuanceSet
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Holder = walletHolder1.ClassicAddress,
            Flags = MPTokenIssuanceSetFlags.tfMPTUnlock,
        };
        unlockTx = await client.Autofill(unlockTx);

        var unlockResult = await client.SubmitAndWait(unlockTx, walletIssuer, true);
        ValidateResult(unlockResult);

        var unlockedFlags = GetMPTokenFlagsFromMeta(unlockResult);
        Assert.IsTrue(unlockedFlags.HasValue, "MPToken flags should exist in unlock tx metadata");
        Assert.IsTrue((unlockedFlags.Value & MPTokenFlags.lsfMPTLocked) == 0, "MPToken should have lock flag cleared after unlock");
    }

    #endregion

    #region MPTokenAuthorize Tests

    [TestMethod]
    public async Task TestMPTokenAuthorize_HolderOptIn()
    {
        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);

        var authTx = new MPTokenAuthorize
        {
            Account = walletHolder1.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        authTx = await client.Autofill(authTx);

        var result = await client.SubmitAndWait(authTx, walletHolder1, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenAuthorize_IssuerAuthorizesHolder()
    {
        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTRequireAuth,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);

        var holderAuthTx = new MPTokenAuthorize
        {
            Account = walletHolder2.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        holderAuthTx = await client.Autofill(holderAuthTx);
        await client.SubmitAndWait(holderAuthTx, walletHolder2, true);

        var issuerAuthTx = new MPTokenAuthorize
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Holder = walletHolder2.ClassicAddress,
        };
        issuerAuthTx = await client.Autofill(issuerAuthTx);

        var result = await client.SubmitAndWait(issuerAuthTx, walletIssuer, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenAuthorize_HolderUnauthorize()
    {
        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);

        var authTx = new MPTokenAuthorize
        {
            Account = walletHolder1.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        authTx = await client.Autofill(authTx);
        await client.SubmitAndWait(authTx, walletHolder1, true);

        var unauthTx = new MPTokenAuthorize
        {
            Account = walletHolder1.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Flags = MPTokenAuthorizeFlags.tfMPTUnauthorize,
        };
        unauthTx = await client.Autofill(unauthTx);

        var result = await client.SubmitAndWait(unauthTx, walletHolder1, true);
        ValidateResult(result);
    }

    [TestMethod]
    public async Task TestMPTokenAuthorize_IssuerRevokesAuthorization()
    {
        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTRequireAuth,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);

        var holderAuthTx = new MPTokenAuthorize
        {
            Account = walletHolder2.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        holderAuthTx = await client.Autofill(holderAuthTx);
        await client.SubmitAndWait(holderAuthTx, walletHolder2, true);

        var issuerAuthTx = new MPTokenAuthorize
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Holder = walletHolder2.ClassicAddress,
        };
        issuerAuthTx = await client.Autofill(issuerAuthTx);
        await client.SubmitAndWait(issuerAuthTx, walletIssuer, true);

        var revokeTx = new MPTokenAuthorize
        {
            Account = walletIssuer.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
            Holder = walletHolder2.ClassicAddress,
            Flags = MPTokenAuthorizeFlags.tfMPTUnauthorize,
        };
        revokeTx = await client.Autofill(revokeTx);

        var result = await client.SubmitAndWait(revokeTx, walletIssuer, true);
        ValidateResult(result);
    }

    #endregion

    #region MPT Payment and Balance Tests

    [TestMethod]
    public async Task TestMPTPayment_TransferAndVerifyBalance()
    {
        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID should be returned in metadata");
        Console.WriteLine($"Created MPT Issuance: {issuanceId}");

        var authTx = new MPTokenAuthorize
        {
            Account = walletHolder1.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        authTx = await client.Autofill(authTx);
        var authResult = await client.SubmitAndWait(authTx, walletHolder1, true);
        ValidateResult(authResult);
        Console.WriteLine($"Holder authorized for MPT");

        const ulong transferAmount = 1000;
        var paymentTx = new Payment
        {
            Account = walletIssuer.ClassicAddress,
            Destination = walletHolder1.ClassicAddress,
            Amount = new Currency { MPTokenIssuanceID = issuanceId, Value = transferAmount.ToString() },
        };
        paymentTx = await client.Autofill(paymentTx);
        var paymentResult = await client.SubmitAndWait(paymentTx, walletIssuer, true);
        ValidateResult(paymentResult);
        Console.WriteLine($"Payment sent: {transferAmount} MPT");

        var ledgerEntryRequest = new LedgerEntryRequest
        {
            LedgerIndex = new LedgerIndex(LedgerIndexType.Validated),
            LedgerEntryRequestType = LedgerEntryRequestType.MPToken,
            MPToken = new MPTokenQuery
            {
                Account = walletHolder1.ClassicAddress,
                MPTokenIssuanceID = issuanceId,
            },
        };
        var ledgerEntryResponse = await client.LedgerEntry(ledgerEntryRequest);
        
        Assert.IsNotNull(ledgerEntryResponse, "LedgerEntry response should not be null");
        Assert.IsNotNull(ledgerEntryResponse.Node, "LedgerEntry node should not be null");
        
        var mpToken = ledgerEntryResponse.Node as LOMPToken;
        Assert.IsNotNull(mpToken, "Node should be LOMPToken type");
        Assert.AreEqual(walletHolder1.ClassicAddress, mpToken.Account, "MPToken account should match holder");
        Assert.AreEqual(issuanceId, mpToken.MPTokenIssuanceID, "MPTokenIssuanceID should match");
        
        Console.WriteLine($"MPToken balance: {mpToken.MPTAmount}");
        Assert.IsNotNull(mpToken.MPTAmount, "MPTAmount should not be null after payment");
        Assert.AreEqual(transferAmount, mpToken.MPTAmount.Value, $"MPTAmount should be {transferAmount}");
    }

    #endregion

    #region Helper Methods

    private static void ValidateResult(Submit res)
    {
        if (res is not { EngineResult: "tesSUCCESS" or "terQUEUED" })
        {
            throw new RippleException($"Transaction failed: {res.EngineResult}");
        }
    }

    private static void ValidateResult(TransactionSummary res)
    {
        if (res is not { Meta: { TransactionResult: "tesSUCCESS" or "terQUEUED" } })
        {
            throw new RippleException($"Transaction failed: {res.Meta?.TransactionResult}");
        }
    }

    private static string GetMPTokenIssuanceIdFromMeta(TransactionSummary result)
    {
        return result.Meta?.MptIssuanceId;
    }

    private static string GetMPTokenFromMeta(TransactionSummary result)
    {
        try
        {
            if (result.Meta?.AffectedNodes != null)
            {
                foreach (var node in result.Meta.AffectedNodes)
                {
                    if (node.ModifiedNode?.LedgerEntryType == LedgerEntryType.MPToken)
                    {
                        return node.ModifiedNode.LedgerIndex;
                    }
                    if (node.CreatedNode?.LedgerEntryType == LedgerEntryType.MPToken)
                    {
                        return node.CreatedNode.LedgerIndex;
                    }
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private static MPTokenFlags? GetMPTokenFlagsFromMeta(TransactionSummary result)
    {
        try
        {
            if (result.Meta?.AffectedNodes != null)
            {
                foreach (var node in result.Meta.AffectedNodes)
                {
                    if (node.ModifiedNode is { LedgerEntryType: LedgerEntryType.MPToken, Final: LOMPToken { } finalFields })
                    {
                        if (finalFields is { Flags: not null })
                        {
                            return finalFields.Flags;
                        }
                    }
                }
            }
        }
        catch
        {
        }
        return null;
    }
    #endregion
}