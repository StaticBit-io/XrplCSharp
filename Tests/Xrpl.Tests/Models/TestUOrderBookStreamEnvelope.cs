using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Models.Subscriptions;
using Xrpl.Models.Transactions;

// The order book stream carries the transaction the way the transaction stream does: in tx_json
// under API v2 and in transaction under API v1 (NetworkOPs.cpp transJson). Payloads are trimmed
// from the same mainnet captures TestUTransactionStreamEnvelope uses.
namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUOrderBookStreamEnvelope
    {
        private const string Account = "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd";

        private const string Transaction = """
            {
              "TransactionType": "OfferCreate",
              "Account": "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd",
              "Fee": "12",
              "Sequence": 84512340,
              "TakerGets": "9000000",
              "TakerPays": { "currency": "USD", "issuer": "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B", "value": "5.25" }
            }
            """;

        private static string Stream(string envelope) => $$"""
            {
              "type": "transaction",
              "status": "closed",
              "validated": true,
              "engine_result": "tesSUCCESS",
              "engine_result_code": 0,
              "engine_result_message": "The transaction was applied. Only final in a validated ledger.",
              "{{envelope}}": {{Transaction}},
              "meta": { "AffectedNodes": [], "TransactionIndex": 7, "TransactionResult": "tesSUCCESS" }
            }
            """;

        [TestMethod]
        public void Transaction_ApiV2_IsReadFromTxJson()
        {
            OrderBookStream stream = JsonSerializer.Deserialize<OrderBookStream>(Stream("tx_json"), XrplJsonOptions.Default);

            ITransactionResponse transaction = stream.Transaction;

            Assert.IsNotNull(transaction);
            Assert.AreEqual(Account, transaction.Account);
            Assert.AreEqual(84512340u, transaction.Sequence);
            Assert.IsNull(stream.TransactionJson);
        }

        [TestMethod]
        public void Transaction_ApiV1_IsReadFromTransaction()
        {
            OrderBookStream stream = JsonSerializer.Deserialize<OrderBookStream>(Stream("transaction"), XrplJsonOptions.Default);

            ITransactionResponse transaction = stream.Transaction;

            Assert.IsNotNull(transaction);
            Assert.AreEqual(Account, transaction.Account);
            Assert.IsNull(stream.TxJson);
        }

        [TestMethod]
        public void Transaction_NeitherEnvelope_IsNull()
        {
            OrderBookStream stream = JsonSerializer.Deserialize<OrderBookStream>("""{ "type": "transaction", "validated": true }""", XrplJsonOptions.Default);

            Assert.IsNull(stream.Transaction);
        }
    }
}
