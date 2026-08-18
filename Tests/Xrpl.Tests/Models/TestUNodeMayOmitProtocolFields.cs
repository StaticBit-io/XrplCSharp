using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Subscriptions;

// rippled-issue-6629 nullability pass: 9 response-model properties that were declared
// non-nullable even though the node genuinely omits them under conditions confirmed against the
// rippled C++ source (not xrpl.js - see the analysis this branch is built from). Each fixture
// below is the JSON shape rippled actually sends for the omission case; the assertions prove two
// things per field: (1) deserializing that shape leaves the property null rather than fabricating
// a default 0/false, and (2) re-serializing the result omits the member instead of writing back a
// value the node never sent - the fabrication this whole branch exists to remove.
namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUNodeMayOmitProtocolFields
    {
        private static readonly JsonSerializerOptions Options = XrplJsonOptions.Default;

        // rippled RPCLedgerHelpers.cpp lookupLedger: `if (!ledger->open()) { ledger_hash;
        // ledger_index; } else { ledger_current_index; }` - an open/current ledger response omits
        // the top-level ledger_index entirely and sends ledger_current_index instead.
        // LOBaseLedger.LedgerIndex is inherited by LOLedger (the `ledger` command's response
        // model), which reaches this branch whenever a caller asks for the current ledger.
        //
        // The same fixture also covers BaseLedgerEntity.Closed: rippled's LedgerToJson.cpp
        // fillJson only omits the nested "closed" member for a non-binary response when the
        // ledger is open AND full:true was requested - exactly the shape below (no "closed" key
        // under "ledger").
        private const string OpenLedgerFullReply = """
        {
          "ledger": {
            "account_hash": "D65BC295E298614947C224734E2C66BD7BADA6D7C82F9FBE7A9EA43DC327C1CA",
            "ledger_hash": "333BFF26AA0ADE0276162C6DFE82D3D94D0EC7054DFFE5BF2F7BAF47A1E02920",
            "ledger_index": "106359163",
            "parent_hash": "E266C538805F7751D16AB2C1810ED1182E8ED9023E411592FE5E97BE743A3115",
            "transaction_hash": "91C5E6AC94F08892E932B89C24B8ED83CBA1608508CCDC62B50AFF892AB86A99"
          },
          "ledger_current_index": 106359163,
          "validated": false
        }
        """;

        [TestMethod]
        public void Deserialize_LOLedger_OpenLedger_LedgerIndexAndClosedAreNull()
        {
            LOLedger ledger = JsonSerializer.Deserialize<LOLedger>(OpenLedgerFullReply, Options);

            Assert.IsNotNull(ledger);
            Assert.IsNull(ledger.LedgerIndex, "the node did not send a top-level ledger_index for an open ledger - the property must stay null, not fabricate 0");
            LedgerEntity entity = ledger.LedgerEntity as LedgerEntity;
            Assert.IsNotNull(entity);
            Assert.IsNull(entity.Closed, "the node omitted \"closed\" for this open+full response - the property must stay null, not fabricate false");
        }

        [TestMethod]
        public void Serialize_LOLedger_OpenLedger_OmitsLedgerIndexAndClosed()
        {
            LOLedger ledger = JsonSerializer.Deserialize<LOLedger>(OpenLedgerFullReply, Options);

            string output = JsonSerializer.Serialize(ledger, Options);
            using JsonDocument doc = JsonDocument.Parse(output);

            Assert.IsFalse(doc.RootElement.TryGetProperty("ledger_index", out _), "re-serialized output must not fabricate a top-level ledger_index the node never sent");
            JsonElement nested = doc.RootElement.GetProperty("ledger");
            Assert.IsFalse(nested.TryGetProperty("closed", out _), "re-serialized output must not fabricate \"closed\" the node never sent");
        }

        // rippled LedgerToJson.cpp: the per-transaction "validated" member is only added inside
        // the apiVersion > 1 branch of the ledger command's transaction expansion; the legacy v1
        // branch copies the flat transaction JSON and never writes it at all.
        private const string LedgerTransactionApiV1Flat = """
        {
          "hash": "AB9D77240EE7414006F979CD8AF43BEAF9EC510F0E99DBFE7A2156BFB7DB56B6",
          "ledger_hash": "333BFF26AA0ADE0276162C6DFE82D3D94D0EC7054DFFE5BF2F7BAF47A1E02920",
          "ledger_index": 106359162
        }
        """;

        [TestMethod]
        public void Deserialize_LedgerTransaction_ApiV1_ValidatedIsNull()
        {
            LedgerTransaction tx = JsonSerializer.Deserialize<LedgerTransaction>(LedgerTransactionApiV1Flat, Options);

            Assert.IsNotNull(tx);
            Assert.IsNull(tx.Validated, "API v1 ledger transaction entries carry no \"validated\" member at all - the property must stay null, not fabricate false");
        }

        [TestMethod]
        public void Serialize_LedgerTransaction_ApiV1_OmitsValidated()
        {
            LedgerTransaction tx = JsonSerializer.Deserialize<LedgerTransaction>(LedgerTransactionApiV1Flat, Options);

            string output = JsonSerializer.Serialize(tx, Options);
            using JsonDocument doc = JsonDocument.Parse(output);

            Assert.IsFalse(doc.RootElement.TryGetProperty("validated", out _), "re-serialized output must not fabricate \"validated\" the node never sent");
        }

        // rippled's shared lookupLedger helper sets ledger_current_index only in the else branch
        // of `if (!ledger->open())` - a noripple_check resolved against a closed/validated ledger
        // sends ledger_hash/ledger_index instead (fields NoRippleCheck does not even model) and
        // omits ledger_current_index entirely.
        private const string NoRippleCheckValidatedLedger = """
        {
          "ledger_hash": "333BFF26AA0ADE0276162C6DFE82D3D94D0EC7054DFFE5BF2F7BAF47A1E02920",
          "ledger_index": 106359162,
          "problems": [],
          "validated": true
        }
        """;

        [TestMethod]
        public void Deserialize_NoRippleCheck_ValidatedLedger_LedgerCurrentIndexIsNull()
        {
            NoRippleCheck result = JsonSerializer.Deserialize<NoRippleCheck>(NoRippleCheckValidatedLedger, Options);

            Assert.IsNotNull(result);
            Assert.IsNull(result.LedgerCurrentIndex, "a noripple_check resolved against a closed ledger never sends ledger_current_index - the property must stay null, not fabricate 0");
        }

        [TestMethod]
        public void Serialize_NoRippleCheck_ValidatedLedger_OmitsLedgerCurrentIndex()
        {
            NoRippleCheck result = JsonSerializer.Deserialize<NoRippleCheck>(NoRippleCheckValidatedLedger, Options);

            string output = JsonSerializer.Serialize(result, Options);
            using JsonDocument doc = JsonDocument.Parse(output);

            Assert.IsFalse(doc.RootElement.TryGetProperty("ledger_current_index", out _), "re-serialized output must not fabricate ledger_current_index the node never sent");
        }

        // rippled's `feature` handler (Feature.cpp) never calls lookupLedger and never writes
        // ledger_hash/ledger_index/validated at all - every real `feature` response omits
        // ledger_index unconditionally, not just sometimes.
        private const string FeatureResponseWithoutLedgerFields = """
        {
          "7DB0788C020F02780A673DC74757F23823FA3014C1866E72CC4CD8B226CD6EF4": {
            "enabled": true,
            "name": "MultiSign",
            "supported": true
          }
        }
        """;

        [TestMethod]
        public void Deserialize_ServerFeatures_LedgerIndexIsNull()
        {
            // ServerFeaturesConverter.Write throws NotSupportedException (serialization of this
            // type is not required), so unlike the other fields in this file there is no
            // re-serialization half to assert on here - only that the converter stopped defaulting
            // the missing member to 0.
            ServerFeatures result = JsonSerializer.Deserialize<ServerFeatures>(FeatureResponseWithoutLedgerFields, Options);

            Assert.IsNotNull(result);
            Assert.IsNull(result.LedgerIndex, "the feature command never sends ledger_index - the property must stay null, not fabricate 0");
        }

        // rippled NetworkOpsImp::subLedger gates ledger_index/reserve_base/reserve_inc on
        // ledgerMaster_.getValidatedLedger() returning non-null; a node with no validated ledger
        // yet (e.g. just started) sends none of them in the subscribe command's own reply.
        private const string SubscribeReplyNoValidatedLedgerYet = """
        {
          "status": "success",
          "type": "response"
        }
        """;

        [TestMethod]
        public void Deserialize_LedgerStreamResponse_NoValidatedLedger_FieldsAreNull()
        {
            LedgerStreamResponse result = JsonSerializer.Deserialize<LedgerStreamResponse>(SubscribeReplyNoValidatedLedgerYet, Options);

            Assert.IsNotNull(result);
            Assert.IsNull(result.LedgerIndex, "subLedger omits ledger_index with no validated ledger yet - must not fabricate 0");
            Assert.IsNull(result.ReserveBase, "subLedger omits reserve_base with no validated ledger yet - must not fabricate 0");
            Assert.IsNull(result.ReserveInc, "subLedger omits reserve_inc with no validated ledger yet - must not fabricate 0");
            Assert.IsNull(result.FeeBase, "subLedger omits fee_base with no validated ledger yet - must not fabricate 0");
            Assert.IsNull(result.LedgerTime, "subLedger omits ledger_time with no validated ledger yet - must not fabricate 0");
            Assert.IsNull(result.FeeRef, "subLedger emits fee_ref only when XRPFees is disabled, and never outside the validated-ledger gate - must not fabricate 0");
            Assert.IsNull(result.TxnCount, "subLedger never emits txn_count on this path at all - must not fabricate 0");
        }

        [TestMethod]
        public void Serialize_LedgerStreamResponse_NoValidatedLedger_OmitsFields()
        {
            LedgerStreamResponse result = JsonSerializer.Deserialize<LedgerStreamResponse>(SubscribeReplyNoValidatedLedgerYet, Options);

            string output = JsonSerializer.Serialize(result, Options);
            using JsonDocument doc = JsonDocument.Parse(output);

            Assert.IsFalse(doc.RootElement.TryGetProperty("ledger_index", out _), "re-serialized output must not fabricate ledger_index the node never sent");
            Assert.IsFalse(doc.RootElement.TryGetProperty("reserve_base", out _), "re-serialized output must not fabricate reserve_base the node never sent");
            Assert.IsFalse(doc.RootElement.TryGetProperty("reserve_inc", out _), "re-serialized output must not fabricate reserve_inc the node never sent");
            Assert.IsFalse(doc.RootElement.TryGetProperty("fee_base", out _), "re-serialized output must not fabricate fee_base the node never sent");
            Assert.IsFalse(doc.RootElement.TryGetProperty("fee_ref", out _), "re-serialized output must not fabricate fee_ref the node never sent");
            Assert.IsFalse(doc.RootElement.TryGetProperty("ledger_time", out _), "re-serialized output must not fabricate ledger_time the node never sent");
            Assert.IsFalse(doc.RootElement.TryGetProperty("txn_count", out _), "re-serialized output must not fabricate txn_count the node never sent");
        }

        // rippled NetworkOpsImp::pubValidation sets ledger_index only when the underlying
        // STValidation carries the optional sfLedgerSequence field - a partial validation missing
        // that field sends no ledger_index at all.
        private const string ValidationReceivedWithoutLedgerSequence = """
        {
          "type": "validationReceived",
          "flags": 2147483649,
          "full": false,
          "ledger_hash": "EC02890710AAA2B71221B0D560CFB22D64317C07B7406B02959AD84BAD33E602",
          "master_key": "nHUon2tpyJEHHYGmxqeGu37cvPYHzrMtUNQFVdCgGNvEkjmCpTqK",
          "signature": "3045022100E199B55643F66BC6B37DBC5E185321CF952FD35D13D9E8001EB2564FFB94A07602201746C9A4F7A93647131A2DEB03B76F05E426EC67A5A27D77F4FF2603B9A528E6",
          "signing_time": 515115322,
          "validation_public_key": "n94Gnc6svmaPPRHUAyyib1gQUov8sYbjLoEwUBYPH39qHZXuo8ZT"
        }
        """;

        [TestMethod]
        public void Deserialize_ValidationStream_NoLedgerSequence_LedgerIndexIsNull()
        {
            ValidationStream result = JsonSerializer.Deserialize<ValidationStream>(ValidationReceivedWithoutLedgerSequence, Options);

            Assert.IsNotNull(result);
            Assert.IsNull(result.LedgerIndex, "pubValidation omits ledger_index when sfLedgerSequence is absent - must not fabricate 0");
        }

        [TestMethod]
        public void Serialize_ValidationStream_NoLedgerSequence_OmitsLedgerIndex()
        {
            ValidationStream result = JsonSerializer.Deserialize<ValidationStream>(ValidationReceivedWithoutLedgerSequence, Options);

            string output = JsonSerializer.Serialize(result, Options);
            using JsonDocument doc = JsonDocument.Parse(output);

            Assert.IsFalse(doc.RootElement.TryGetProperty("ledger_index", out _), "re-serialized output must not fabricate ledger_index the node never sent");
        }

        // rippled NetworkOpsImp::pubLedger guards fee_ref with `if (!rules().enabled(featureXRPFees))`.
        // XRPFees is active on mainnet, so no current node sends the member at all - which makes
        // this the one omission below that is not an edge case but the everyday shape of the
        // ledgerClosed stream. Captured from a real mainnet subscription.
        private const string LedgerClosedFromMainnet = """
        {
          "type": "ledgerClosed",
          "fee_base": 10,
          "ledger_hash": "1BF9F0D8B2C1F94BF9C69AC1E2A34DEE1AAB68A9D1CDBD6B9E7EF0A5C0C0F3E1",
          "ledger_index": 106384960,
          "ledger_time": 837719525,
          "network_id": 0,
          "reserve_base": 1000000,
          "reserve_inc": 200000,
          "txn_count": 42,
          "validated_ledgers": "32570-106384960"
        }
        """;

        [TestMethod]
        public void Deserialize_LedgerStream_XrpFeesEnabled_FeeRefIsNull()
        {
            LedgerStream result = JsonSerializer.Deserialize<LedgerStream>(LedgerClosedFromMainnet, Options);

            Assert.IsNotNull(result);
            Assert.IsNull(result.FeeRef, "XRPFees is active on mainnet, so pubLedger never sends fee_ref - the property must stay null, not fabricate 0");
            Assert.AreEqual(10u, result.FeeBase, "fee_base is unconditional in pubLedger and must still round-trip");
            Assert.AreEqual(42u, result.TxnCount, "txn_count is unconditional in pubLedger, unlike the subLedger reply");
        }

        [TestMethod]
        public void Serialize_LedgerStream_XrpFeesEnabled_OmitsFeeRef()
        {
            LedgerStream result = JsonSerializer.Deserialize<LedgerStream>(LedgerClosedFromMainnet, Options);

            string output = JsonSerializer.Serialize(result, Options);
            using JsonDocument doc = JsonDocument.Parse(output);

            Assert.IsFalse(doc.RootElement.TryGetProperty("fee_ref", out _), "re-serialized output must not fabricate fee_ref into every mainnet ledgerClosed event");
            Assert.IsTrue(doc.RootElement.TryGetProperty("fee_base", out _), "fee_base was sent by the node and must survive the round-trip");
        }
    }
}
