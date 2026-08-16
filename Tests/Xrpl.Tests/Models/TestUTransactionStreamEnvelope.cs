using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Models;
using Xrpl.Models.Subscriptions;
using Xrpl.Models.Transactions;

// Regression tests for the API v1 / v2 payload shapes of the transaction streams, the same split
// TestUAccountTransactionsEnvelope pins for account_tx. rippled wraps the transaction in tx_json
// under API v2 and in transaction under API v1, and moves the hash with it: v2 reports it at the
// top level, v1 only inside the envelope. Payloads below are trimmed captures of real mainnet
// stream messages from s2.ripple.com.
namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUTransactionStreamEnvelope
    {
        private const string V1Hash = "96E02B092A9EDE4F7DA45E5DF8CE353AEEDAB1EFB46646146EAC582FFED19039";
        private const string V2Hash = "BC4F1500B56FE32D51AF23893CAA0FC972E2400E570151CA61FAC563A7C452E7";
        private const string Account = "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd";

        private const string StreamApiV1 = """
        {
          "type": "transaction",
          "status": "closed",
          "validated": true,
          "close_time_iso": "2026-08-16T18:39:42Z",
          "ledger_index": 106338962,
          "ledger_hash": "CD27564825B9F6177964FB3231D0CF0EA29C6E1E5F0D0D5F5F2D6E5C4B3A2918",
          "engine_result": "tesSUCCESS",
          "engine_result_code": 0,
          "engine_result_message": "The transaction was applied. Only final in a validated ledger.",
          "transaction": {
            "TransactionType": "OfferCreate",
            "Account": "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd",
            "Fee": "12",
            "Sequence": 84512339,
            "TakerGets": "9000000",
            "TakerPays": { "currency": "USD", "issuer": "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B", "value": "5.25" },
            "date": 808684782,
            "hash": "96E02B092A9EDE4F7DA45E5DF8CE353AEEDAB1EFB46646146EAC582FFED19039"
          },
          "meta": {
            "AffectedNodes": [],
            "TransactionIndex": 7,
            "TransactionResult": "tesSUCCESS"
          }
        }
        """;

        private const string StreamApiV2 = """
        {
          "type": "transaction",
          "status": "closed",
          "validated": true,
          "close_time_iso": "2026-08-16T18:39:50Z",
          "ledger_index": 106338963,
          "ledger_hash": "6F6656D47E7E667C75DD2B961A0C2E4D3B5A6978C1D2E3F4A5B6C7D8E9F00112",
          "hash": "BC4F1500B56FE32D51AF23893CAA0FC972E2400E570151CA61FAC563A7C452E7",
          "engine_result": "tesSUCCESS",
          "engine_result_code": 0,
          "engine_result_message": "The transaction was applied. Only final in a validated ledger.",
          "tx_json": {
            "TransactionType": "OfferCreate",
            "Account": "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd",
            "Fee": "12",
            "Sequence": 84512340,
            "TakerGets": "9000000",
            "TakerPays": { "currency": "USD", "issuer": "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B", "value": "5.25" },
            "date": 808684790
          },
          "meta": {
            "AffectedNodes": [],
            "TransactionIndex": 8,
            "TransactionResult": "tesSUCCESS"
          }
        }
        """;

        /// <summary>A stream message carrying neither envelope, which the type must survive.</summary>
        private const string StreamWithoutEnvelope = """
        {
          "type": "transaction",
          "status": "closed",
          "validated": true,
          "ledger_index": 106338964,
          "engine_result": "tesSUCCESS",
          "engine_result_code": 0
        }
        """;

        private static TransactionStream Parse(string message)
        {
            return JsonSerializer.Deserialize<TransactionStream>(message, XrplJsonOptions.Default);
        }

        [TestMethod]
        public void TestTransactionStreamApiV1Envelope()
        {
            TransactionStream stream = Parse(StreamApiV1);

            Assert.IsNotNull(stream);
            Assert.IsNotNull(stream.Transaction, "API v1 wraps the transaction in transaction instead of tx_json");
            Assert.AreEqual(TransactionType.OfferCreate, stream.Transaction.TransactionType);
            Assert.AreEqual(Account, stream.Transaction.Account);
            Assert.AreEqual(V1Hash, stream.Hash, "API v1 reports the hash inside the transaction envelope");
            Assert.AreEqual(106338962ul, stream.LedgerIndex);
            Assert.AreEqual("tesSUCCESS", stream.EngineResult);
        }

        [TestMethod]
        public void TestTransactionStreamApiV2Envelope()
        {
            TransactionStream stream = Parse(StreamApiV2);

            Assert.IsNotNull(stream);
            Assert.IsNotNull(stream.Transaction, "API v2 wraps the transaction in tx_json");
            Assert.AreEqual(TransactionType.OfferCreate, stream.Transaction.TransactionType);
            Assert.AreEqual(Account, stream.Transaction.Account);
            Assert.AreEqual(V2Hash, stream.Hash, "API v2 reports the hash at the top level");
            Assert.AreEqual(106338963ul, stream.LedgerIndex);
        }

        [TestMethod]
        public void TestTransactionStreamWithoutEnvelopeDoesNotThrow()
        {
            TransactionStream stream = Parse(StreamWithoutEnvelope);

            Assert.IsNotNull(stream);
            Assert.IsNull(stream.Transaction, "a message carrying neither envelope must read as no transaction");
            Assert.IsNull(stream.Hash);
        }

        /// <summary>
        /// The transaction is deserialized once, with the message that carries it. Reading it back
        /// is a field read and must cost nothing: the property used to re-parse the whole
        /// transaction on every single access, on the busiest path in the client.
        /// </summary>
        [TestMethod]
        public void TestRepeatedTransactionAccessCostsNothing()
        {
            const int Reads = 50;

            TransactionStream stream = Parse(StreamApiV1);
            TransactionResponse first = stream.Transaction;
            Assert.IsNotNull(first);

            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < Reads; i++)
            {
                Assert.AreSame(first, stream.Transaction, "every read must hand back the same instance");
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.IsTrue(
                allocated < 1024,
                $"{Reads} reads of Transaction allocated {allocated} bytes; re-parsing per access costs " +
                "kilobytes per read and is what this pins against");
        }
    }
}
