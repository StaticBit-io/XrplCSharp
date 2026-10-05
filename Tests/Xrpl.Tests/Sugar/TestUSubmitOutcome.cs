using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Methods;
using Xrpl.Models.Subscriptions;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;

using TimeoutException = Xrpl.Client.Exceptions.TimeoutException;

namespace XrplTests.Xrpl.Sugar;

/// <summary>
/// How reliable submission decides a transaction's outcome (issue #266): the provisional result,
/// <c>fail_hard</c>, the validated ledger, <c>searched_all</c> and the ledgers of the range.
/// </summary>
[TestClass]
public class TestUSubmitOutcome
{
    private const string Hash = "E08D6E9754025BA2534A78707605E0601F03ACE063687A0CA1BDDACFCD1698C7";
    private const uint MinLedger = 100;
    private const uint LastLedger = 120;

    /// <summary>Answers the lookups and the validated-ledger reads in order, the ledger reads by index, and records the calls.</summary>
    private sealed class Script
    {
        private readonly Queue<Func<TransactionSummary>> _lookups = new Queue<Func<TransactionSummary>>();
        private readonly Queue<Func<uint>> _validated = new Queue<Func<uint>>();
        private readonly Dictionary<uint, Func<bool?>> _ledgers = new Dictionary<uint, Func<bool?>>();

        public List<TxRequest> Requests { get; } = new List<TxRequest>();

        /// <summary>The calls in the order they were made: "lookup", "validated" and "ledger N".</summary>
        public List<string> Calls { get; } = new List<string>();

        public Script Found(bool validated, string result = "tesSUCCESS")
        {
            _lookups.Enqueue(() => new TransactionSummary { Validated = validated, Meta = new Meta { TransactionResult = result } });
            return this;
        }

        public Script NotFound(bool? searchedAll)
        {
            _lookups.Enqueue(() => throw new RippledException(
                "txnNotFound - Transaction not found.",
                new ErrorResponse { Error = XrplErrorCodes.TxnNotFound, SearchedAll = searchedAll }));
            return this;
        }

        public Script LookupThrows(Exception exception)
        {
            _lookups.Enqueue(() => throw exception);
            return this;
        }

        public Script Validated(uint index)
        {
            _validated.Enqueue(() => index);
            return this;
        }

        public Script ValidatedThrows(Exception exception)
        {
            _validated.Enqueue(() => throw exception);
            return this;
        }

        /// <summary>What reading ledger <paramref name="index"/> answers; an unset ledger is validated and does not hold the transaction.</summary>
        public Script Ledger(uint index, Func<bool?> holds)
        {
            _ledgers[index] = holds;
            return this;
        }

        public int LedgerReads(uint index) => Calls.FindAll(call => call == $"ledger {index}").Count;

        public Task<TransactionSummary> Run(string submissionResult, bool failHard = false, uint minLedger = MinLedger, uint lastLedger = LastLedger, CancellationToken cancellationToken = default) =>
            SubmitSugar.WaitForFinalTransactionOutcome(
                (request, _) =>
                {
                    Calls.Add("lookup");
                    Requests.Add(request);
                    return Task.FromResult(_lookups.Dequeue()());
                },
                _ =>
                {
                    Calls.Add("validated");
                    return Task.FromResult(_validated.Dequeue()());
                },
                (index, _) =>
                {
                    Calls.Add($"ledger {index}");
                    return Task.FromResult(_ledgers.TryGetValue(index, out Func<bool?> holds) ? holds() : false);
                },
                Hash,
                lastLedger,
                minLedger,
                submissionResult,
                failHard,
                TimeSpan.Zero,
                cancellationToken);
    }

    [TestMethod]
    public async Task ValidatedSuccessIsReturned()
    {
        Script script = new Script()
            .Found(validated: false).Validated(110)
            .Found(validated: true);

        TransactionSummary summary = await script.Run("tesSUCCESS");

        Assert.IsTrue(summary.Validated);
        Assert.HasCount(2, script.Requests);
    }

    [TestMethod]
    public async Task ValidatedTecFailsWithTheSummary()
    {
        Script script = new Script().Found(validated: true, result: "tecPATH_DRY");

        TransactionFailedException error = await Assert.ThrowsExactlyAsync<TransactionFailedException>(() => script.Run("tesSUCCESS"));

        Assert.AreEqual("tecPATH_DRY", error.EngineResult);
        Assert.IsNotNull(error.Result);
        Assert.IsTrue(error.ReachedLedger);
        Assert.AreEqual(Hash, error.Hash);
    }

    /// <summary>
    /// The case the issue opens with. The transaction is in the closed ledger <c>LastLedgerSequence</c>,
    /// which is not validated yet while the open ledger is already past it. Read against the open
    /// ledger it used to be reported expired; it is validated a moment later.
    /// </summary>
    [TestMethod]
    public async Task ClosedButNotValidatedAtTheLastLedgerIsNotExpired()
    {
        Script script = new Script()
            .Found(validated: false).Validated(LastLedger)
            .Found(validated: true);

        TransactionSummary summary = await script.Run("tesSUCCESS");

        Assert.IsTrue(summary.Validated);
    }

    [TestMethod]
    public async Task TemFailsAtOnceWithoutALookup()
    {
        Script script = new Script();

        TransactionFailedException error = await Assert.ThrowsExactlyAsync<TransactionFailedException>(() => script.Run("temBAD_AMOUNT"));

        Assert.AreEqual("temBAD_AMOUNT", error.EngineResult);
        Assert.IsFalse(error.ReachedLedger);
        Assert.IsEmpty(script.Calls);
    }

    /// <summary>A provisional <c>tec</c> was applied to the open ledger: it is reported once validated, with its metadata.</summary>
    [TestMethod]
    public async Task ProvisionalTecWaitsForTheValidatedResult()
    {
        Script script = new Script()
            .Found(validated: false, result: "tecUNFUNDED_PAYMENT").Validated(110)
            .Found(validated: true, result: "tecUNFUNDED_PAYMENT");

        TransactionFailedException error = await Assert.ThrowsExactlyAsync<TransactionFailedException>(() => script.Run("tecUNFUNDED_PAYMENT"));

        Assert.IsNotNull(error.Result, "The failure is reported from the validated ledger, not from the provisional answer.");
        Assert.HasCount(2, script.Requests);
    }

    /// <summary>
    /// Without <c>fail_hard</c> rippled holds a locally submitted transaction whatever its
    /// <c>ter</c>, <c>tef</c> or <c>tel</c> and retries it until its last ledger, so it is waited for.
    /// They used to be reported failed at once.
    /// </summary>
    [TestMethod]
    [DataRow("tefALREADY")]
    [DataRow("tefPAST_SEQ")]
    [DataRow("tefBAD_AUTH")]
    [DataRow("telINSUF_FEE_P")]
    [DataRow("terPRE_SEQ")]
    public async Task WithoutFailHardAProvisionalRefusalIsWaitedFor(string submissionResult)
    {
        Script script = new Script()
            .NotFound(searchedAll: false).Validated(105)
            .Found(validated: true);

        TransactionSummary summary = await script.Run(submissionResult);

        Assert.IsTrue(summary.Validated);
    }

    /// <summary>Under <c>fail_hard</c> the node neither applied, held nor relayed it: one lookup for an earlier copy decides.</summary>
    [TestMethod]
    [DataRow("tefBAD_AUTH")]
    [DataRow("telINSUF_FEE_P")]
    [DataRow("terNO_AMM")]
    [DataRow("tecUNFUNDED_PAYMENT")]
    public async Task UnderFailHardARefusalNotFoundFails(string submissionResult)
    {
        Script script = new Script().NotFound(searchedAll: false);

        TransactionFailedException error = await Assert.ThrowsExactlyAsync<TransactionFailedException>(() => script.Run(submissionResult, failHard: true));

        Assert.AreEqual(submissionResult, error.EngineResult);
        Assert.IsFalse(error.ReachedLedger, "Under fail_hard nothing was applied, a tec included: no fee was taken.");
        CollectionAssert.AreEqual(new[] { "lookup" }, script.Calls, "A refusal under fail_hard is decided by one lookup, without waiting.");
    }

    [TestMethod]
    public async Task UnderFailHardAnEarlierCopyIsFollowed()
    {
        Script script = new Script()
            .Found(validated: false)
            .Found(validated: true);

        TransactionSummary summary = await script.Run("tefBAD_AUTH", failHard: true);

        Assert.IsTrue(summary.Validated);
    }

    /// <summary>
    /// <c>tefPAST_SEQ</c> may report the earlier copy itself, which can still sit in a closed ledger
    /// the lookup does not reach yet: it is waited for even under <c>fail_hard</c>.
    /// </summary>
    [TestMethod]
    public async Task UnderFailHardASequenceAlreadyConsumedIsWaitedFor()
    {
        Script script = new Script()
            .NotFound(searchedAll: false).Validated(110)
            .Found(validated: true);

        TransactionSummary summary = await script.Run("tefPAST_SEQ", failHard: true);

        Assert.IsTrue(summary.Validated);
    }

    /// <summary>A blob submitted again after it was applied: found by its hash, reported as its success.</summary>
    [TestMethod]
    public async Task ABlobAlreadyValidatedSucceeds()
    {
        Script script = new Script().Found(validated: true);

        TransactionSummary summary = await script.Run("tefPAST_SEQ");

        Assert.IsTrue(summary.Validated);
    }

    [TestMethod]
    public async Task NotFoundWithSearchedAllExpires()
    {
        Script script = new Script()
            .NotFound(searchedAll: false).Validated(LastLedger)
            .NotFound(searchedAll: true);

        TransactionExpiredException error = await Assert.ThrowsExactlyAsync<TransactionExpiredException>(() => script.Run("tesSUCCESS"));

        Assert.IsTrue(error.SearchedAll);
        Assert.AreEqual(Hash, error.Hash);
        Assert.AreEqual(LastLedger, error.LastLedgerSequence);
        Assert.AreEqual(MinLedger, error.MinLedger);
        Assert.AreEqual("tesSUCCESS", error.PreliminaryResult);
        Assert.IsNotInstanceOfType<ValidationException>(error, "An expiry is not a refusal before submission.");
        Assert.IsFalse(script.Calls.Exists(call => call.StartsWith("ledger", StringComparison.Ordinal)), "searched_all needs no ledger read.");
    }

    /// <summary>
    /// rippled answers <c>searched_all</c> only for ranges where every ledger holds a transaction. On a
    /// quiet network an empty ledger keeps it false, and the ledgers of the range are read instead.
    /// </summary>
    [TestMethod]
    public async Task NotFoundOnAQuietNetworkExpiresFromTheLedgers()
    {
        Script script = new Script().NotFound(searchedAll: false).Validated(LastLedger + 1);

        TransactionExpiredException error = await Assert.ThrowsExactlyAsync<TransactionExpiredException>(() => script.Run("terPRE_SEQ"));

        Assert.IsFalse(error.SearchedAll);
        for (uint index = MinLedger; index <= LastLedger; index++)
            Assert.AreEqual(1, script.LedgerReads(index), $"Ledger {index} is read once.");
    }

    /// <summary>Nothing is concluded from the ledgers before the last one is validated.</summary>
    [TestMethod]
    public async Task TheLedgersAreReadOnlyOnceTheLastIsValidated()
    {
        Script script = new Script()
            .NotFound(searchedAll: false).Validated(LastLedger)
            .Found(validated: true);

        await script.Run("tesSUCCESS");

        Assert.IsFalse(script.Calls.Exists(call => call.StartsWith("ledger", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A held transaction (<c>terPRE_SEQ</c>) stays in the node's cache after its last ledger and is
    /// reported found but unvalidated. Seen on the CI stand: it used to end as unknown.
    /// </summary>
    [TestMethod]
    public async Task AHeldTransactionStillCachedPastTheLastLedgerExpires()
    {
        Script script = new Script()
            .Found(validated: false).Validated(LastLedger - 2)
            .Found(validated: false).Validated(LastLedger + 1);

        TransactionExpiredException error = await Assert.ThrowsExactlyAsync<TransactionExpiredException>(() => script.Run("terPRE_SEQ"));

        Assert.IsFalse(error.SearchedAll);
    }

    /// <summary>
    /// A validated ledger that lists the transaction while the lookup misses it - its transactions
    /// not yet written to the server's database - is not an expiry: the wait goes on.
    /// </summary>
    [TestMethod]
    public async Task ALedgerThatHoldsTheTransactionIsNotAnExpiry()
    {
        Script script = new Script()
            .Ledger(110, () => true)
            .NotFound(searchedAll: false).Validated(LastLedger + 1)
            .Found(validated: true);

        TransactionSummary summary = await script.Run("tesSUCCESS");

        Assert.IsTrue(summary.Validated);
    }

    [TestMethod]
    public async Task ALedgerTheServerLacksEndsUnknownAndTheProvenOnesAreNotReadAgain()
    {
        Script script = new Script()
            .Ledger(110, () => null)
            .NotFound(searchedAll: false).Validated(LastLedger + 1)
            .NotFound(searchedAll: false).Validated(LastLedger + SubmitSugar.LedgersPastExpiryBeforeUnknown + 1);

        TransactionOutcomeUnknownException error = await Assert.ThrowsExactlyAsync<TransactionOutcomeUnknownException>(() => script.Run("tesSUCCESS"));

        Assert.AreEqual(Hash, error.Hash);
        Assert.AreEqual(MinLedger, error.MinLedger);
        Assert.AreEqual(1, script.LedgerReads(MinLedger), "A ledger proven not to hold it is not read again.");
        Assert.AreEqual(2, script.LedgerReads(110), "The missing ledger is asked again on the next pass.");
        Assert.AreEqual(0, script.LedgerReads(111));
    }

    [TestMethod]
    public async Task ARangeTooWideToReadEndsUnknown()
    {
        uint lastLedger = MinLedger + SubmitSugar.MaxLedgersToRead;
        Script script = new Script()
            .NotFound(searchedAll: false).Validated(lastLedger + SubmitSugar.LedgersPastExpiryBeforeUnknown + 1);

        await Assert.ThrowsExactlyAsync<TransactionOutcomeUnknownException>(() => script.Run("tesSUCCESS", lastLedger: lastLedger));

        Assert.IsFalse(script.Calls.Exists(call => call.StartsWith("ledger", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task LookupsAskForTheRangeTheTransactionCouldBeIn()
    {
        Script script = new Script().Found(validated: true);

        await script.Run("tesSUCCESS");

        Assert.AreEqual(MinLedger, script.Requests[0].MinLedger);
        Assert.AreEqual(LastLedger, script.Requests[0].MaxLedger);
        Assert.AreEqual(Hash, script.Requests[0].Transaction);
    }

    /// <summary>rippled refuses a range wider than 1000 ledgers, so none is asked for.</summary>
    [TestMethod]
    public async Task ARangeWiderThanTheNodeSearchesIsNotAsked()
    {
        Script script = new Script().Found(validated: true);

        await script.Run("tesSUCCESS", minLedger: MinLedger, lastLedger: MinLedger + 1001);

        Assert.IsNull(script.Requests[0].MinLedger);
        Assert.IsNull(script.Requests[0].MaxLedger);
    }

    [TestMethod]
    public async Task AFailedLookupLeavesTheOutcomeUnknown()
    {
        TimeoutException timeout = new TimeoutException("Timeout for request");
        Script script = new Script().LookupThrows(timeout);

        TransactionOutcomeUnknownException error = await Assert.ThrowsExactlyAsync<TransactionOutcomeUnknownException>(() => script.Run("tesSUCCESS"));

        Assert.AreSame(timeout, error.InnerException);
        Assert.AreEqual(Hash, error.Hash);
    }

    /// <summary>
    /// A malformed ledger response while waiting used to surface as a <see cref="ValidationException"/>,
    /// the type that otherwise says nothing was submitted.
    /// </summary>
    [TestMethod]
    public async Task AFailedLedgerReadWhileWaitingIsNotAValidationFailure()
    {
        Script script = new Script()
            .NotFound(searchedAll: false)
            .ValidatedThrows(new ValidationException("Ledger response did not include a JSON ledger object."));

        TransactionOutcomeUnknownException error = await Assert.ThrowsExactlyAsync<TransactionOutcomeUnknownException>(() => script.Run("tesSUCCESS"));

        Assert.IsInstanceOfType<ValidationException>(error.InnerException);
    }

    [TestMethod]
    public async Task AFailedRangeReadLeavesTheOutcomeUnknown()
    {
        TimeoutException timeout = new TimeoutException("Timeout for request");
        Script script = new Script()
            .Ledger(105, () => throw timeout)
            .NotFound(searchedAll: false).Validated(LastLedger + 1);

        TransactionOutcomeUnknownException error = await Assert.ThrowsExactlyAsync<TransactionOutcomeUnknownException>(() => script.Run("tesSUCCESS"));

        Assert.AreSame(timeout, error.InnerException);
    }

    /// <summary>
    /// A cancelled wait stays an <see cref="OperationCanceledException"/> carrying the caller's token,
    /// and carries what resuming the wait takes.
    /// </summary>
    [TestMethod]
    public async Task ACancelledWaitCarriesWhatResumingTakes()
    {
        using CancellationTokenSource cancel = new CancellationTokenSource();
        cancel.Cancel();
        Script script = new Script();

        TransactionWaitCanceledException error = await Assert.ThrowsExactlyAsync<TransactionWaitCanceledException>(
            () => script.Run("terQUEUED", cancellationToken: cancel.Token));

        Assert.IsInstanceOfType<OperationCanceledException>(error);
        Assert.AreEqual(cancel.Token, error.CancellationToken);
        Assert.AreEqual(Hash, error.Hash);
        Assert.AreEqual(LastLedger, error.LastLedgerSequence);
        Assert.AreEqual(MinLedger, error.MinLedger);
        Assert.AreEqual("terQUEUED", error.PreliminaryResult);
    }

    /// <summary>
    /// <c>tefPAST_SEQ</c>, <c>tefNO_TICKET</c> and <c>tefALREADY</c> say this blob or its sequence was
    /// already used, possibly by an earlier submission applied before the range. Absence from the
    /// range then proves nothing about the transaction: it ends unknown, not expired.
    /// </summary>
    [TestMethod]
    [DataRow("tefPAST_SEQ")]
    [DataRow("tefNO_TICKET")]
    [DataRow("tefALREADY")]
    public async Task AResultPointingAtAnEarlierCopyIsNotProvenExpired(string submissionResult)
    {
        Script bySearchedAll = new Script().NotFound(searchedAll: true);
        Script byLedgers = new Script().NotFound(searchedAll: false).Validated(LastLedger + 1);

        await Assert.ThrowsExactlyAsync<TransactionOutcomeUnknownException>(() => bySearchedAll.Run(submissionResult));
        await Assert.ThrowsExactlyAsync<TransactionOutcomeUnknownException>(() => byLedgers.Run(submissionResult));
    }

    /// <summary>
    /// A server that keeps answering while its validated ledger stands still - a lost validator
    /// quorum - ends the wait as unknown. Read against the open ledger, which kept advancing, the old
    /// wait ended; read against the validated one it would not.
    /// </summary>
    [TestMethod]
    public async Task AValidatedLedgerThatStopsAdvancingEndsUnknown()
    {
        Script script = new Script();
        for (int poll = 0; poll <= SubmitSugar.ValidatedStallPollsBeforeUnknown; poll++)
            script.NotFound(searchedAll: false).Validated(110);

        TransactionOutcomeUnknownException error = await Assert.ThrowsExactlyAsync<TransactionOutcomeUnknownException>(() => script.Run("tesSUCCESS"));

        Assert.HasCount(SubmitSugar.ValidatedStallPollsBeforeUnknown + 1, script.Requests);
        StringAssert.Contains(error.Message, "stayed at 110");
    }

    [TestMethod]
    public async Task AnAdvancingValidatedLedgerIsNotAStall()
    {
        Script script = new Script();
        for (uint poll = 0; poll <= SubmitSugar.ValidatedStallPollsBeforeUnknown; poll++)
            script.NotFound(searchedAll: false).Validated(MinLedger + poll / 4);
        script.Found(validated: true);

        TransactionSummary summary = await script.Run("tesSUCCESS", lastLedger: MinLedger + 100);

        Assert.IsTrue(summary.Validated);
    }

    /// <summary>
    /// A transaction whose last ledger is already behind the first one searched - a blob submitted
    /// again late - is still looked up by its hash, without a range, and followed if found.
    /// </summary>
    [TestMethod]
    public async Task ALastLedgerBeforeTheFirstSearchedIsStillLookedUp()
    {
        Script followed = new Script().Found(validated: true);
        Script lost = new Script().NotFound(searchedAll: null).Validated(LastLedger + SubmitSugar.LedgersPastExpiryBeforeUnknown + 1);

        TransactionSummary summary = await followed.Run("tesSUCCESS", minLedger: LastLedger + 10);
        await Assert.ThrowsExactlyAsync<TransactionOutcomeUnknownException>(() => lost.Run("tesSUCCESS", minLedger: LastLedger + 10));

        Assert.IsTrue(summary.Validated);
        Assert.IsNull(followed.Requests[0].MinLedger);
        Assert.IsFalse(lost.Calls.Exists(call => call.StartsWith("ledger", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Only an error answer from the node proves the submit request left nothing on the network. The
    /// connection rejects a request it already sent with the same types it uses for one it never sent.
    /// </summary>
    [TestMethod]
    public void OnlyAnErrorAnswerProvesNothingWasSubmitted()
    {
        Assert.IsTrue(SubmitSugar.WasNotSubmitted(new RippledException("invalidTransaction", new ErrorResponse { Error = "invalidTransaction" })));

        // rippled answers these when processing the transaction threw, or after it was processed.
        Assert.IsFalse(SubmitSugar.WasNotSubmitted(new RippledException("internalSubmit", new ErrorResponse { Error = "internalSubmit" })));
        Assert.IsFalse(SubmitSugar.WasNotSubmitted(new RippledException("internalJson", new ErrorResponse { Error = "internalJson" })));

        Assert.IsFalse(SubmitSugar.WasNotSubmitted(new DisconnectedException("websocket was closed, code: 1001, reason: going away")));
        Assert.IsFalse(SubmitSugar.WasNotSubmitted(new NotConnectedException()));
        Assert.IsFalse(SubmitSugar.WasNotSubmitted(new ConnectHandlerFailedException("gave up", 3)));
        Assert.IsFalse(SubmitSugar.WasNotSubmitted(new TimeoutException("Timeout for request")));
        Assert.IsFalse(SubmitSugar.WasNotSubmitted(new ConnectionSupersededException("swept", ConnectionTransitionKind.Reconnect)));
    }

    private static Task<Submit> Submitting(Exception failure) => SubmitSugar.SubmitTracked(
        _ => throw failure, Hash, LastLedger, MinLedger, CancellationToken.None);

    [TestMethod]
    public async Task ASubmitRequestThatMayHaveReachedTheNodeIsUnknown()
    {
        TimeoutException timeout = new TimeoutException("Timeout for request");
        RippledException internalSubmit = new RippledException("internalSubmit", new ErrorResponse { Error = "internalSubmit" });

        TransactionOutcomeUnknownException timedOut = await Assert.ThrowsExactlyAsync<TransactionOutcomeUnknownException>(() => Submitting(timeout));
        await Assert.ThrowsExactlyAsync<TransactionOutcomeUnknownException>(() => Submitting(internalSubmit));

        Assert.AreSame(timeout, timedOut.InnerException);
        Assert.AreEqual(Hash, timedOut.Hash);
        Assert.AreEqual(MinLedger, timedOut.MinLedger);
        Assert.IsNull(timedOut.PreliminaryResult);
    }

    [TestMethod]
    public async Task ARefusedSubmitRequestPropagatesAsItIs()
    {
        RippledException refused = new RippledException("invalidTransaction", new ErrorResponse { Error = "invalidTransaction" });

        RippledException error = await Assert.ThrowsExactlyAsync<RippledException>(() => Submitting(refused));

        Assert.AreSame(refused, error);
    }

    [TestMethod]
    public async Task ACancelledSubmitRequestCarriesWhatResumingTakes()
    {
        using CancellationTokenSource cancel = new CancellationTokenSource();
        cancel.Cancel();

        TransactionWaitCanceledException error = await Assert.ThrowsExactlyAsync<TransactionWaitCanceledException>(
            () => SubmitSugar.SubmitTracked(token => throw new OperationCanceledException(token), Hash, LastLedger, MinLedger, cancel.Token));

        Assert.AreEqual(cancel.Token, error.CancellationToken);
        Assert.AreEqual(Hash, error.Hash);
        Assert.AreEqual(MinLedger, error.MinLedger);
    }

    [TestMethod]
    public void SearchedAllIsReadFromTheErrorFrame()
    {
        const string frame = "{\"error\":\"txnNotFound\",\"error_code\":29,\"error_message\":\"Transaction not found.\",\"searched_all\":true,\"status\":\"error\",\"type\":\"response\"}";

        ErrorResponse response = JsonSerializer.Deserialize<ErrorResponse>(frame);

        Assert.IsNotNull(response.SearchedAll);
        Assert.IsTrue(response.SearchedAll.Value);
        Assert.IsNull(JsonSerializer.Deserialize<ErrorResponse>("{\"error\":\"txnNotFound\",\"status\":\"error\"}").SearchedAll);
    }
}
