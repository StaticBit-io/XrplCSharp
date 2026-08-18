using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Xrpl.Client.Json;
using Xrpl.Models.Methods;
using Xrpl.Models.Subscriptions;

// The last remainder of the raw-response effort (plans/2026-08-17-raw-response-level*.md all name
// it): the frame reaches a query response's RawResult, but a stream event still went through
// EnqueueStreamMessage(Text()) - already a UTF-16 string with no frame behind it - so
// TransactionStream and friends had nowhere to hang a Raw. These tests cover the frame's trip
// through Connection's stream pipeline (OnMessage -> the byte[] channel -> AttachFrame) rather
// than just the model in isolation, since that pipeline is exactly what regressed twice before.
namespace Xrpl.Tests.ClientLib
{
    [TestClass]
    public class TestUStreamRawJson
    {
        public static SetupUnitClient runner;

        [TestInitialize]
        public async Task MyTestInitializeAsync()
        {
            runner = await new SetupUnitClient().SetupClient();
        }

        [TestCleanup]
        public async Task MyTestCleanupAsync()
        {
            await runner.client.Disconnect();
        }

        /// <summary>Carries a field no model on this stream knows, to prove Raw is not reconstructed.</summary>
        private const string LedgerClosedMessage = """
        {
          "type": "ledgerClosed",
          "fee_base": 10,
          "fee_ref": 10,
          "ledger_hash": "B3980C722D71873D6708723E71B7A28C826BC66C58712ADCEC61603415305CD1",
          "ledger_index": 66093872,
          "ledger_time": 683942720,
          "reserve_base": 20000000,
          "reserve_inc": 5000000,
          "txn_count": 70,
          "validated_ledgers": "65201743-66093872",
          "network_id": 9999
        }
        """;

        private const string TransactionStreamApiV2 = """
        {
          "type": "transaction",
          "status": "closed",
          "validated": true,
          "ledger_index": 106400001,
          "ledger_hash": "AA11BB22CC33DD44EE55FF66001122334455667788990011223344556677889",
          "hash": "FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210FEDCBA987654321",
          "engine_result": "tesSUCCESS",
          "engine_result_code": 0,
          "engine_result_message": "The transaction was applied. Only final in a validated ledger.",
          "tx_json": {
            "TransactionType": "Payment",
            "Account": "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd",
            "Destination": "rBTwLga3i2gz3doX6Gva3MgEV8ZCD8jjah",
            "Amount": "1000000",
            "Fee": "12",
            "Sequence": 1
          },
          "meta": {
            "AffectedNodes": [],
            "TransactionIndex": 3,
            "TransactionResult": "tesSUCCESS"
          }
        }
        """;

        private const string TransactionStreamApiV1 = """
        {
          "type": "transaction",
          "status": "closed",
          "validated": true,
          "ledger_index": 106400002,
          "ledger_hash": "BB22CC33DD44EE55FF660011223344556677889900112233445566778899AA",
          "engine_result": "tesSUCCESS",
          "engine_result_code": 0,
          "engine_result_message": "The transaction was applied. Only final in a validated ledger.",
          "transaction": {
            "TransactionType": "Payment",
            "Account": "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd",
            "Destination": "rBTwLga3i2gz3doX6Gva3MgEV8ZCD8jjah",
            "Amount": "500000",
            "Fee": "10",
            "Sequence": 2,
            "hash": "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCD"
          },
          "meta": {
            "AffectedNodes": [],
            "TransactionIndex": 4,
            "TransactionResult": "tesSUCCESS"
          }
        }
        """;

        /// <summary>Extracts a top-level member's source text through an independent path (JsonDocument), to check RawTransaction against.</summary>
        private static string TopLevelMemberRawText(string message, string name)
        {
            using JsonDocument document = JsonDocument.Parse(message);
            return document.RootElement.GetProperty(name).GetRawText();
        }

        /// <summary>
        /// The event exactly as the node sent it must survive the trip through <c>OnMessage</c>,
        /// the byte[] channel and <c>AttachFrame</c> byte for byte - including a field
        /// (<c>network_id</c>) that <see cref="LedgerStream"/> has no property for at all, which is
        /// exactly what distinguishes Raw from a re-serialization of the typed model.
        /// </summary>
        [TestMethod]
        public async Task TestLedgerClosedRawSurvivesTheStreamPipelineByteForByte()
        {
            TaskCompletionSource<LedgerStream> received = new TaskCompletionSource<LedgerStream>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            runner.client.connection.OnLedgerClosed += r =>
            {
                received.TrySetResult(r);
                return Task.CompletedTask;
            };

            await runner.client.connection.OnMessage(LedgerClosedMessage);

            Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(received.Task, completed, "OnLedgerClosed was not invoked within timeout");

            LedgerStream result = await received.Task;
            Assert.AreEqual(ResponseStreamType.ledgerClosed, result.Type);
            Assert.AreEqual(LedgerClosedMessage, result.Raw.ToString(),
                "Raw must be the exact bytes of the message, not a re-encoded copy");

            // The member the model has no place for: present in Raw, absent from a re-serialization
            // of the typed projection. This is the whole point of Raw existing.
            StringAssert.Contains(result.Raw.ToString(), "network_id");
            string reserialized = JsonSerializer.Serialize(result, XrplJsonOptions.Default);
            Assert.IsFalse(reserialized.Contains("network_id", StringComparison.Ordinal),
                "the typed model has no property for network_id and must not invent one on the way back out");
        }

        [TestMethod]
        public async Task TestTransactionStreamRawTransactionUsesTxJsonUnderApiV2()
        {
            TaskCompletionSource<TransactionStream> received = new TaskCompletionSource<TransactionStream>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            runner.client.connection.OnTransaction += r =>
            {
                received.TrySetResult(r);
                return Task.CompletedTask;
            };

            await runner.client.connection.OnMessage(TransactionStreamApiV2);

            Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(received.Task, completed, "OnTransaction was not invoked within timeout");

            TransactionStream result = await received.Task;
            Assert.IsFalse(result.RawTransaction.IsEmpty, "API v2 reports the transaction under tx_json");
            Assert.AreEqual(
                TopLevelMemberRawText(TransactionStreamApiV2, "tx_json"),
                result.RawTransaction.ToString());

            // Raw is the whole event; RawTransaction is only the transaction inside it - the two
            // must not collapse into the same thing, or a wallet asking for "just the tx" would get
            // engine_result/meta/etc. along with it.
            Assert.AreEqual(TransactionStreamApiV2, result.Raw.ToString());
            Assert.AreNotEqual(result.Raw.ToString(), result.RawTransaction.ToString());
        }

        [TestMethod]
        public async Task TestTransactionStreamRawTransactionUsesTransactionUnderApiV1()
        {
            TaskCompletionSource<TransactionStream> received = new TaskCompletionSource<TransactionStream>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            runner.client.connection.OnTransaction += r =>
            {
                received.TrySetResult(r);
                return Task.CompletedTask;
            };

            await runner.client.connection.OnMessage(TransactionStreamApiV1);

            Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(received.Task, completed, "OnTransaction was not invoked within timeout");

            TransactionStream result = await received.Task;
            Assert.IsFalse(result.RawTransaction.IsEmpty, "API v1 reports the transaction under transaction");
            Assert.AreEqual(
                TopLevelMemberRawText(TransactionStreamApiV1, "transaction"),
                result.RawTransaction.ToString());
        }

        /// <summary>
        /// A stream message carrying neither envelope - the same input
        /// <c>TestUTransactionStreamEnvelope.TestTransactionStreamWithoutEnvelopeDoesNotThrow</c>
        /// pins for the typed <see cref="TransactionStream.Transaction"/> property - must read as no
        /// raw transaction either, not throw and not alias some unrelated member.
        /// </summary>
        [TestMethod]
        public void TestRawTransactionIsEmptyWithoutEitherEnvelope()
        {
            const string message = """
            {"type":"transaction","status":"closed","validated":true,"engine_result":"tesSUCCESS"}
            """;

            byte[] frame = Encoding.UTF8.GetBytes(message);
            TransactionStream stream = JsonSerializer.Deserialize<TransactionStream>(frame, XrplJsonOptions.Default);
            stream.AttachFrame(frame);

            Assert.IsTrue(stream.RawTransaction.IsEmpty);
            Assert.IsFalse(stream.Raw.IsEmpty, "the event itself was still parsed off a real frame");
        }

        /// <summary>Mirrors <c>TestUAttachFrameRejectsANullFrame</c> for the stream side of the frame.</summary>
        [TestMethod]
        public void TestAttachFrameRejectsANullFrame()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => new LedgerStream().AttachFrame(null));
            Assert.ThrowsExactly<ArgumentNullException>(() => new TransactionStream().AttachFrame(null));
        }

        /// <summary>Raw/RawTransaction on an event nobody paired with a frame - built by hand, or deserialized outside the stream pipeline - must read as empty, not throw.</summary>
        [TestMethod]
        public void TestRawIsEmptyWithoutAnAttachedFrame()
        {
            TransactionStream stream = JsonSerializer.Deserialize<TransactionStream>(TransactionStreamApiV2, XrplJsonOptions.Default);

            Assert.IsTrue(stream.Raw.IsEmpty);
            Assert.IsTrue(stream.RawTransaction.IsEmpty);
        }

        /// <summary>
        /// The frame the byte[] channel carries is shared, not copied per event: pairing
        /// <see cref="TransactionStream"/> with it through <see cref="BaseStream.AttachFrame(byte[])"/>
        /// must add no more than a couple of reference fields per instance on top of what
        /// deserializing the event already costs - not a second copy of the frame (900+ B here),
        /// which is the shape the two prior retention regressions on this branch took. Mirrors
        /// <c>TestUEnvelopeRetainsNoMoreThanTheFrame</c>, applied to the stream side of the frame.
        /// </summary>
        [TestMethod]
        [DoNotParallelize]
        public void TestUTransactionStreamAttachFrameRetainsNoMoreThanTheFrame()
        {
            const int Count = 2000;
            byte[] frame = Encoding.UTF8.GetBytes(TransactionStreamApiV2);

            long MeasurePerInstance(bool attach)
            {
                // Warm up JIT/type-init for this exact path outside the measured window, so the
                // first of the two calls does not carry a one-time cost the second does not.
                TransactionStream warm = JsonSerializer.Deserialize<TransactionStream>(frame, XrplJsonOptions.Default);
                if (attach)
                {
                    warm.AttachFrame(frame);
                }

                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                long before = GC.GetTotalMemory(true);

                List<TransactionStream> retained = new List<TransactionStream>(Count);
                for (int i = 0; i < Count; i++)
                {
                    TransactionStream stream = JsonSerializer.Deserialize<TransactionStream>(frame, XrplJsonOptions.Default);
                    if (attach)
                    {
                        stream.AttachFrame(frame);
                    }

                    retained.Add(stream);
                }

                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true);
                long perInstance = (GC.GetTotalMemory(true) - before) / Count;

                GC.KeepAlive(retained);
                GC.KeepAlive(warm);
                return perInstance;
            }

            long withoutFrame = MeasurePerInstance(attach: false);
            long withFrame = MeasurePerInstance(attach: true);
            long marginal = withFrame - withoutFrame;

            Console.WriteLine(
                $"TransactionStream retains {withoutFrame} B/instance without AttachFrame, {withFrame} B/instance " +
                $"with it (marginal {marginal} B, frame is {frame.Length} B, shared across all {Count} instances)");

            // A frame accidentally copied per instance inside AttachFrame - the failure mode this
            // guards against - would add close to frame.Length (900+ B) per instance; sharing the
            // one array plus two int-pair slices should add near enough to nothing that a few
            // kilobytes of GC jitter over 2 000 samples does not need to be told apart from it.
            Assert.IsTrue(
                marginal < 300,
                $"AttachFrame added {marginal} B/instance beyond the unattached baseline of {withoutFrame} B; " +
                $"budget is 300 B, a full copy of the {frame.Length} B frame would show up as {frame.Length}+");
        }
    }
}
