using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

// Regression tests for the API v1 / v2 payload shapes of account_tx.
// rippled renames Payment.Amount to DeliverMax in API v2 and wraps the transaction
// in tx_json (v2) instead of tx (v1). Payloads below are trimmed captures of real
// testnet responses for EB5EAD6DD91CE3731B2A50EBDA6D9D4495A0C8AA755051C8A76C39706BBBB83D.
namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUAccountTransactionsEnvelope
    {
        private const string ExpectedHash = "EB5EAD6DD91CE3731B2A50EBDA6D9D4495A0C8AA755051C8A76C39706BBBB83D";
        private const string ExpectedDestination = "rNrjh1KGZk2jBR3wPfAQnoidtFFYQKbQn2";

        private const string PaymentApiV2 = """
        {
          "TransactionType": "Payment",
          "Account": "rtc14CSPYUctZYFnhAziL9tCzZNCMLQGf",
          "Destination": "rNrjh1KGZk2jBR3wPfAQnoidtFFYQKbQn2",
          "DeliverMax": "1000000",
          "Fee": "12"
        }
        """;

        private const string PaymentApiV1 = """
        {
          "TransactionType": "Payment",
          "Account": "rtc14CSPYUctZYFnhAziL9tCzZNCMLQGf",
          "Destination": "rNrjh1KGZk2jBR3wPfAQnoidtFFYQKbQn2",
          "Amount": "1000000",
          "DeliverMax": "1000000",
          "Fee": "12"
        }
        """;

        private const string PaymentIssuedCurrencyApiV2 = """
        {
          "TransactionType": "Payment",
          "Account": "rtc14CSPYUctZYFnhAziL9tCzZNCMLQGf",
          "Destination": "rNrjh1KGZk2jBR3wPfAQnoidtFFYQKbQn2",
          "DeliverMax": { "currency": "USD", "issuer": "rNrjh1KGZk2jBR3wPfAQnoidtFFYQKbQn2", "value": "100.5" },
          "Fee": "12"
        }
        """;

        private const string AccountTxApiV2 = """
        {
          "account": "rtc14CSPYUctZYFnhAziL9tCzZNCMLQGf",
          "ledger_index_max": 19413662,
          "ledger_index_min": 12929081,
          "limit": 10,
          "validated": true,
          "transactions": [
            {
              "close_time_iso": "2026-07-20T14:11:42Z",
              "hash": "EB5EAD6DD91CE3731B2A50EBDA6D9D4495A0C8AA755051C8A76C39706BBBB83D",
              "ledger_hash": "573C13C5F626106BFFA684C46BB34DAAF44FD802670A89894C545B1DE38E86BB",
              "ledger_index": 19224694,
              "meta": {
                "AffectedNodes": [],
                "TransactionIndex": 3,
                "TransactionResult": "tesSUCCESS",
                "delivered_amount": "1000000"
              },
              "tx_json": {
                "Account": "rtc14CSPYUctZYFnhAziL9tCzZNCMLQGf",
                "DeliverMax": "1000000",
                "Destination": "rNrjh1KGZk2jBR3wPfAQnoidtFFYQKbQn2",
                "Fee": "12",
                "Sequence": 19224691,
                "TransactionType": "Payment",
                "date": 837871902,
                "ledger_index": 19224694
              },
              "validated": true
            }
          ]
        }
        """;

        private const string AccountTxApiV1 = """
        {
          "account": "rtc14CSPYUctZYFnhAziL9tCzZNCMLQGf",
          "ledger_index_max": 19413664,
          "ledger_index_min": 12929081,
          "limit": 10,
          "validated": true,
          "transactions": [
            {
              "meta": {
                "AffectedNodes": [],
                "TransactionIndex": 3,
                "TransactionResult": "tesSUCCESS",
                "delivered_amount": "1000000"
              },
              "tx": {
                "Account": "rtc14CSPYUctZYFnhAziL9tCzZNCMLQGf",
                "Amount": "1000000",
                "DeliverMax": "1000000",
                "Destination": "rNrjh1KGZk2jBR3wPfAQnoidtFFYQKbQn2",
                "Fee": "12",
                "Sequence": 19224691,
                "TransactionType": "Payment",
                "date": 837871902,
                "hash": "EB5EAD6DD91CE3731B2A50EBDA6D9D4495A0C8AA755051C8A76C39706BBBB83D",
                "inLedger": 19224694,
                "ledger_index": 19224694
              },
              "validated": true
            }
          ]
        }
        """;

        [TestMethod]
        public void TestPaymentResponseReadsDeliverMaxIntoAmount()
        {
            PaymentResponse payment = JsonSerializer.Deserialize<PaymentResponse>(PaymentApiV2, XrplJsonOptions.Default) as PaymentResponse;

            Assert.IsNotNull(payment);
            Assert.IsNotNull(payment.Amount, "API v2 omits Amount and sends DeliverMax instead");
            Assert.AreEqual("1000000", payment.Amount.Value);
            Assert.AreEqual(ExpectedDestination, payment.Destination);
        }

        [TestMethod]
        public void TestPaymentResponseReadsIssuedCurrencyDeliverMax()
        {
            PaymentResponse payment = JsonSerializer.Deserialize<PaymentResponse>(PaymentIssuedCurrencyApiV2, XrplJsonOptions.Default) as PaymentResponse;

            Assert.IsNotNull(payment);
            Assert.IsNotNull(payment.Amount);
            Assert.AreEqual("USD", payment.Amount.CurrencyCode);
            Assert.AreEqual(ExpectedDestination, payment.Amount.Issuer);
            Assert.AreEqual("100.5", payment.Amount.Value);
        }

        [TestMethod]
        public void TestPaymentResponseKeepsAmountWhenBothFieldsPresent()
        {
            PaymentResponse payment = JsonSerializer.Deserialize<PaymentResponse>(PaymentApiV1, XrplJsonOptions.Default) as PaymentResponse;

            Assert.IsNotNull(payment);
            Assert.IsNotNull(payment.Amount);
            Assert.AreEqual("1000000", payment.Amount.Value);
        }

        [TestMethod]
        public void TestPaymentRequestReadsDeliverMaxIntoAmount()
        {
            Payment payment = JsonSerializer.Deserialize<Payment>(PaymentApiV2, XrplJsonOptions.Default);

            Assert.IsNotNull(payment);
            Assert.IsNotNull(payment.Amount);
            Assert.AreEqual("1000000", payment.Amount.Value);
        }

        [TestMethod]
        public void TestPaymentDoesNotSerializeDeliverMax()
        {
            Payment payment = JsonSerializer.Deserialize<Payment>(PaymentApiV1, XrplJsonOptions.Default);

            string json = JsonSerializer.Serialize(payment, payment.GetType(), XrplJsonOptions.Default);

            // DeliverMax is an API-level alias, not a ledger field: it must never reach the binary codec.
            StringAssert.Contains(json, "\"Amount\":\"1000000\"");
            Assert.IsFalse(json.Contains("DeliverMax"), $"DeliverMax leaked into serialized output: {json}");
        }

        [TestMethod]
        public void TestAccountTransactionsApiV2Envelope()
        {
            AccountTransactions response = JsonSerializer.Deserialize<AccountTransactions>(AccountTxApiV2, XrplJsonOptions.Default);

            Assert.IsNotNull(response);
            Assert.AreEqual(1, response.Transactions.Count);

            TransactionSummary summary = response.Transactions[0];
            Assert.IsNotNull(summary.Transaction, "API v2 wraps the transaction in tx_json");
            Assert.AreEqual(ExpectedHash, summary.Hash);
            Assert.AreEqual(19224694ul, summary.LedgerIndex);

            PaymentResponse payment = summary.Transaction as PaymentResponse;
            Assert.IsNotNull(payment);
            Assert.IsNotNull(payment.Amount, "DeliverMax must populate Amount");
            Assert.AreEqual("1000000", payment.Amount.Value);
            Assert.AreEqual("1000000", summary.Meta.ActuallyDeliveredAmount.Value);
        }

        [TestMethod]
        public void TestAccountTransactionsApiV1Envelope()
        {
            AccountTransactions response = JsonSerializer.Deserialize<AccountTransactions>(AccountTxApiV1, XrplJsonOptions.Default);

            Assert.IsNotNull(response);
            Assert.AreEqual(1, response.Transactions.Count);

            TransactionSummary summary = response.Transactions[0];
            Assert.IsNotNull(summary.Transaction, "API v1 wraps the transaction in tx instead of tx_json");
            Assert.AreEqual(ExpectedHash, summary.Hash, "API v1 reports the hash inside the tx envelope");
            Assert.AreEqual(19224694ul, summary.LedgerIndex, "API v1 reports ledger_index inside the tx envelope");

            PaymentResponse payment = summary.Transaction as PaymentResponse;
            Assert.IsNotNull(payment);
            Assert.IsNotNull(payment.Amount);
            Assert.AreEqual("1000000", payment.Amount.Value);
        }
    }
}
