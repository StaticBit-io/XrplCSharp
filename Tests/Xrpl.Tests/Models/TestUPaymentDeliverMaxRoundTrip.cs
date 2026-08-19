using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Models.Transactions;

// Level 3, Task 3 of the raw-response initiative: PaymentResponse always wrote "Amount" back out,
// even for a transaction the node reported under "DeliverMax" (API v2). That is not a lost field —
// it is a substitution for a different, only superficially-equivalent protocol field name. A
// reconciliation screen that shows the signer "what is being signed" would show a field the node
// never sent. Fixtures below are real mainnet captures (or the same transaction reshaped to the
// API v1 wire form, the way TestUAccountTransactionsEnvelope and TestUBaseTransactionResponseFields
// already do): tx E08D6E9754025BA2534A78707605E0601F03ACE063687A0CA1BDDACFCD1698C7 (v1/v2 IOU form)
// and tx AB9D77240EE7414006F979CD8AF43BEAF9EC510F0E99DBFE7A2156BFB7DB56B6 (v2 XRP-drops form, from
// account rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh's account_tx).
namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUPaymentDeliverMaxRoundTrip
    {
        private static readonly JsonSerializerOptions Options = XrplJsonOptions.Default;

        private const string PaymentResponseV2DeliverMax = """
        {
          "Account": "rEPak6n2CEsQmowqsTMnkooskcLaGW9MzE",
          "DeliverMax": "2",
          "Destination": "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
          "DestinationTag": 304200,
          "Fee": "11",
          "Flags": 0,
          "LastLedgerSequence": 106227746,
          "Sequence": 98882928,
          "SigningPubKey": "ED4C767651D1B94D9F1EAA11119442F8228D473E0778926C49D34D3ACE3AFE08B5",
          "TransactionType": "Payment",
          "TxnSignature": "1823144FA737B57DB3418069B7D0ED7FFA57E3FA9ED4434F27EA26056B3D2AFA5E0E608D5C3744B250C1376CFA17F8FA22A8CF371442DBA756ABA8BF7D59080F",
          "ctid": "C654E7BE00000000",
          "date": 839790321,
          "ledger_index": 106227646
        }
        """;

        private const string PaymentResponseV1Amount = """
        {
          "TransactionType": "Payment",
          "Account": "r3PDtZSa5LiYp1Ysn1vMuMzB59RzV3W9QH",
          "Destination": "r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59",
          "Amount": { "currency": "USD", "issuer": "r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59", "value": "1" },
          "Fee": "10",
          "Sequence": 88,
          "hash": "E08D6E9754025BA2534A78707605E0601F03ACE063687A0CA1BDDACFCD1698C7",
          "ledger_index": 348734,
          "validated": true
        }
        """;

        // rippled's `tx` method with api_version: 1 still sends BOTH "Amount" and "DeliverMax" for
        // the same transaction - confirmed live against mainnet on the hash above
        // (Fixtures/Responses/tx_v1_raw.json). Level 3's binary flag ("came in as DeliverMax: yes/no")
        // could only remember one of the two, so the second field silently vanished on round-trip -
        // by count not a regression (v1 used to lose DeliverMax, this lost Amount instead), but a
        // node that sent two fields getting one back out contradicts this class's whole point.
        private const string PaymentResponseV1BothNames = """
        {
          "TransactionType": "Payment",
          "Account": "r3PDtZSa5LiYp1Ysn1vMuMzB59RzV3W9QH",
          "Destination": "r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59",
          "Amount": { "currency": "USD", "issuer": "r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59", "value": "1" },
          "DeliverMax": { "currency": "USD", "issuer": "r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59", "value": "1" },
          "Fee": "10",
          "Sequence": 88,
          "hash": "E08D6E9754025BA2534A78707605E0601F03ACE063687A0CA1BDDACFCD1698C7",
          "ledger_index": 348734,
          "validated": true
        }
        """;

        [TestMethod]
        public void Serialize_PaymentResponse_V2_RoundTripsDeliverMax_NotAmount()
        {
            PaymentResponse payment = JsonSerializer.Deserialize<PaymentResponse>(PaymentResponseV2DeliverMax, Options);
            Assert.IsNotNull(payment);
            Assert.IsNotNull(payment.Amount, "DeliverMax must still populate Amount for callers");
            Assert.AreEqual("2", payment.Amount.Value);

            string output = JsonSerializer.Serialize(payment, Options);

            using JsonDocument doc = JsonDocument.Parse(output);
            Assert.IsTrue(doc.RootElement.TryGetProperty("DeliverMax", out JsonElement deliverMax),
                "the node sent DeliverMax; the round-trip must send it back under the same name");
            Assert.AreEqual("2", deliverMax.GetString());
            Assert.IsFalse(doc.RootElement.TryGetProperty("Amount", out _),
                "must not substitute Amount for a field the node never sent");
        }

        [TestMethod]
        public void Serialize_PaymentResponse_V1_RoundTripsAmount_NotDeliverMax()
        {
            PaymentResponse payment = JsonSerializer.Deserialize<PaymentResponse>(PaymentResponseV1Amount, Options);
            Assert.IsNotNull(payment);
            Assert.IsNotNull(payment.Amount);
            Assert.AreEqual("1", payment.Amount.Value);

            string output = JsonSerializer.Serialize(payment, Options);

            using JsonDocument doc = JsonDocument.Parse(output);
            Assert.IsTrue(doc.RootElement.TryGetProperty("Amount", out JsonElement amount),
                "the node sent Amount; the round-trip must send it back under the same name");
            Assert.AreEqual("USD", amount.GetProperty("currency").GetString());
            Assert.IsFalse(doc.RootElement.TryGetProperty("DeliverMax", out _),
                "must not invent a field the node never sent");
        }

        [TestMethod]
        public void Serialize_PaymentResponse_V1_WithBothNames_RoundTripsBoth()
        {
            PaymentResponse payment = JsonSerializer.Deserialize<PaymentResponse>(PaymentResponseV1BothNames, Options);
            Assert.IsNotNull(payment);
            Assert.IsNotNull(payment.Amount);
            Assert.AreEqual("1", payment.Amount.Value);

            string output = JsonSerializer.Serialize(payment, Options);

            using JsonDocument doc = JsonDocument.Parse(output);
            Assert.IsTrue(doc.RootElement.TryGetProperty("Amount", out JsonElement amount),
                "the node sent Amount; deserializing under DeliverMax's setter afterwards must not erase it");
            Assert.AreEqual("USD", amount.GetProperty("currency").GetString());
            Assert.IsTrue(doc.RootElement.TryGetProperty("DeliverMax", out JsonElement deliverMax),
                "the node also sent DeliverMax; it must not be dropped just because Amount already fired");
            Assert.AreEqual("USD", deliverMax.GetProperty("currency").GetString());
        }

        [TestMethod]
        public void Serialize_PaymentResponse_ConstructedByCode_WritesAmount()
        {
            PaymentResponse payment = new PaymentResponse
            {
                Account = "rEPak6n2CEsQmowqsTMnkooskcLaGW9MzE",
                Destination = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
                Amount = "1000000",
            };

            string output = JsonSerializer.Serialize(payment, Options);

            using JsonDocument doc = JsonDocument.Parse(output);
            Assert.IsTrue(doc.RootElement.TryGetProperty("Amount", out JsonElement amount),
                "an object assembled by application code, not deserialized, defaults to the Amount name");
            Assert.AreEqual("1000000", amount.GetString());
            Assert.IsFalse(doc.RootElement.TryGetProperty("DeliverMax", out _));
        }

        [TestMethod]
        public void Serialize_Payment_ConstructedByCode_WritesAmount()
        {
            Payment payment = new Payment
            {
                Account = "rEPak6n2CEsQmowqsTMnkooskcLaGW9MzE",
                Destination = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
                Amount = "1000000",
            };

            string output = JsonSerializer.Serialize(payment, Options);

            using JsonDocument doc = JsonDocument.Parse(output);
            Assert.IsTrue(doc.RootElement.TryGetProperty("Amount", out JsonElement amount));
            Assert.AreEqual("1000000", amount.GetString());
            Assert.IsFalse(doc.RootElement.TryGetProperty("DeliverMax", out _));
        }
    }
}
