using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Methods;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// Reliable submission against a node (issue #266): the expiry proven by <c>searched_all</c>, a
/// blob submitted again after it was applied, and tracking a transaction already sent.
/// </summary>
/// <remarks>
/// The decisions themselves are unit tested; what only a node can show is that it answers the
/// ranged <c>tx</c> lookup with <c>searched_all</c>, and which provisional codes it gives.
/// </remarks>
[TestClass]
public class TestIReliableSubmission
{
    private static IXrplClient client;
    private static XrplWallet wallet;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync();
        wallet = await Utils.GenerateFundedWallet(client);
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    private static async Task<Dictionary<string, object>> PaymentAsync()
    {
        Dictionary<string, object> tx = new Dictionary<string, object>
        {
            { "TransactionType", "Payment" },
            { "Account", wallet.ClassicAddress },
            { "Destination", XrplWallet.Generate().ClassicAddress },
            { "Amount", "20000000" },
        };
        return await client.Autofill(tx);
    }

    /// <summary>
    /// A sequence one ahead of the account's is held (<c>terPRE_SEQ</c>) and never applies. Once its
    /// <c>LastLedgerSequence</c> is validated, the node proves it is in none of the ledgers it could
    /// have reached, and the wait ends in an expiry rather than in an unknown outcome.
    /// </summary>
    [TestMethod]
    public async Task ATransactionThatCannotApplyExpiresConclusively()
    {
        Dictionary<string, object> tx = await PaymentAsync();
        uint validated = await client.GetLedgerIndex();
        tx["Sequence"] = Convert.ToUInt32(tx["Sequence"]) + 1;
        tx["LastLedgerSequence"] = validated + 3;
        SignatureResult signed = wallet.Sign(tx);

        TransactionExpiredException error = await Assert.ThrowsExactlyAsync<TransactionExpiredException>(
            () => client.SubmitRequestAndWait(signed.TxBlob, false));

        Assert.AreEqual(signed.Hash, error.Hash);
        Assert.AreEqual(validated + 3, error.LastLedgerSequence);
        Assert.AreEqual("terPRE_SEQ", error.PreliminaryResult);
        Assert.IsLessThanOrEqualTo(error.LastLedgerSequence, error.MinLedger);
    }

    /// <summary>
    /// The same signed blob submitted again after it was validated: the node answers with a
    /// <c>tef</c>, the transaction is found, and the caller learns it succeeded.
    /// </summary>
    [TestMethod]
    public async Task ABlobSubmittedAgainAfterItAppliedReportsItsSuccess()
    {
        Dictionary<string, object> tx = await PaymentAsync();
        SignatureResult signed = wallet.Sign(tx);

        TransactionSummary first = await client.SubmitRequestAndWait(signed.TxBlob, false);
        TransactionSummary again = await client.SubmitRequestAndWait(signed.TxBlob, false);

        Assert.AreEqual("tesSUCCESS", first.Meta?.TransactionResult);
        Assert.IsTrue(again.Validated);
        Assert.AreEqual("tesSUCCESS", again.Meta?.TransactionResult);
    }

    /// <summary>
    /// Submitted again ledgers later, the search range starts after the ledger the blob was applied
    /// in. rippled looks a transaction up by its hash whatever the range, which only bounds the
    /// proof of absence, so it is still found.
    /// </summary>
    [TestMethod]
    public async Task ABlobSubmittedAgainLedgersLaterIsFoundOutsideTheRange()
    {
        Dictionary<string, object> tx = await PaymentAsync();
        SignatureResult signed = wallet.Sign(tx);
        TransactionSummary first = await client.SubmitRequestAndWait(signed.TxBlob, false);
        uint appliedIn = Convert.ToUInt32(first.LedgerIndex);

        DateTime deadline = DateTime.UtcNow.AddMinutes(2);
        while (await client.GetLedgerIndex() < appliedIn + 3)
        {
            Assert.IsLessThan(deadline, DateTime.UtcNow, "The validated ledger did not advance.");
            await IntegrationTestConfig.LedgerAcceptAsync(client);
            await Task.Delay(500);
        }

        TransactionSummary again = await client.SubmitRequestAndWait(signed.TxBlob, false);

        Assert.AreEqual(appliedIn, Convert.ToUInt32(again.LedgerIndex));
        Assert.AreEqual("tesSUCCESS", again.Meta?.TransactionResult);
    }

    [TestMethod]
    public async Task ATransactionAlreadySentIsTrackedToItsOutcome()
    {
        Dictionary<string, object> tx = await PaymentAsync();
        uint minLedger = await client.GetLedgerIndex();
        SignatureResult signed = wallet.Sign(tx);
        await client.SubmitRequest(signed.TxBlob, false);

        TransactionSummary summary = await client.WaitForTransactionOutcome(
            signed.Hash,
            Convert.ToUInt32(tx["LastLedgerSequence"]),
            minLedger);

        Assert.IsTrue(summary.Validated);
        Assert.AreEqual("tesSUCCESS", summary.Meta?.TransactionResult);
    }
}
