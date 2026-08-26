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
        /// (<c>network_id</c>) that <see cref="LedgerStream"/> has no *declared* property for,
        /// which is exactly what distinguishes Raw (the literal bytes) from a re-serialization of
        /// the typed model (parsed values, round-tripped through <see cref="BaseStream.UnknownFields"/>).
        /// </summary>
        /// <remarks>
        /// Before <see cref="BaseStream.UnknownFields"/> existed, <c>network_id</c> - which
        /// <c>NetworkOpsImp::pubLedger</c> (NetworkOPs.cpp) sends unconditionally on every
        /// <c>ledgerClosed</c> push - silently vanished from the typed projection entirely: this
        /// test used to assert the re-serialization did NOT contain it, pinning the loss as
        /// expected behavior. Extension-data capture on the shared stream base fixed that, so the
        /// field now survives a full round trip the same way Raw always did - the assertion below
        /// was flipped to prove it stays, not that it disappears.
        /// </remarks>
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

            // The member the model has no *declared* property for: present in Raw (the literal
            // bytes) and, since UnknownFields captures it, also present in a re-serialization of
            // the typed projection - it must not be dropped on the way back out.
            StringAssert.Contains(result.Raw.ToString(), "network_id");
            Assert.IsTrue(result.UnknownFields.ContainsKey("network_id"),
                "network_id has no declared property on LedgerStream - it must land in UnknownFields instead of vanishing");
            Assert.AreEqual(9999u, result.UnknownFields["network_id"].GetUInt32());
            string reserialized = JsonSerializer.Serialize(result, XrplJsonOptions.Default);
            StringAssert.Contains(reserialized, "network_id",
                "UnknownFields round-trips on serialization - network_id must survive, not be silently dropped");
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
            // engine_result/meta/etc. along with it. Checked directly, not through
            // Assert.AreNotEqual(Raw.ToString(), RawTransaction.ToString()) - that held on any two
            // differently-sized strings regardless of what either one actually contained, so it
            // would still pass even if RawTransaction picked up the wrong slice entirely.
            Assert.AreEqual(TransactionStreamApiV2, result.Raw.ToString());
            StringAssert.Contains(result.Raw.ToString(), "engine_result",
                "sanity: the outer event carries fields RawTransaction must not");
            Assert.IsFalse(
                result.RawTransaction.ToString().Contains("engine_result", StringComparison.Ordinal),
                "RawTransaction must be only the tx_json object, not the event it sits inside");
        }

        private const string TransactionStreamDuplicateTxJson = """
        {
          "type": "transaction",
          "status": "closed",
          "validated": true,
          "tx_json": {
            "TransactionType": "Payment",
            "Account": "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd",
            "Destination": "rBTwLga3i2gz3doX6Gva3MgEV8ZCD8jjah",
            "Amount": "1000000",
            "Fee": "12",
            "Sequence": 1
          },
          "engine_result": "tesSUCCESS",
          "engine_result_code": 0,
          "tx_json": {
            "TransactionType": "Payment",
            "Account": "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd",
            "Destination": "rBTwLga3i2gz3doX6Gva3MgEV8ZCD8jjah",
            "Amount": "999999999",
            "Fee": "99",
            "Sequence": 42
          },
          "meta": {
            "AffectedNodes": [],
            "TransactionIndex": 3,
            "TransactionResult": "tesSUCCESS"
          }
        }
        """;

        /// <summary>
        /// rippled never sends a duplicate top-level <c>tx_json</c>, but the frame arrives over a
        /// network path this library does not control - an intermediate proxy or a compromised
        /// link is not prevented from sending one. <see cref="Utf8JsonReader"/>-based deserializer
        /// and <see cref="JsonSlice.FindTopLevelMember"/> must then agree on which occurrence wins,
        /// or a wallet showing <see cref="TransactionStream.RawTransaction"/> to a person and then
        /// signing <see cref="TransactionStream.Transaction"/> would show one transaction and sign
        /// a different one.
        /// </summary>
        [TestMethod]
        public async Task TestTransactionStreamRawTransactionUsesTheLastOccurrenceOfADuplicateTxJson()
        {
            TaskCompletionSource<TransactionStream> received = new TaskCompletionSource<TransactionStream>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            runner.client.connection.OnTransaction += r =>
            {
                received.TrySetResult(r);
                return Task.CompletedTask;
            };

            await runner.client.connection.OnMessage(TransactionStreamDuplicateTxJson);

            Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(received.Task, completed, "OnTransaction was not invoked within timeout");

            TransactionStream result = await received.Task;

            // Sanity check on the typed side first: System.Text.Json's own last-value-wins
            // behavior for a duplicate JSON member feeding one POCO property means Transaction
            // already reflects the second occurrence (Sequence 42), not the first (Sequence 1).
            Assert.AreEqual(42u, result.Transaction.Sequence, "sanity: the typed side must already reflect the second occurrence");

            string rawTransaction = result.RawTransaction.ToString();
            StringAssert.Contains(rawTransaction, "\"Sequence\": 42");
            // Discriminates on Fee, not Sequence: "Sequence": 1 is the last member of the first
            // envelope, so no comma follows it and a search for `"Sequence": 1,` matched nothing
            // either way - the assertion passed even when RawTransaction picked the first
            // occurrence. Fee differs between the two envelopes and sits mid-object in both.
            Assert.IsFalse(rawTransaction.Contains("\"Fee\": \"12\"", StringComparison.Ordinal),
                "RawTransaction picked the first occurrence instead of the last - it would show a wallet a different transaction than the one Transaction/signing would use");
        }

        private const string TransactionStreamBothEnvelopes = """
        {
          "type": "transaction",
          "status": "closed",
          "validated": true,
          "tx_json": {
            "TransactionType": "Payment",
            "Account": "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd",
            "Destination": "rBTwLga3i2gz3doX6Gva3MgEV8ZCD8jjah",
            "Amount": "1000000",
            "Fee": "12",
            "Sequence": 11
          },
          "engine_result": "tesSUCCESS",
          "transaction": {
            "TransactionType": "Payment",
            "Account": "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd",
            "Destination": "rBTwLga3i2gz3doX6Gva3MgEV8ZCD8jjah",
            "Amount": "222222222",
            "Fee": "22",
            "Sequence": 22
          },
          "meta": {
            "AffectedNodes": [],
            "TransactionIndex": 3,
            "TransactionResult": "tesSUCCESS"
          }
        }
        """;

        /// <summary>
        /// rippled sends one envelope or the other, never both - <c>NetworkOpsImp::transJson</c>
        /// moves <c>transaction</c> to <c>tx_json</c> under API v2 rather than adding it. But the
        /// frame reaches this library over the network through arbitrary infrastructure, and a
        /// wallet showing <see cref="TransactionStream.RawTransaction"/> while signing what
        /// <see cref="TransactionStream.Transaction"/> holds must never be shown one transaction
        /// and sign another. Both views therefore resolve the same envelope: the one later in the
        /// document, which is what the typed side's pair of `value ?? _transaction` setters leaves
        /// behind after running in document order.
        /// </summary>
        [TestMethod]
        public async Task TestTransactionStreamRawTransactionAgreesWithTypedWhenBothEnvelopesArePresent()
        {
            TaskCompletionSource<TransactionStream> received = new TaskCompletionSource<TransactionStream>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            runner.client.connection.OnTransaction += r =>
            {
                received.TrySetResult(r);
                return Task.CompletedTask;
            };

            await runner.client.connection.OnMessage(TransactionStreamBothEnvelopes);

            Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(received.Task, completed, "OnTransaction was not invoked within timeout");

            TransactionStream result = await received.Task;

            // Sanity check on the typed side first: "transaction" sits after "tx_json" here, so the
            // second setter to run wins and Transaction holds Sequence 22.
            Assert.AreEqual(22u, result.Transaction.Sequence, "sanity: the typed side must reflect the envelope that appears later");

            string rawTransaction = result.RawTransaction.ToString();
            StringAssert.Contains(rawTransaction, "\"Sequence\": 22");
            Assert.IsFalse(rawTransaction.Contains("\"Sequence\": 11", StringComparison.Ordinal),
                "RawTransaction resolved tx_json while the typed Transaction resolved the later \"transaction\" envelope - a wallet would display one transaction and sign another");
        }

        private const string TransactionStreamNullLegacyEnvelope = """
        {
          "type": "transaction",
          "status": "closed",
          "validated": true,
          "tx_json": {
            "TransactionType": "Payment",
            "Account": "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd",
            "Destination": "rBTwLga3i2gz3doX6Gva3MgEV8ZCD8jjah",
            "Amount": "1000000",
            "Fee": "12",
            "Sequence": 33
          },
          "engine_result": "tesSUCCESS",
          "transaction": null,
          "meta": {
            "AffectedNodes": [],
            "TransactionIndex": 3,
            "TransactionResult": "tesSUCCESS"
          }
        }
        """;

        /// <summary>
        /// An envelope explicitly set to JSON <c>null</c> is not an envelope. The typed setters
        /// discard it (<c>value ?? _transaction</c>), so the slice must too — otherwise the later
        /// position of a null <c>transaction</c> would win the tie-break and hand
        /// <see cref="TransactionStream.RawTransaction"/> four bytes of literal while
        /// <see cref="TransactionStream.Transaction"/> held the real payment.
        /// </summary>
        [TestMethod]
        public async Task TestTransactionStreamIgnoresAnEnvelopeExplicitlySetToNull()
        {
            TaskCompletionSource<TransactionStream> received = new TaskCompletionSource<TransactionStream>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            runner.client.connection.OnTransaction += r =>
            {
                received.TrySetResult(r);
                return Task.CompletedTask;
            };

            await runner.client.connection.OnMessage(TransactionStreamNullLegacyEnvelope);

            Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(received.Task, completed, "OnTransaction was not invoked within timeout");

            TransactionStream result = await received.Task;

            Assert.AreEqual(33u, result.Transaction.Sequence, "sanity: the typed side ignores the null envelope");

            string rawTransaction = result.RawTransaction.ToString();
            Assert.AreNotEqual("null", rawTransaction,
                "RawTransaction took the null envelope while Transaction took the real one - the two views must not disagree");
            StringAssert.Contains(rawTransaction, "\"Sequence\": 33");
        }

        /// <summary>
        /// An event that arrived without a <c>type</c> must not report one. The property is
        /// nullable precisely so absence round-trips as absence rather than as
        /// <c>ResponseStreamType.UNKNOWN</c>, the enum's zero value — the same fabrication this
        /// branch removes everywhere else. Was untested: reverting the property to non-nullable
        /// left the whole suite green.
        /// </summary>
        [TestMethod]
        public void TestStreamEventWithoutATypeDoesNotInventOne()
        {
            TransactionStream blank = new TransactionStream();
            Assert.IsNull(blank.Type, "no message assigned a type - the property must stay null, not read as UNKNOWN");

            string serialized = JsonSerializer.Serialize(blank, XrplJsonOptions.Default);
            Assert.IsFalse(serialized.Contains("\"type\"", StringComparison.Ordinal),
                "re-serializing an event that carried no type must not emit one: " + serialized);

            TransactionStream parsed = JsonSerializer.Deserialize<TransactionStream>(
                "{\"status\":\"closed\",\"validated\":true}", XrplJsonOptions.Default);
            Assert.IsNull(parsed.Type, "the message carried no type member - the property must stay null");
        }

        private const string TransactionStreamDuplicateEndingInNull = """
        {
          "type": "transaction",
          "status": "closed",
          "validated": true,
          "tx_json": {
            "TransactionType": "Payment",
            "Account": "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd",
            "Destination": "rBTwLga3i2gz3doX6Gva3MgEV8ZCD8jjah",
            "Amount": "1000000",
            "Fee": "12",
            "Sequence": 55
          },
          "engine_result": "tesSUCCESS",
          "tx_json": null,
          "meta": {
            "AffectedNodes": [],
            "TransactionIndex": 3,
            "TransactionResult": "tesSUCCESS"
          }
        }
        """;

        /// <summary>
        /// The last-occurrence rule skips null-valued occurrences, because the typed setters do:
        /// they run `value ?? _transaction`, so a duplicate ending in null leaves the real object
        /// in place. Resolving the slice to that null instead would empty
        /// <see cref="TransactionStream.RawTransaction"/> while
        /// <see cref="TransactionStream.Transaction"/> still held a payment — showing a wallet
        /// nothing while it signs something.
        /// </summary>
        [TestMethod]
        public async Task TestTransactionStreamSkipsANullOccurrenceWhenPickingTheLastOne()
        {
            TaskCompletionSource<TransactionStream> received = new TaskCompletionSource<TransactionStream>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            runner.client.connection.OnTransaction += r =>
            {
                received.TrySetResult(r);
                return Task.CompletedTask;
            };

            await runner.client.connection.OnMessage(TransactionStreamDuplicateEndingInNull);

            Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(received.Task, completed, "OnTransaction was not invoked within timeout");

            TransactionStream result = await received.Task;

            Assert.AreEqual(55u, result.Transaction.Sequence, "sanity: the typed side keeps the object, discarding the null duplicate");

            Assert.IsFalse(result.RawTransaction.IsEmpty,
                "RawTransaction resolved to the trailing null while Transaction kept the payment - the two views must not disagree");
            StringAssert.Contains(result.RawTransaction.ToString(), "\"Sequence\": 55");
        }

        // rippled NetworkOpsImp: account_history_tx_index is written on every event of such a
        // subscription (forwardTxIndex++ streaming forward, txHistoryIndex-- backfilling, hence
        // the negative value here); account_history_boundary marks the last transaction of a
        // ledger and account_history_tx_first the earliest transaction the account ever had, so
        // those two appear only on some events. All three are declared properties rather than
        // extension-data captures because capture costs ~464 B per member.
        private const string AccountHistoryTransaction = """
        {
          "type": "transaction",
          "status": "closed",
          "validated": true,
          "engine_result": "tesSUCCESS",
          "account_history_tx_index": -5,
          "account_history_boundary": true,
          "account_history_tx_first": true,
          "tx_json": {
            "TransactionType": "Payment",
            "Account": "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd",
            "Sequence": 7
          },
          "meta": {
            "AffectedNodes": [],
            "TransactionIndex": 3,
            "TransactionResult": "tesSUCCESS"
          }
        }
        """;

        /// <summary>
        /// The <c>account_history_*</c> members reach declared properties, not
        /// <see cref="BaseStream.UnknownFields"/> — a typo in any of the three
        /// <c>[JsonPropertyName]</c> attributes would silently route the field back into capture,
        /// which round-trips either way and so shows up in no fidelity check.
        /// </summary>
        [TestMethod]
        public void TestAccountHistoryMembersReachDeclaredProperties()
        {
            TransactionStream result = JsonSerializer.Deserialize<TransactionStream>(
                AccountHistoryTransaction, XrplJsonOptions.Default);

            Assert.AreEqual(-5L, result.AccountHistoryTxIndex, "backfill counts down through zero, so the index must survive as a signed value");
            Assert.AreEqual(true, result.AccountHistoryBoundary);
            Assert.AreEqual(true, result.AccountHistoryTxFirst);

            Assert.IsTrue(result.UnknownFields is null || result.UnknownFields.Count == 0,
                "all three are declared properties - none of them should have fallen into capture: "
                + (result.UnknownFields is null ? "" : string.Join(", ", result.UnknownFields.Keys)));
        }

        /// <summary>
        /// An event of an ordinary <c>transactions</c> subscription carries none of them, and must
        /// not report values the node never sent.
        /// </summary>
        [TestMethod]
        public void TestAccountHistoryMembersStayNullWhenNotAnAccountHistorySubscription()
        {
            TransactionStream result = JsonSerializer.Deserialize<TransactionStream>(
                TransactionStreamApiV2, XrplJsonOptions.Default);

            Assert.IsNull(result.AccountHistoryTxIndex);
            Assert.IsNull(result.AccountHistoryBoundary);
            Assert.IsNull(result.AccountHistoryTxFirst);
        }

        private const string TransactionStreamUppercaseTxJson = """
        {
          "type": "transaction",
          "status": "closed",
          "validated": true,
          "engine_result": "tesSUCCESS",
          "TX_JSON": {
            "TransactionType": "Payment",
            "Account": "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd",
            "Destination": "rBTwLga3i2gz3doX6Gva3MgEV8ZCD8jjah",
            "Amount": "1000000",
            "Fee": "12",
            "Sequence": 7
          },
          "meta": {
            "AffectedNodes": [],
            "TransactionIndex": 3,
            "TransactionResult": "tesSUCCESS"
          }
        }
        """;

        /// <summary>
        /// <see cref="XrplJsonOptions.Default"/> sets
        /// <see cref="System.Text.Json.JsonSerializerOptions.PropertyNameCaseInsensitive"/>, so the
        /// deserializer binds a differently-cased <c>"TX_JSON"</c> to
        /// <see cref="TransactionStream.Transaction"/> same as it would <c>"tx_json"</c>.
        /// <see cref="JsonSlice.FindTopLevelMember"/> has to match the same way, or
        /// <see cref="TransactionStream.RawTransaction"/> would come back empty on exactly the
        /// frame whose typed <see cref="TransactionStream.Transaction"/> is populated.
        /// </summary>
        [TestMethod]
        public async Task TestTransactionStreamRawTransactionMatchesTxJsonCaseInsensitively()
        {
            TaskCompletionSource<TransactionStream> received = new TaskCompletionSource<TransactionStream>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            runner.client.connection.OnTransaction += r =>
            {
                received.TrySetResult(r);
                return Task.CompletedTask;
            };

            await runner.client.connection.OnMessage(TransactionStreamUppercaseTxJson);

            Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(received.Task, completed, "OnTransaction was not invoked within timeout");

            TransactionStream result = await received.Task;

            // Sanity check first: confirms the fixture actually exercises case-insensitive binding
            // rather than accidentally matching some other way.
            Assert.IsNotNull(result.Transaction, "sanity: the typed side must bind \"TX_JSON\" case-insensitively");
            Assert.AreEqual(7u, result.Transaction.Sequence);

            Assert.IsFalse(result.RawTransaction.IsEmpty,
                "FindTopLevelMember must match \"TX_JSON\" case-insensitively, the same way the deserializer bound it to Transaction");
            StringAssert.Contains(result.RawTransaction.ToString(), "\"Sequence\": 7");
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
        /// All the tests above drive <c>OnMessage(string)</c>, where <c>Frame()</c> always
        /// synthesizes a fresh byte array from the string via <c>Encoding.UTF8.GetBytes</c> - the
        /// <c>utf8Message ??</c> branch of <c>Frame()</c>, which is what
        /// <c>ws.OnBinaryMessage</c> actually feeds in production and the entire reason the
        /// stream pipeline was moved onto bytes in the first place, is never exercised by any of
        /// them. This drives <see cref="Connection.IOnMessageFastPath(byte[])"/> directly - the
        /// same overload the socket callback calls - with a frame the test owns, and checks that
        /// the frame reaching <see cref="BaseStream.Raw"/>/<see cref="TransactionStream.RawTransaction"/>
        /// is that literal array, not a re-encoded copy of it.
        /// </summary>
        [TestMethod]
        public async Task TestBinaryFramePathRetainsTheSameArrayNotACopy()
        {
            TaskCompletionSource<TransactionStream> received = new TaskCompletionSource<TransactionStream>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            runner.client.connection.OnTransaction += r =>
            {
                received.TrySetResult(r);
                return Task.CompletedTask;
            };

            byte[] frame = Encoding.UTF8.GetBytes(TransactionStreamApiV2);

            await runner.client.connection.IOnMessageFastPath(frame);

            Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(received.Task, completed, "OnTransaction was not invoked within timeout");

            TransactionStream result = await received.Task;

            // _frame is internal on BaseStream specifically so a test can check this directly,
            // rather than inferring aliasing indirectly (e.g. by mutating the source array and
            // checking whether Raw sees the change) the way TestURawJsonToJsonElementOwnsItsData
            // proves the opposite (copying) contract for JsonElement.
            Assert.AreSame(frame, result._frame,
                "the byte[] entry point must retain the very same array the socket handed over, not a copy of it");
            Assert.AreEqual(TransactionStreamApiV2, result.Raw.ToString());
            Assert.AreEqual(
                TopLevelMemberRawText(TransactionStreamApiV2, "tx_json"),
                result.RawTransaction.ToString());
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

            // Warm up JIT and type-init for both calls outside any measured window.
            TransactionStream warm = JsonSerializer.Deserialize<TransactionStream>(frame, XrplJsonOptions.Default);
            warm.AttachFrame(frame);

            // The instances exist before the window opens, and the window contains nothing but the
            // AttachFrame calls. That is the whole of the fix to how this used to be measured: it
            // took two whole-heap readings in two separate windows - one pass that attached, one
            // that did not - and subtracted them. Anything that moved the heap between those two
            // passes landed in the answer, and on Linux something did, by more than a megabyte:
            // the unattached pass read ~600 B/instance lower than the attached one and the
            // difference was reported as AttachFrame's cost. It failed CI twice, on two unrelated
            // pull requests, and took a merge down with it; locally it read 0 B every time.
            //
            // Attaching to instances that already exist removes the comparison instead of widening
            // the bound: what the window measures is exactly what AttachFrame allocates, which is
            // the thing the test is named after.
            List<TransactionStream> retained = new List<TransactionStream>(Count);
            for (int i = 0; i < Count; i++)
            {
                retained.Add(JsonSerializer.Deserialize<TransactionStream>(frame, XrplJsonOptions.Default));
            }

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            long before = GC.GetTotalMemory(true);

            foreach (TransactionStream stream in retained)
            {
                stream.AttachFrame(frame);
            }

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            long marginal = (GC.GetTotalMemory(true) - before) / Count;

            GC.KeepAlive(retained);
            GC.KeepAlive(warm);

            Console.WriteLine(
                $"AttachFrame adds {marginal} B/instance across {Count} instances " +
                $"(frame is {frame.Length} B, shared by all of them)");

            // A frame copied per instance - the failure mode this guards against, and the shape of
            // the two retention regressions on this branch - would add close to frame.Length here,
            // which is 744 B and 1.5 MB over the sample. Storing one reference and two int pairs
            // adds nothing measurable, so the bound has two and a half times the headroom it needs
            // over any jitter and still misses a real copy by a wide margin.
            Assert.IsTrue(
                marginal < 300,
                $"AttachFrame added {marginal} B/instance; budget is 300 B, and a full copy of the " +
                $"{frame.Length} B frame would show up as {frame.Length}+");
        }
    }
}
