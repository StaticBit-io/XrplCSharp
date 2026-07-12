using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec;
using Xrpl.Client.Json;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

namespace Xrpl.Tests.Models.Tests
{
    /// <summary>
    /// Pins the protocol-completeness pass driven by rippled server_definitions:
    /// newly added transaction/ledger-object fields round-trip, and the
    /// NFTokenModify validation dispatch bug stays fixed.
    /// </summary>
    [TestClass]
    public class TestUProtocolCompleteness
    {
        private static string Account1;
        private static string Account2;

        [ClassInitialize]
        public static void Init(TestContext _)
        {
            Account1 = XrplWallet.Generate().ClassicAddress;
            Account2 = XrplWallet.Generate().ClassicAddress;
        }

        [TestMethod]
        public async Task TestUNFTokenModify_DispatchesToOwnValidator()
        {
            // Pre-fix the dispatcher routed NFTokenModify to ValidateNFTokenMint,
            // which rejects a valid Modify (no NFTokenTaxon present)
            Dictionary<string, object> tx = new()
            {
                ["TransactionType"] = "NFTokenModify",
                ["Account"] = Account1,
                ["NFTokenID"] = new string('A', 64),
            };
            await Validation.Validate(tx);
        }

        [TestMethod]
        public void TestUNFTokenMint_MintOfferFields_RoundTrip()
        {
            NFTokenMint mint = new NFTokenMint
            {
                Account = Account1,
                NFTokenTaxon = 7,
                Amount = new Currency { ValueAsXrp = 2m },
                Destination = Account2,
                Expiration = 800000000,
                Sequence = 1,
                Fee = new Currency { Value = "12" },
                SigningPublicKey = "",
            };
            JsonObject json = JsonNode.Parse(mint.ToJson())!.AsObject();
            string blob = XrplBinaryCodec.Encode(json);
            JsonObject decoded = XrplBinaryCodec.Decode(blob).AsObject();

            Assert.AreEqual("2000000", decoded["Amount"]!.GetValue<string>());
            Assert.AreEqual(Account2, decoded["Destination"]!.GetValue<string>());
            Assert.AreEqual(800000000u, decoded["Expiration"]!.GetValue<uint>());
        }

        [TestMethod]
        public void TestUMPTokenIssuanceSet_DynamicFields_RoundTrip()
        {
            MPTokenIssuanceSet set = new MPTokenIssuanceSet
            {
                Account = Account1,
                MPTokenIssuanceID = "00000001A407AF5856CCF3C42619DAA925813FC955C72983",
                MutableFlags = 3,
                TransferFee = 250,
                MPTokenMetadata = "DEADBEEF",
                DomainID = new string('B', 64),
                Sequence = 1,
                Fee = new Currency { Value = "12" },
                SigningPublicKey = "",
            };
            JsonObject json = JsonNode.Parse(set.ToJson())!.AsObject();
            string blob = XrplBinaryCodec.Encode(json);
            JsonObject decoded = XrplBinaryCodec.Decode(blob).AsObject();

            Assert.AreEqual(3u, decoded["MutableFlags"]!.GetValue<uint>());
            Assert.AreEqual(250u, decoded["TransferFee"]!.GetValue<uint>());
            Assert.AreEqual("DEADBEEF", decoded["MPTokenMetadata"]!.GetValue<string>());
            Assert.AreEqual(new string('B', 64), decoded["DomainID"]!.GetValue<string>());
        }

        [TestMethod]
        public void TestULORippleState_SponsorFields_Deserialize()
        {
            string json = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["LedgerEntryType"] = "RippleState",
                ["Flags"] = 65536,
                ["Balance"] = new Dictionary<string, object> { ["currency"] = "USD", ["issuer"] = Account1, ["value"] = "5" },
                ["HighLimit"] = new Dictionary<string, object> { ["currency"] = "USD", ["issuer"] = Account1, ["value"] = "10" },
                ["LowLimit"] = new Dictionary<string, object> { ["currency"] = "USD", ["issuer"] = Account2, ["value"] = "0" },
                ["HighSponsor"] = Account1,
                ["LowSponsor"] = Account2,
            });
            LORippleState state = JsonSerializer.Deserialize<LORippleState>(json, XrplJsonOptions.Default);
            Assert.AreEqual(Account1, state.HighSponsor);
            Assert.AreEqual(Account2, state.LowSponsor);
        }
    }
}
