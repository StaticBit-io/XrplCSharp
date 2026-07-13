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

using TxFormat = Xrpl.Models.Transaction.TxFormat;
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
            System.DateTime rippleEpoch = new System.DateTime(2000, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
            NFTokenMint mint = new NFTokenMint
            {
                Account = Account1,
                NFTokenTaxon = 7,
                Amount = new Currency { ValueAsXrp = 2m },
                Destination = Account2,
                Expiration = rippleEpoch.AddSeconds(800000000),
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
                MutableFlags = MPTokenIssuanceSetMutableFlags.tmfMPTSetCanLock | MPTokenIssuanceSetMutableFlags.tmfMPTSetRequireAuth,
                TransferFee = 250,
                MPTokenMetadata = "DEADBEEF",
                DomainID = new string('B', 64),
                IssuerEncryptionKey = new string('C', 66),
                AuditorEncryptionKey = new string('D', 66),
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
            Assert.AreEqual(new string('C', 66), decoded["IssuerEncryptionKey"]!.GetValue<string>());
            Assert.AreEqual(new string('D', 66), decoded["AuditorEncryptionKey"]!.GetValue<string>());
        }

        [TestMethod]
        public void TestUSetFee_XRPFeesFields_RoundTrip()
        {
            JsonObject json = JsonNode.Parse($@"{{
                ""TransactionType"": ""SetFee"",
                ""Account"": ""rrrrrrrrrrrrrrrrrrrrrhoLvTp"",
                ""Fee"": ""0"",
                ""Sequence"": 0,
                ""SigningPubKey"": """",
                ""LedgerSequence"": 123456,
                ""BaseFeeDrops"": ""10"",
                ""ReserveBaseDrops"": ""1000000"",
                ""ReserveIncrementDrops"": ""200000""
            }}")!.AsObject();
            string blob = XrplBinaryCodec.Encode(json);
            JsonObject decoded = XrplBinaryCodec.Decode(blob).AsObject();

            Assert.AreEqual("10", decoded["BaseFeeDrops"]!.GetValue<string>());
            Assert.AreEqual("1000000", decoded["ReserveBaseDrops"]!.GetValue<string>());
            Assert.AreEqual("200000", decoded["ReserveIncrementDrops"]!.GetValue<string>());

            TxFormat setFeeFormat = TxFormat.Formats[BinaryCodec.Types.TransactionType.SetFee];
            Assert.IsTrue(setFeeFormat.ContainsKey(BinaryCodec.Enums.Field.BaseFeeDrops));
            Assert.IsTrue(setFeeFormat.ContainsKey(BinaryCodec.Enums.Field.ReserveBaseDrops));
            Assert.IsTrue(setFeeFormat.ContainsKey(BinaryCodec.Enums.Field.ReserveIncrementDrops));
            Assert.IsTrue(TxFormat.Formats[BinaryCodec.Types.TransactionType.UNLModify]
                .ContainsKey(BinaryCodec.Enums.Field.UNLModifyDisabling));
        }

        [TestMethod]
        public void TestUAMMDeposit_TradingFee_RoundTrip()
        {
            JsonObject json = JsonNode.Parse($@"{{
                ""TransactionType"": ""AMMDeposit"",
                ""Account"": ""{Account1}"",
                ""Fee"": ""12"",
                ""Sequence"": 1,
                ""SigningPubKey"": """",
                ""Asset"": {{ ""currency"": ""XRP"" }},
                ""Asset2"": {{ ""currency"": ""USD"", ""issuer"": ""{Account2}"" }},
                ""TradingFee"": 600
            }}")!.AsObject();
            string blob = XrplBinaryCodec.Encode(json);
            JsonObject decoded = XrplBinaryCodec.Decode(blob).AsObject();

            Assert.AreEqual(600u, decoded["TradingFee"]!.GetValue<uint>());
            Assert.IsTrue(TxFormat.Formats[BinaryCodec.Types.TransactionType.AMMDeposit]
                .ContainsKey(BinaryCodec.Enums.Field.TradingFee));
        }

        [TestMethod]
        public void TestUVaultDelete_MemoData_RoundTrip()
        {
            JsonObject json = JsonNode.Parse($@"{{
                ""TransactionType"": ""VaultDelete"",
                ""Account"": ""{Account1}"",
                ""Fee"": ""12"",
                ""Sequence"": 1,
                ""SigningPubKey"": """",
                ""VaultID"": ""{new string('E', 64)}"",
                ""MemoData"": ""CAFEBABE""
            }}")!.AsObject();
            string blob = XrplBinaryCodec.Encode(json);
            JsonObject decoded = XrplBinaryCodec.Decode(blob).AsObject();

            Assert.AreEqual("CAFEBABE", decoded["MemoData"]!.GetValue<string>());
            Assert.IsTrue(TxFormat.Formats[BinaryCodec.Types.TransactionType.VaultDelete]
                .ContainsKey(BinaryCodec.Enums.Field.MemoData));
        }

        [TestMethod]
        public void TestUUint64_FieldContext_RoundTrip()
        {
            // Digit-only value of a hex-semantics UInt64 field must be parsed as hex:
            // pre-fix "0000000000000012" was parsed as decimal 12 (0x0C) and the
            // round-trip silently corrupted the value.
            JsonObject hexField = JsonNode.Parse(@"{""OwnerNode"": ""0000000000000012""}")!.AsObject();
            JsonObject decodedHex = XrplBinaryCodec.Decode(XrplBinaryCodec.Encode(hexField)).AsObject();
            Assert.AreEqual("0000000000000012", decodedHex["OwnerNode"]!.GetValue<string>());

            // kSmdBaseTen fields keep decimal-string semantics in both directions
            JsonObject baseTenField = JsonNode.Parse(@"{""MaximumAmount"": ""18446744073709551615""}")!.AsObject();
            JsonObject decodedBaseTen = XrplBinaryCodec.Decode(XrplBinaryCodec.Encode(baseTenField)).AsObject();
            Assert.AreEqual("18446744073709551615", decodedBaseTen["MaximumAmount"]!.GetValue<string>());
        }

        [TestMethod]
        public async Task TestUMPTokenIssuanceSet_PreflightRules()
        {
            // rippled MPTokenIssuanceSet::preflight rules pinned client-side
            Dictionary<string, object> tx = new()
            {
                ["TransactionType"] = "MPTokenIssuanceSet",
                ["Account"] = Account1,
                ["MPTokenIssuanceID"] = "00000001A407AF5856CCF3C42619DAA925813FC955C72983",
            };

            // MutableFlags: zero and out-of-mask values are temINVALID_FLAG
            tx["MutableFlags"] = 0u;
            await Assert.ThrowsExactlyAsync<Xrpl.Client.Exceptions.ValidationException>(() => Validation.ValidateMPTokenIssuanceSet(tx));
            tx["MutableFlags"] = 0x80u;
            await Assert.ThrowsExactlyAsync<Xrpl.Client.Exceptions.ValidationException>(() => Validation.ValidateMPTokenIssuanceSet(tx));

            // Non-zero TransferFee combined with enabling confidential balances is temBAD_TRANSFER_FEE
            tx["MutableFlags"] = (uint)MPTokenIssuanceSetMutableFlags.tmfMPTSetCanHoldConfidentialBalance;
            tx["TransferFee"] = 10u;
            await Assert.ThrowsExactlyAsync<Xrpl.Client.Exceptions.ValidationException>(() => Validation.ValidateMPTokenIssuanceSet(tx));

            tx["TransferFee"] = 0u;
            await Validation.ValidateMPTokenIssuanceSet(tx);
        }

        [TestMethod]
        public async Task TestUMPTokenIssuanceCreate_MutableFlagsMask()
        {
            Dictionary<string, object> tx = new()
            {
                ["TransactionType"] = "MPTokenIssuanceCreate",
                ["Account"] = Account1,
            };

            tx["MutableFlags"] = 0u;
            await Assert.ThrowsExactlyAsync<Xrpl.Client.Exceptions.ValidationException>(() => Validation.ValidateMPTokenIssuanceCreate(tx));
            tx["MutableFlags"] = 0x100u; // outside tmf* mask
            await Assert.ThrowsExactlyAsync<Xrpl.Client.Exceptions.ValidationException>(() => Validation.ValidateMPTokenIssuanceCreate(tx));

            tx["MutableFlags"] = (uint)(MPTokenIssuanceCreateMutableFlags.tmfMPTCanMutateMetadata | MPTokenIssuanceCreateMutableFlags.tmfMPTCanMutateTransferFee);
            await Validation.ValidateMPTokenIssuanceCreate(tx);
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
