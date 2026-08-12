using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

namespace Xrpl.Tests.Models.Tests
{
    /// <summary>
    /// Binary round-trip and validation tests for ConfidentialTransfer transactions
    /// and XLS-68 sponsorship validation rules.
    /// </summary>
    [TestClass]
    public class TestUConfidentialMPT
    {
        private const string IssuanceId = "00000001A407AF5856CCF3C42619DAA925813FC955C72983"; // Hash192
        private const string Hash256Hex = "1D5A924A24F9AF5F1BF2AD9F4E385E4C0F5B85B94E4C4A1D8B6E1C0B7F3D2E1A";
        private const string BlobHex = "DEADBEEF00112233445566778899AABBCCDDEEFF";

        private static string Account1;
        private static string Account2;

        [ClassInitialize]
        public static void Init(TestContext _)
        {
            Account1 = XrplWallet.Generate().ClassicAddress;
            Account2 = XrplWallet.Generate().ClassicAddress;
        }

        private static JsonObject RoundTrip(TransactionRequest tx)
        {
            JsonObject json = JsonNode.Parse(tx.ToJson())!.AsObject();
            json["Sequence"] = 1u;
            json["Fee"] = "12";
            json["SigningPubKey"] = "";
            string blob = XrplBinaryCodec.Encode(json);
            return XrplBinaryCodec.Decode(blob).AsObject();
        }

        [TestMethod]
        public void TestUConfidentialMPTConvert_BinaryRoundTrip()
        {
            var tx = new ConfidentialMPTConvert
            {
                Account = Account1,
                MPTokenIssuanceID = IssuanceId,
                MPTAmount = "1000000",
                HolderEncryptionKey = BlobHex,
                HolderEncryptedAmount = BlobHex,
                IssuerEncryptedAmount = BlobHex,
                BlindingFactor = Hash256Hex,
                ZKProof = BlobHex,
            };
            JsonObject decoded = RoundTrip(tx);
            Assert.AreEqual("ConfidentialMPTConvert", decoded["TransactionType"]!.GetValue<string>());
            Assert.AreEqual(IssuanceId, decoded["MPTokenIssuanceID"]!.GetValue<string>());
            Assert.AreEqual("1000000", decoded["MPTAmount"]!.GetValue<string>());
            Assert.AreEqual(BlobHex, decoded["HolderEncryptedAmount"]!.GetValue<string>());
            Assert.AreEqual(Hash256Hex, decoded["BlindingFactor"]!.GetValue<string>());
        }

        [TestMethod]
        public void TestUConfidentialMPTMergeInbox_BinaryRoundTrip()
        {
            var tx = new ConfidentialMPTMergeInbox
            {
                Account = Account1,
                MPTokenIssuanceID = IssuanceId,
            };
            JsonObject decoded = RoundTrip(tx);
            Assert.AreEqual("ConfidentialMPTMergeInbox", decoded["TransactionType"]!.GetValue<string>());
            Assert.AreEqual(IssuanceId, decoded["MPTokenIssuanceID"]!.GetValue<string>());
        }

        [TestMethod]
        public void TestUConfidentialMPTConvertBack_BinaryRoundTrip()
        {
            var tx = new ConfidentialMPTConvertBack
            {
                Account = Account1,
                MPTokenIssuanceID = IssuanceId,
                MPTAmount = "500",
                HolderEncryptedAmount = BlobHex,
                IssuerEncryptedAmount = BlobHex,
                BlindingFactor = Hash256Hex,
                ZKProof = BlobHex,
                BalanceCommitment = BlobHex,
            };
            JsonObject decoded = RoundTrip(tx);
            Assert.AreEqual("ConfidentialMPTConvertBack", decoded["TransactionType"]!.GetValue<string>());
            Assert.AreEqual(BlobHex, decoded["BalanceCommitment"]!.GetValue<string>());
        }

        [TestMethod]
        public void TestUConfidentialMPTSend_BinaryRoundTrip()
        {
            var tx = new ConfidentialMPTSend
            {
                Account = Account1,
                MPTokenIssuanceID = IssuanceId,
                Destination = Account2,
                DestinationTag = 7,
                SenderEncryptedAmount = BlobHex,
                DestinationEncryptedAmount = BlobHex,
                IssuerEncryptedAmount = BlobHex,
                ZKProof = BlobHex,
                AmountCommitment = BlobHex,
                BalanceCommitment = BlobHex,
            };
            JsonObject decoded = RoundTrip(tx);
            Assert.AreEqual("ConfidentialMPTSend", decoded["TransactionType"]!.GetValue<string>());
            Assert.AreEqual(Account2, decoded["Destination"]!.GetValue<string>());
            Assert.AreEqual(7u, decoded["DestinationTag"]!.GetValue<uint>());
            Assert.AreEqual(BlobHex, decoded["AmountCommitment"]!.GetValue<string>());
        }

        [TestMethod]
        public void TestUConfidentialMPTClawback_BinaryRoundTrip()
        {
            var tx = new ConfidentialMPTClawback
            {
                Account = Account1,
                MPTokenIssuanceID = IssuanceId,
                Holder = Account2,
                MPTAmount = "42",
                ZKProof = BlobHex,
            };
            JsonObject decoded = RoundTrip(tx);
            Assert.AreEqual("ConfidentialMPTClawback", decoded["TransactionType"]!.GetValue<string>());
            Assert.AreEqual(Account2, decoded["Holder"]!.GetValue<string>());
        }

        [TestMethod]
        public void TestUSponsorship_BinaryRoundTrip()
        {
            var tx = new SponsorshipSet
            {
                Account = Account1,
                Sponsee = Account2,
                FeeAmountDelta = new global::Xrpl.Models.Common.Currency { ValueAsXrp = 5m },
                RemainingOwnerCountDelta = 3,
                Flags = SponsorshipSetFlags.tfSponsorshipSetRequireSignForFee,
            };
            JsonObject decoded = RoundTrip(tx);
            Assert.AreEqual("SponsorshipSet", decoded["TransactionType"]!.GetValue<string>());
            Assert.AreEqual(Account2, decoded["Sponsee"]!.GetValue<string>());
            Assert.AreEqual(3, decoded["RemainingOwnerCountDelta"]!.GetValue<int>());
            Assert.AreEqual((uint)SponsorshipSetFlags.tfSponsorshipSetRequireSignForFee, decoded["Flags"]!.GetValue<uint>());
        }

        #region Validation

        private static Dictionary<string, object> BaseTx(string type) => new()
        {
            ["TransactionType"] = type,
            ["Account"] = Account1,
        };

        [TestMethod]
        public async Task TestUValidateSponsorshipSet_ConflictingFlags_Throws()
        {
            Dictionary<string, object> tx = BaseTx("SponsorshipSet");
            tx["Sponsee"] = Account2;
            tx["Flags"] = (uint)(SponsorshipSetFlags.tfSponsorshipSetRequireSignForFee | SponsorshipSetFlags.tfSponsorshipClearRequireSignForFee);
            await Assert.ThrowsExactlyAsync<ValidationException>(() => Validation.ValidateSponsorshipSet(tx));
        }

        [TestMethod]
        public async Task TestUValidateSponsorshipTransfer_ModeRules()
        {
            // no mode flag
            Dictionary<string, object> tx = BaseTx("SponsorshipTransfer");
            await Assert.ThrowsExactlyAsync<ValidationException>(() => Validation.ValidateSponsorshipTransfer(tx));

            // create without Sponsor
            tx = BaseTx("SponsorshipTransfer");
            tx["Flags"] = (uint)SponsorshipTransferFlags.tfSponsorshipCreate;
            await Assert.ThrowsExactlyAsync<ValidationException>(() => Validation.ValidateSponsorshipTransfer(tx));

            // create with Sponsor — valid
            tx["Sponsor"] = Account2;
            await Validation.ValidateSponsorshipTransfer(tx);

            // create with Sponsee — invalid
            tx["Sponsee"] = Account2;
            await Assert.ThrowsExactlyAsync<ValidationException>(() => Validation.ValidateSponsorshipTransfer(tx));

            // end with Sponsor — invalid
            tx = BaseTx("SponsorshipTransfer");
            tx["Flags"] = (uint)SponsorshipTransferFlags.tfSponsorshipEnd;
            tx["Sponsor"] = Account2;
            await Assert.ThrowsExactlyAsync<ValidationException>(() => Validation.ValidateSponsorshipTransfer(tx));

            // end with neither field — valid (account-level self-sponsorship end)
            tx = BaseTx("SponsorshipTransfer");
            tx["Flags"] = (uint)SponsorshipTransferFlags.tfSponsorshipEnd;
            await Validation.ValidateSponsorshipTransfer(tx);

            // end with Sponsee == Account — invalid
            tx["Sponsee"] = Account1;
            await Assert.ThrowsExactlyAsync<ValidationException>(() => Validation.ValidateSponsorshipTransfer(tx));
        }

        #endregion
    }
}
