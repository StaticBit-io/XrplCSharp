using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

// Level 3, Task 1/2 of the raw-response initiative: close_time_iso, ctid, meta_blob and tx_blob
// had no property to land on at all. Every payload below is either a real mainnet capture, or a
// real capture reshaped to the API v1 wire form the same way TestUAccountTransactionsEnvelope
// already does (rippled genuinely flattens the transaction envelope for v1; this SDK's own
// PaymentJsonWithCtid fixture in TransactionResponseExtensionDataTests uses the same convention).
// Source captures: tx E08D6E9754025BA2534A78707605E0601F03ACE063687A0CA1BDDACFCD1698C7 (tx.json,
// tx binary.json), account rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh (account_tx.json, ledger.json).
namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUBaseTransactionResponseFields
    {
        private static readonly JsonSerializerOptions Options = XrplJsonOptions.Default;

        // account_tx entry for hash AB9D77240EE7414006F979CD8AF43BEAF9EC510F0E99DBFE7A2156BFB7DB56B6:
        // API v2 nests ctid inside tx_json itself (unlike the singular tx method, where it sits
        // beside tx_json — see TestUTransactionSummaryCtidFromRealTx below).
        private const string PaymentTxJsonWithNestedCtid = """
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

        [TestMethod]
        public void Deserialize_PaymentResponse_ReadsCtidNestedInTxJson()
        {
            PaymentResponse payment = JsonSerializer.Deserialize<PaymentResponse>(PaymentTxJsonWithNestedCtid, Options);

            Assert.IsNotNull(payment);
            Assert.AreEqual("C654E7BE00000000", payment.Ctid);
        }

        [TestMethod]
        public void Serialize_PaymentResponse_RoundTripsCtid()
        {
            PaymentResponse payment = JsonSerializer.Deserialize<PaymentResponse>(PaymentTxJsonWithNestedCtid, Options);

            string output = JsonSerializer.Serialize(payment, Options);

            using JsonDocument doc = JsonDocument.Parse(output);
            Assert.IsTrue(doc.RootElement.TryGetProperty("ctid", out JsonElement value));
            Assert.AreEqual("C654E7BE00000000", value.GetString());
        }

        // Reshaped to the v1 flat wire form: real close_time_iso/ctid/hash/ledger_index from the
        // tx response envelope, merged onto the transaction object at one level the way rippled's
        // API v1 genuinely does (Amount replaces DeliverMax, matching the v1/v2 alias already
        // covered elsewhere in this SDK).
        private const string PaymentV1FlatWithCloseTimeIsoAndCtid = """
        {
          "TransactionType": "Payment",
          "Account": "r3PDtZSa5LiYp1Ysn1vMuMzB59RzV3W9QH",
          "Destination": "r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59",
          "Amount": { "currency": "USD", "issuer": "r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59", "value": "1" },
          "Fee": "10",
          "Sequence": 88,
          "hash": "E08D6E9754025BA2534A78707605E0601F03ACE063687A0CA1BDDACFCD1698C7",
          "ledger_index": 348734,
          "close_time_iso": "2013-03-12T23:16:50Z",
          "ctid": "C005523E00000000",
          "validated": true
        }
        """;

        [TestMethod]
        public void Deserialize_PaymentResponse_V1Flat_ReadsCloseTimeIsoAndCtid()
        {
            PaymentResponse payment = JsonSerializer.Deserialize<PaymentResponse>(PaymentV1FlatWithCloseTimeIsoAndCtid, Options);

            Assert.IsNotNull(payment);
            Assert.AreEqual(new DateTime(2013, 3, 12, 23, 16, 50, DateTimeKind.Utc), payment.CloseTimeIso);
            Assert.AreEqual("C005523E00000000", payment.Ctid);
            Assert.AreEqual("E08D6E9754025BA2534A78707605E0601F03ACE063687A0CA1BDDACFCD1698C7", payment.Hash);
        }

        [TestMethod]
        public void Serialize_PaymentResponse_V1Flat_RoundTripsCloseTimeIsoAndCtid()
        {
            PaymentResponse payment = JsonSerializer.Deserialize<PaymentResponse>(PaymentV1FlatWithCloseTimeIsoAndCtid, Options);

            string output = JsonSerializer.Serialize(payment, Options);

            using JsonDocument doc = JsonDocument.Parse(output);
            Assert.IsTrue(doc.RootElement.TryGetProperty("close_time_iso", out JsonElement closeTimeIso));
            Assert.AreEqual("2013-03-12T23:16:50Z", closeTimeIso.GetString());
            Assert.IsTrue(doc.RootElement.TryGetProperty("ctid", out JsonElement ctid));
            Assert.AreEqual("C005523E00000000", ctid.GetString());
        }

        // Real "result" of a tx (API v2) response — rippled puts close_time_iso and ctid beside
        // tx_json here, not nested inside it (contrast with PaymentTxJsonWithNestedCtid above).
        private const string TxV2Result = """
        {
          "close_time_iso": "2013-03-12T23:16:50Z",
          "ctid": "C005523E00000000",
          "hash": "E08D6E9754025BA2534A78707605E0601F03ACE063687A0CA1BDDACFCD1698C7",
          "ledger_hash": "195F62F34EB2CCFA4C5888BA20387E82EB353DDB4508BAE6A835AF19FB8B0C09",
          "ledger_index": 348734,
          "meta": {
            "TransactionIndex": 0,
            "TransactionResult": "tesSUCCESS",
            "delivered_amount": "unavailable"
          },
          "status": "success",
          "tx_json": {
            "Account": "r3PDtZSa5LiYp1Ysn1vMuMzB59RzV3W9QH",
            "DeliverMax": { "currency": "USD", "issuer": "r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59", "value": "1" },
            "Destination": "r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59",
            "Fee": "10",
            "Sequence": 88,
            "SigningPubKey": "02EAE5DAB54DD8E1C49641D848D5B97D1B29149106174322EDF98A1B2CCE5D7F8E",
            "TransactionType": "Payment",
            "TxnSignature": "30440220791B6A3E036ECEFFE99E8D4957564E8C84D1548C8C3E80A87ED1AA646ECCFB16022037C5CAC97E34E3021EBB426479F2ACF3ACA75DB91DCC48D1BCFB4CF547CFEAA0",
            "date": 416445410,
            "ledger_index": 348734
          },
          "validated": true
        }
        """;

        [TestMethod]
        public void Deserialize_TransactionSummary_ReadsCtidBesideTxJson()
        {
            TransactionSummary summary = JsonSerializer.Deserialize<TransactionSummary>(TxV2Result, Options);

            Assert.IsNotNull(summary);
            Assert.AreEqual("C005523E00000000", summary.Ctid);
            Assert.AreEqual(new DateTime(2013, 3, 12, 23, 16, 50, DateTimeKind.Utc), summary.CloseTimeIso);
        }

        [TestMethod]
        public void Serialize_TransactionSummary_RoundTripsCtid()
        {
            TransactionSummary summary = JsonSerializer.Deserialize<TransactionSummary>(TxV2Result, Options);

            string output = JsonSerializer.Serialize(summary, Options);

            using JsonDocument doc = JsonDocument.Parse(output);
            Assert.IsTrue(doc.RootElement.TryGetProperty("ctid", out JsonElement value));
            Assert.AreEqual("C005523E00000000", value.GetString());
        }

        // Real "result" of a tx (API v2, binary: true) response for the same transaction as
        // TxV2Result above. rippled drops "meta"/"tx_json" entirely and sends meta_blob/tx_blob
        // instead — before these properties existed, TransactionSummary lost the response body
        // wholesale (measured: 2246 B in, 195 B out).
        private const string TxV2BinaryResult = """
        {
          "close_time_iso": "2013-03-12T23:16:50Z",
          "ctid": "C005523E00000000",
          "hash": "E08D6E9754025BA2534A78707605E0601F03ACE063687A0CA1BDDACFCD1698C7",
          "ledger_hash": "195F62F34EB2CCFA4C5888BA20387E82EB353DDB4508BAE6A835AF19FB8B0C09",
          "ledger_index": 348734,
          "meta_blob": "201C00000000F8E5110061250005521C55C26AA6B4F7C3B9F55E17CD0D11F12032A1C7AD2757229FFD277C9447A8815E6E",
          "status": "success",
          "tx_blob": "1200002200000000240000005861D4838D7EA4C6800000000000000000000000000055534400000000005E7B112523F68D2F5E879DB4EAC51C6698A6930468400000000000000A",
          "validated": true
        }
        """;

        [TestMethod]
        public void Deserialize_TransactionSummary_Binary_ReadsMetaBlobAndTxBlob()
        {
            TransactionSummary summary = JsonSerializer.Deserialize<TransactionSummary>(TxV2BinaryResult, Options);

            Assert.IsNotNull(summary);
            Assert.IsNotNull(summary.MetaBlob, "meta_blob did not bind to TransactionSummary.MetaBlob");
            Assert.IsNotNull(summary.TxBlob, "tx_blob did not bind to TransactionSummary.TxBlob");
            Assert.IsTrue(summary.MetaBlob.StartsWith("201C00000000F8E5", StringComparison.Ordinal));
            Assert.IsTrue(summary.TxBlob.StartsWith("1200002200000000", StringComparison.Ordinal));
            Assert.IsNull(summary.Transaction, "tx_json is absent from a binary response");
        }

        [TestMethod]
        public void Serialize_TransactionSummary_Binary_RoundTripsMetaBlobAndTxBlob()
        {
            TransactionSummary summary = JsonSerializer.Deserialize<TransactionSummary>(TxV2BinaryResult, Options);

            string output = JsonSerializer.Serialize(summary, Options);

            using JsonDocument doc = JsonDocument.Parse(output);
            Assert.IsTrue(doc.RootElement.TryGetProperty("meta_blob", out JsonElement metaBlob), "output is missing meta_blob");
            string metaBlobValue = metaBlob.GetString();
            Assert.IsNotNull(metaBlobValue, "meta_blob serialized as JSON null instead of the blob string");
            Assert.IsTrue(metaBlobValue.StartsWith("201C00000000F8E5", StringComparison.Ordinal));
            Assert.IsTrue(doc.RootElement.TryGetProperty("tx_blob", out JsonElement txBlob), "output is missing tx_blob");
            string txBlobValue = txBlob.GetString();
            Assert.IsNotNull(txBlobValue, "tx_blob serialized as JSON null instead of the blob string");
            Assert.IsTrue(txBlobValue.StartsWith("1200002200000000", StringComparison.Ordinal));
        }

        // Real "result" of a ledger response (ledger 106359162). ledger.close_time_iso was
        // already modeled on LedgerEntity before this level, but was measured as lost anyway: the
        // shared FromStringDateTimeConverter silently returned null for "Z"-suffixed timestamps
        // (fixed alongside this level — see TestUFromStringDateTimeConverter). This regression
        // test exists to keep that fix honest for the ledger command specifically.
        private const string LedgerResult = """
        {
          "ledger": {
            "account_hash": "D65BC295E298614947C224734E2C66BD7BADA6D7C82F9FBE7A9EA43DC327C1CA",
            "close_flags": 0,
            "close_time": 840298580,
            "close_time_human": "2026-Aug-17 16:16:20.000000000 UTC",
            "close_time_iso": "2026-08-17T16:16:20Z",
            "close_time_resolution": 10,
            "closed": true,
            "ledger_hash": "333BFF26AA0ADE0276162C6DFE82D3D94D0EC7054DFFE5BF2F7BAF47A1E02920",
            "ledger_index": 106359162,
            "parent_close_time": 840298572,
            "parent_hash": "E266C538805F7751D16AB2C1810ED1182E8ED9023E411592FE5E97BE743A3115",
            "total_coins": "99985627508883176",
            "transaction_hash": "91C5E6AC94F08892E932B89C24B8ED83CBA1608508CCDC62B50AFF892AB86A99"
          },
          "ledger_hash": "333BFF26AA0ADE0276162C6DFE82D3D94D0EC7054DFFE5BF2F7BAF47A1E02920",
          "ledger_index": 106359162,
          "status": "success",
          "validated": true
        }
        """;

        [TestMethod]
        public void Deserialize_LOLedger_ReadsNestedCloseTimeIso()
        {
            LOLedger ledger = JsonSerializer.Deserialize<LOLedger>(LedgerResult, Options);

            Assert.IsNotNull(ledger);
            LedgerEntity entity = ledger.LedgerEntity as LedgerEntity;
            Assert.IsNotNull(entity, "a non-binary ledger response deserializes to LedgerEntity");
            Assert.AreEqual(new DateTime(2026, 8, 17, 16, 16, 20, DateTimeKind.Utc), entity.CloseTimeIso);
        }

        [TestMethod]
        public void Serialize_LOLedger_RoundTripsNestedCloseTimeIso()
        {
            LOLedger ledger = JsonSerializer.Deserialize<LOLedger>(LedgerResult, Options);

            string output = JsonSerializer.Serialize(ledger, Options);

            using JsonDocument doc = JsonDocument.Parse(output);
            JsonElement ledgerElement = doc.RootElement.GetProperty("ledger");
            Assert.IsTrue(ledgerElement.TryGetProperty("close_time_iso", out JsonElement value));
            Assert.AreEqual("2026-08-17T16:16:20Z", value.GetString());
        }
    }
}
