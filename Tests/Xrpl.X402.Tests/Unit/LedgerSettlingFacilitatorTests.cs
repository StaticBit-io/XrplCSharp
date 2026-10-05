using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;
using Xrpl.X402.AspNetCore;
using Xrpl.X402.Wire;

namespace Xrpl.X402.Tests.Unit;

/// <summary>
/// How the ledger-settling facilitator answers each outcome of a settlement - above all one whose
/// outcome the client could not tell, where the payer's funds may already have moved.
/// </summary>
[TestClass]
public class LedgerSettlingFacilitatorTests
{
    private const string Hash = "E08D6E9754025BA2534A78707605E0601F03ACE063687A0CA1BDDACFCD1698C7";

    private static readonly XrplWallet Payer = XrplWallet.Generate();
    private static readonly string PayTo = XrplWallet.Generate().ClassicAddress;

    private static PaymentSignatureEnvelope Envelope()
    {
        Dictionary<string, object> tx = new()
        {
            { "TransactionType", "Payment" },
            { "Account", Payer.ClassicAddress },
            { "Destination", PayTo },
            { "Amount", "1000000" },
            { "Fee", "12" },
            { "Sequence", 1u },
            { "LastLedgerSequence", 120u },
        };

        return new PaymentSignatureEnvelope
        {
            Accepted = new PaymentRequirement { Scheme = "exact", Network = "xrpl:1", Asset = "XRP", PayTo = PayTo, Amount = "1000000" },
            Payload = new SignedPayload { SignedTxBlob = Payer.Sign(tx).TxBlob }
        };
    }

    private static TransactionSummary Validated(string result) =>
        new() { Validated = true, Meta = new Meta { TransactionResult = result } };

    private static TransactionOutcomeUnknownException Unknown() =>
        new("outcome unknown", Hash, 120, 100, "tesSUCCESS");

    private static LedgerSettlingFacilitator Facilitator(
        Func<TransactionSummary> submit,
        Func<TransactionSummary>? resolve = null,
        List<TransactionOutcomeUnknownException>? resolved = null) =>
        new(
            (_, _) => Task.FromResult(submit()),
            (unknown, _) =>
            {
                resolved?.Add(unknown);
                return Task.FromResult((resolve ?? throw new AssertFailedException("resolve was not expected"))());
            });

    [TestMethod]
    public async Task AValidatedSuccessSettles()
    {
        PaymentResponseEnvelope response = await Facilitator(() => Validated("tesSUCCESS")).VerifyAndSettleAsync(Envelope());

        Assert.IsTrue(response.Success);
        Assert.AreEqual(Payer.ClassicAddress, response.Payer);
    }

    /// <summary>
    /// The client lost track of the submission, and waiting once more finds the payment validated:
    /// the resource is served, not refused while the payer's funds moved.
    /// </summary>
    [TestMethod]
    public async Task AnUnknownOutcomeThatTurnsOutSettledIsServed()
    {
        TransactionOutcomeUnknownException unknown = Unknown();
        List<TransactionOutcomeUnknownException> resolved = new();

        PaymentResponseEnvelope response = await Facilitator(
            () => throw unknown,
            () => Validated("tesSUCCESS"),
            resolved).VerifyAndSettleAsync(Envelope());

        Assert.IsTrue(response.Success);
        Assert.HasCount(1, resolved);
        Assert.AreSame(unknown, resolved[0], "The wait resumes with what the unknown outcome carried.");
    }

    [TestMethod]
    public async Task AnOutcomeThatStaysUnknownIsRefusedWithItsHash()
    {
        PaymentResponseEnvelope response = await Facilitator(
            () => throw Unknown(),
            () => throw Unknown()).VerifyAndSettleAsync(Envelope());

        Assert.IsFalse(response.Success);
        Assert.AreEqual("settlement_unknown", response.ErrorReason);
        Assert.AreEqual(Hash, response.Transaction, "The hash is what reconciling the payment takes.");
        Assert.AreEqual(Payer.ClassicAddress, response.Payer);
    }

    [TestMethod]
    public async Task AnUnknownOutcomeThatTurnsOutExpiredFails()
    {
        PaymentResponseEnvelope response = await Facilitator(
            () => throw Unknown(),
            () => throw new TransactionExpiredException("expired", Hash, 120, 100, "tesSUCCESS", searchedAll: true)).VerifyAndSettleAsync(Envelope());

        Assert.IsFalse(response.Success);
        Assert.AreEqual("settlement_failed", response.ErrorReason);
    }

    [TestMethod]
    public async Task AFinalFailureFails()
    {
        PaymentResponseEnvelope failed = await Facilitator(
            () => throw new TransactionFailedException("tec", "tecUNFUNDED_PAYMENT", Hash, Validated("tecUNFUNDED_PAYMENT"))).VerifyAndSettleAsync(Envelope());
        PaymentResponseEnvelope expired = await Facilitator(
            () => throw new TransactionExpiredException("expired", Hash, 120, 100, "terPRE_SEQ", searchedAll: false)).VerifyAndSettleAsync(Envelope());

        Assert.AreEqual("settlement_failed", failed.ErrorReason);
        Assert.AreEqual("settlement_failed", expired.ErrorReason);
    }

    [TestMethod]
    public async Task AnyOtherErrorIsASettlementError()
    {
        PaymentResponseEnvelope response = await Facilitator(() => throw new InvalidOperationException("boom")).VerifyAndSettleAsync(Envelope());

        Assert.IsFalse(response.Success);
        Assert.AreEqual("settlement_error", response.ErrorReason);
    }

    [TestMethod]
    public async Task TheCallersCancellationPropagates()
    {
        using CancellationTokenSource cancel = new();
        cancel.Cancel();
        LedgerSettlingFacilitator facilitator = new(
            (_, token) => throw new TransactionWaitCanceledException("cancelled", Hash, 120, 100, null!, new OperationCanceledException(token), token),
            (_, _) => throw new AssertFailedException("resolve was not expected"));

        await Assert.ThrowsAsync<OperationCanceledException>(() => facilitator.VerifyAndSettleAsync(Envelope(), cancel.Token));
    }
}
