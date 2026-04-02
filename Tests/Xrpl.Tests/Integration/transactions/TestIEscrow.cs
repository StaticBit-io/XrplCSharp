using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;
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
[DoNotParallelize]
public class TestIEscrow
{
    public TestContext TestContext { get; set; }
    public static IXrplClient client;

    private static TestNodeType nodeType = TestNodeType.Standalone;
    //static XrplWallet walletIssuer = XrplWallet.Generate();
    //static XrplWallet walletHolder1 = XrplWallet.Generate();

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

    #region XRP Escrow Tests

    [TestMethod]
    public async Task TestXrpEscrowCreate_AndFinish()
    {
        var walletIssuer = XrplWallet.Generate();
        var walletHolder1 = XrplWallet.Generate();
        var walletHolder2 = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder1, nodeType);
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder2, nodeType);

        LedgerRequest ledgerReq = new LedgerRequest() { LedgerIndex = new LedgerIndex(LedgerIndexType.Validated) };
        LOLedger ledgerResponse = await client.Ledger(ledgerReq);
        LedgerEntity ledgerEntity = (LedgerEntity)ledgerResponse.LedgerEntity;
        var closeTime = ledgerEntity.CloseTime;

        var escrowCreateTx = new EscrowCreate
        {
            Account = walletHolder1.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1 },
            Destination = walletHolder2.ClassicAddress,
            FinishAfter = closeTime + TimeSpan.FromSeconds(2),
        };
        escrowCreateTx = await client.Autofill(escrowCreateTx);
        uint escrowSequence = (uint)escrowCreateTx.Sequence;
        var escrowCreateResult = await client.SubmitAndWait(escrowCreateTx, walletHolder1, true);
        ValidateResult(escrowCreateResult);

        AccountObjectsRequest objReq = new AccountObjectsRequest(walletHolder1.ClassicAddress) { Type = LedgerEntryType.Escrow };
        AccountObjects objResp = await client.AccountObjects(objReq);
        Assert.IsTrue(objResp.AccountObjectList.Count >= 1, "At least one escrow should exist after creation");

        await WaitForLedgerCloseTime(client, closeTime.Value + TimeSpan.FromSeconds(2));

        EscrowFinish finishTx = new EscrowFinish
        {
            Account = walletHolder1.ClassicAddress,
            Owner = walletHolder1.ClassicAddress,
            OfferSequence = escrowSequence,
        };
        finishTx = await client.Autofill(finishTx);
        var finishResult = await client.SubmitAndWait(finishTx, walletHolder1, true);
        ValidateResult(finishResult);
    }

    [TestMethod]
    public async Task TestXrpEscrowCreate_AndCancel()
    {
        var walletIssuer = XrplWallet.Generate();
        var walletHolder1 = XrplWallet.Generate();
        var walletHolder2 = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder1, nodeType);
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder2, nodeType);

        LedgerRequest ledgerReq = new LedgerRequest() { LedgerIndex = new LedgerIndex(LedgerIndexType.Validated) };
        LOLedger ledgerResponse = await client.Ledger(ledgerReq);
        LedgerEntity ledgerEntity = (LedgerEntity)ledgerResponse.LedgerEntity;
        var closeTime = ledgerEntity.CloseTime;

        var cancelAfterTime = closeTime + TimeSpan.FromSeconds(10);
        var escrowCreateTx = new EscrowCreate
        {
            Account = walletHolder1.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1 },
            Destination = walletHolder2.ClassicAddress,
            CancelAfter = cancelAfterTime,
            FinishAfter = closeTime + TimeSpan.FromSeconds(2),
        };
        escrowCreateTx = await client.Autofill(escrowCreateTx);
        uint escrowSequence = (uint)escrowCreateTx.Sequence;
        var escrowCreateResult = await client.SubmitAndWait(escrowCreateTx, walletHolder1, true);
        ValidateResult(escrowCreateResult);

        AccountObjectsRequest objReq = new AccountObjectsRequest(walletHolder1.ClassicAddress) { Type = LedgerEntryType.Escrow };
        AccountObjects objResp = await client.AccountObjects(objReq);
        Assert.IsTrue(objResp.AccountObjectList.Count >= 1, "At least one escrow should exist after creation");

        await WaitForLedgerCloseTime(client, cancelAfterTime.Value);

        EscrowCancel cancelTx = new EscrowCancel
        {
            Account = walletHolder1.ClassicAddress,
            Owner = walletHolder1.ClassicAddress,
            OfferSequence = escrowSequence,
        };
        cancelTx = await client.Autofill(cancelTx);
        var cancelResult = await client.SubmitAndWait(cancelTx, walletHolder1, true);
        ValidateResult(cancelResult);
    }

    #endregion

    #region IOU Escrow Tests

    [TestMethod]
    public async Task TestIOUEscrowCreate_AndFinish()
    {
        var walletIssuer = XrplWallet.Generate();
        var walletHolder1 = XrplWallet.Generate();
        var walletHolder2 = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder1, nodeType);
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder2, nodeType);

        var accountSetTx = new AccountSet
        {
            Account = walletIssuer.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfAllowTrustLineLocking,
        };
        accountSetTx = await client.Autofill(accountSetTx);
        var accountSetResult = await client.SubmitAndWait(accountSetTx, walletIssuer, true);
        ValidateResult(accountSetResult);

        var trustSetHolder1 = new TrustSet
        {
            Account = walletHolder1.ClassicAddress,
            LimitAmount = new Currency { CurrencyCode = "USD", Issuer = walletIssuer.ClassicAddress, Value = "10000000" },
        };
        trustSetHolder1 = await client.Autofill(trustSetHolder1);
        var trustResult1 = await client.SubmitAndWait(trustSetHolder1, walletHolder1, true);
        ValidateResult(trustResult1);

        var paymentTx = new Payment
        {
            Account = walletIssuer.ClassicAddress,
            Destination = walletHolder1.ClassicAddress,
            Amount = new Currency { CurrencyCode = "USD", Issuer = walletIssuer.ClassicAddress, Value = "1000" },
        };
        paymentTx = await client.Autofill(paymentTx);
        var payResult = await client.SubmitAndWait(paymentTx, walletIssuer, true);
        ValidateResult(payResult);

        var trustSetHolder2 = new TrustSet
        {
            Account = walletHolder2.ClassicAddress,
            LimitAmount = new Currency { CurrencyCode = "USD", Issuer = walletIssuer.ClassicAddress, Value = "10000000" },
        };
        trustSetHolder2 = await client.Autofill(trustSetHolder2);
        var trustResult2 = await client.SubmitAndWait(trustSetHolder2, walletHolder2, true);
        ValidateResult(trustResult2);

        LedgerRequest ledgerReq = new LedgerRequest() { LedgerIndex = new LedgerIndex(LedgerIndexType.Validated) };
        LOLedger ledgerResponse = await client.Ledger(ledgerReq);
        LedgerEntity ledgerEntity = (LedgerEntity)ledgerResponse.LedgerEntity;
        var closeTime = ledgerEntity.CloseTime;

        var escrowCreateTx = new EscrowCreate
        {
            Account = walletHolder1.ClassicAddress,
            Amount = new Currency { CurrencyCode = "USD", Issuer = walletIssuer.ClassicAddress, Value = "100" },
            Destination = walletHolder2.ClassicAddress,
            FinishAfter = closeTime + TimeSpan.FromSeconds(2),
        };
        escrowCreateTx = await client.Autofill(escrowCreateTx);
        uint escrowSequence = (uint)escrowCreateTx.Sequence;
        var escrowCreateResult = await client.SubmitAndWait(escrowCreateTx, walletHolder1, true);
        ValidateResult(escrowCreateResult);

        AccountObjectsRequest objReq = new AccountObjectsRequest(walletHolder1.ClassicAddress) { Type = LedgerEntryType.Escrow };
        AccountObjects objResp = await client.AccountObjects(objReq);
        Assert.IsTrue(objResp.AccountObjectList.Count >= 1, "At least one escrow should exist after creation");

        await WaitForLedgerCloseTime(client, closeTime.Value + TimeSpan.FromSeconds(2));

        EscrowFinish finishTx = new EscrowFinish
        {
            Account = walletHolder1.ClassicAddress,
            Owner = walletHolder1.ClassicAddress,
            OfferSequence = escrowSequence,
        };
        finishTx = await client.Autofill(finishTx);
        var finishResult = await client.SubmitAndWait(finishTx, walletHolder1, true);
        ValidateResult(finishResult);
    }

    [TestMethod]
    public async Task TestIOUEscrowCreate_AndCancel()
    {
        var walletIssuer = XrplWallet.Generate();
        var walletHolder1 = XrplWallet.Generate();
        var walletHolder2 = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder1, nodeType);
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder2, nodeType);

        var accountSetTx = new AccountSet
        {
            Account = walletIssuer.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfAllowTrustLineLocking,
        };
        accountSetTx = await client.Autofill(accountSetTx);
        var accountSetResult = await client.SubmitAndWait(accountSetTx, walletIssuer, true);
        ValidateResult(accountSetResult);

        var trustSetHolder1 = new TrustSet
        {
            Account = walletHolder1.ClassicAddress,
            LimitAmount = new Currency { CurrencyCode = "USD", Issuer = walletIssuer.ClassicAddress, Value = "10000000" },
        };
        trustSetHolder1 = await client.Autofill(trustSetHolder1);
        var trustResult1 = await client.SubmitAndWait(trustSetHolder1, walletHolder1, true);
        ValidateResult(trustResult1);

        var paymentTx = new Payment
        {
            Account = walletIssuer.ClassicAddress,
            Destination = walletHolder1.ClassicAddress,
            Amount = new Currency { CurrencyCode = "USD", Issuer = walletIssuer.ClassicAddress, Value = "1000" },
        };
        paymentTx = await client.Autofill(paymentTx);
        var payResult = await client.SubmitAndWait(paymentTx, walletIssuer, true);
        ValidateResult(payResult);

        var trustSetHolder2 = new TrustSet
        {
            Account = walletHolder2.ClassicAddress,
            LimitAmount = new Currency { CurrencyCode = "USD", Issuer = walletIssuer.ClassicAddress, Value = "10000000" },
        };
        trustSetHolder2 = await client.Autofill(trustSetHolder2);
        var trustResult2 = await client.SubmitAndWait(trustSetHolder2, walletHolder2, true);
        ValidateResult(trustResult2);

        LedgerRequest ledgerReq = new LedgerRequest() { LedgerIndex = new LedgerIndex(LedgerIndexType.Validated) };
        LOLedger ledgerResponse = await client.Ledger(ledgerReq);
        LedgerEntity ledgerEntity = (LedgerEntity)ledgerResponse.LedgerEntity;
        var closeTime = ledgerEntity.CloseTime;

        var cancelAfterTime = closeTime + TimeSpan.FromSeconds(10);
        var escrowCreateTx = new EscrowCreate
        {
            Account = walletHolder1.ClassicAddress,
            Amount = new Currency { CurrencyCode = "USD", Issuer = walletIssuer.ClassicAddress, Value = "100" },
            Destination = walletHolder2.ClassicAddress,
            CancelAfter = cancelAfterTime,
            FinishAfter = closeTime + TimeSpan.FromSeconds(2),
        };
        escrowCreateTx = await client.Autofill(escrowCreateTx);
        uint escrowSequence = (uint)escrowCreateTx.Sequence;
        var escrowCreateResult = await client.SubmitAndWait(escrowCreateTx, walletHolder1, true);
        ValidateResult(escrowCreateResult);

        AccountObjectsRequest objReq = new AccountObjectsRequest(walletHolder1.ClassicAddress) { Type = LedgerEntryType.Escrow };
        AccountObjects objResp = await client.AccountObjects(objReq);
        Assert.IsTrue(objResp.AccountObjectList.Count >= 1, "At least one escrow should exist after creation");

        await WaitForLedgerCloseTime(client, cancelAfterTime.Value);

        EscrowCancel cancelTx = new EscrowCancel
        {
            Account = walletHolder1.ClassicAddress,
            Owner = walletHolder1.ClassicAddress,
            OfferSequence = escrowSequence,
        };
        cancelTx = await client.Autofill(cancelTx);
        var cancelResult = await client.SubmitAndWait(cancelTx, walletHolder1, true);
        ValidateResult(cancelResult);
    }

    #endregion

    #region MPT Escrow Tests

    [TestMethod]
    public async Task TestMPTEscrowCreate_AndFinish()
    {
        var walletIssuer = XrplWallet.Generate();
        var walletHolder1 = XrplWallet.Generate();
        var walletHolder2 = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder1, nodeType);
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder2, nodeType);

        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer | MPTokenIssuanceCreateFlags.tfMPTCanEscrow,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID should be returned in metadata");

        var accountSetTx = new AccountSet
        {
            Account = walletIssuer.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfAllowTrustLineLocking,
        };
        accountSetTx = await client.Autofill(accountSetTx);
        var accountSetResult = await client.SubmitAndWait(accountSetTx, walletIssuer, true);
        ValidateResult(accountSetResult);

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
            Amount = new Currency { MPTokenIssuanceID = issuanceId, Value = "10000" },
        };
        paymentTx = await client.Autofill(paymentTx);
        var payResult = await client.SubmitAndWait(paymentTx, walletIssuer, true);
        ValidateResult(payResult);

        var authHolder2 = new MPTokenAuthorize
        {
            Account = walletHolder2.ClassicAddress,
            MPTokenIssuanceID = issuanceId,
        };
        authHolder2 = await client.Autofill(authHolder2);
        var authResult2 = await client.SubmitAndWait(authHolder2, walletHolder2, true);
        ValidateResult(authResult2);

        LedgerRequest ledgerReq = new LedgerRequest() { LedgerIndex = new LedgerIndex(LedgerIndexType.Validated) };
        LOLedger ledgerResponse = await client.Ledger(ledgerReq);
        LedgerEntity ledgerEntity = (LedgerEntity)ledgerResponse.LedgerEntity;
        var closeTime = ledgerEntity.CloseTime;

        var escrowCreateTx = new EscrowCreate
        {
            Account = walletHolder1.ClassicAddress,
            Amount = new Currency { MPTokenIssuanceID = issuanceId, Value = "1000" },
            Destination = walletHolder2.ClassicAddress,
            FinishAfter = closeTime + TimeSpan.FromSeconds(2),
        };

        escrowCreateTx = await client.Autofill(escrowCreateTx);
        uint escrowSequence = (uint)escrowCreateTx.Sequence;
        var escrowCreateResult = await client.SubmitAndWait(escrowCreateTx, walletHolder1, true);
        ValidateResult(escrowCreateResult);

        AccountObjectsRequest objReq = new AccountObjectsRequest(walletHolder1.ClassicAddress) { Type = LedgerEntryType.Escrow };
        AccountObjects objResp = await client.AccountObjects(objReq);
        Assert.IsTrue(objResp.AccountObjectList.Count >= 1, "At least one escrow should exist after creation");

        await WaitForLedgerCloseTime(client, closeTime.Value + TimeSpan.FromSeconds(2));


        EscrowFinish finishTx = new EscrowFinish
        {
            Account = walletHolder1.ClassicAddress,
            Owner = walletHolder1.ClassicAddress,
            OfferSequence = escrowSequence,
        };
        finishTx = await client.Autofill(finishTx);
        var finishResult = await client.SubmitAndWait(finishTx, walletHolder1, true);
        ValidateResult(finishResult);
    }

    [TestMethod]
    public async Task TestMPTEscrowCreate_AndCancel()
    {
        var walletIssuer = XrplWallet.Generate();
        var walletHolder1 = XrplWallet.Generate();
        var walletHolder2 = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletAsync(client, walletIssuer, nodeType);
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder1, nodeType);
        await IntegrationTestConfig.TryFundWalletAsync(client, walletHolder2, nodeType);

        var createTx = new MPTokenIssuanceCreate
        {
            Account = walletIssuer.ClassicAddress,
            Flags = MPTokenIssuanceCreateFlags.tfMPTCanTransfer | MPTokenIssuanceCreateFlags.tfMPTCanEscrow,
        };
        createTx = await client.Autofill(createTx);
        var createResult = await client.SubmitAndWait(createTx, walletIssuer, true);
        ValidateResult(createResult);

        var issuanceId = GetMPTokenIssuanceIdFromMeta(createResult);
        Assert.IsNotNull(issuanceId, "MPTokenIssuanceID should be returned in metadata");

        var accountSetTx = new AccountSet
        {
            Account = walletIssuer.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfAllowTrustLineLocking,
        };
        accountSetTx = await client.Autofill(accountSetTx);
        var accountSetResult = await client.SubmitAndWait(accountSetTx, walletIssuer, true);
        ValidateResult(accountSetResult);

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
            Amount = new Currency { MPTokenIssuanceID = issuanceId, Value = "10000" },
        };
        paymentTx = await client.Autofill(paymentTx);
        var payResult = await client.SubmitAndWait(paymentTx, walletIssuer, true);
        ValidateResult(payResult);

        LedgerRequest ledgerReq = new LedgerRequest() { LedgerIndex = new LedgerIndex(LedgerIndexType.Validated) };
        LOLedger ledgerResponse = await client.Ledger(ledgerReq);
        LedgerEntity ledgerEntity = (LedgerEntity)ledgerResponse.LedgerEntity;
        var closeTime = ledgerEntity.CloseTime;

        var cancelAfterTime = closeTime + TimeSpan.FromSeconds(10);
        var escrowCreateTx = new EscrowCreate
        {
            Account = walletHolder1.ClassicAddress,
            Amount = new Currency { MPTokenIssuanceID = issuanceId, Value = "1000" },
            Destination = walletHolder2.ClassicAddress,
            CancelAfter = cancelAfterTime,
            FinishAfter = closeTime + TimeSpan.FromSeconds(2),
        };
        escrowCreateTx = await client.Autofill(escrowCreateTx);
        uint escrowSequence = (uint)escrowCreateTx.Sequence;
        var escrowCreateResult = await client.SubmitAndWait(escrowCreateTx, walletHolder1, true);
        ValidateResult(escrowCreateResult);

        AccountObjectsRequest objReq = new AccountObjectsRequest(walletHolder1.ClassicAddress) { Type = LedgerEntryType.Escrow };
        AccountObjects objResp = await client.AccountObjects(objReq);
        Assert.IsTrue(objResp.AccountObjectList.Count >= 1, "At least one escrow should exist after creation");

        await WaitForLedgerCloseTime(client, cancelAfterTime.Value);

        EscrowCancel cancelTx = new EscrowCancel
        {
            Account = walletHolder1.ClassicAddress,
            Owner = walletHolder1.ClassicAddress,
            OfferSequence = escrowSequence,
        };
        cancelTx = await client.Autofill(cancelTx);
        var cancelResult = await client.SubmitAndWait(cancelTx, walletHolder1, true);
        ValidateResult(cancelResult);
    }

    #endregion

    #region Helper Methods

    private static async Task WaitForLedgerCloseTime(IXrplClient client, DateTime targetTime, int maxWaitSeconds = 60)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < maxWaitSeconds+10)
        {
            await Task.Delay(3000);
            LedgerRequest req = new LedgerRequest() { LedgerIndex = new LedgerIndex(LedgerIndexType.Validated) };
            LOLedger resp = await client.Ledger(req);
            LedgerEntity entity = (LedgerEntity)resp.LedgerEntity;
            if (entity.CloseTime > targetTime)
                return;
        }
        Assert.Fail($"Ledger close time did not exceed {targetTime} within {maxWaitSeconds} seconds");
    }

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

    #endregion
}
