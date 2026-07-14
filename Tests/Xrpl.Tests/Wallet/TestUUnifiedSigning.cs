using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Wallet;

namespace Xrpl.Tests.Wallet.Tests
{
    /// <summary>
    /// Unified signing entry point (#43): the standard Sign routes by role for
    /// sponsored transactions — sponsor wallets produce SponsorSignature,
    /// submitter wallets produce the main signature preserving an existing
    /// SponsorSignature. Outputs must be byte-identical to the explicit
    /// V1/V2/V3 helper flows (see TestUSigningPinned).
    /// </summary>
    [TestClass]
    public class TestUUnifiedSigning
    {
        private const string SubmitterSeed = "sEdVJXQmtqNy1pp8uMqsqgxMGL9QdzP";
        private const string SponsorSeed = "sEdTTqBarUA64vciRMqd1KwpBguQuXJ";
        private const string DestinationSeed = "sEdVPTJ6emfG3hCFdubKMpaskvkWLrT";

        private static XrplWallet Submitter => XrplWallet.FromSeed(SubmitterSeed);
        private static XrplWallet Sponsor => XrplWallet.FromSeed(SponsorSeed);
        private static XrplWallet Destination => XrplWallet.FromSeed(DestinationSeed);

        private static Dictionary<string, object> PreparedSponsoredTx() =>
            JsonSerializer.Deserialize<Dictionary<string, object>>(new JsonObject
            {
                ["TransactionType"] = "Payment",
                ["Account"] = Submitter.ClassicAddress,
                ["Destination"] = Destination.ClassicAddress,
                ["Amount"] = "1000000",
                ["Fee"] = "12",
                ["Sequence"] = 7u,
                ["LastLedgerSequence"] = 8000000u,
                ["Sponsor"] = Sponsor.ClassicAddress,
                ["SponsorFlags"] = 1u,
                ["SigningPubKey"] = Submitter.PublicKey,
            }.ToJsonString(), XrplJsonOptions.Default);

        [TestMethod]
        public void TestUSign_SponsorWallet_RoutesToSponsorSignature()
        {
            // Standard Sign, no helper choice: the wallet matching tx.Sponsor
            // must produce the same partial blob as the explicit SignAsSponsor
            var viaUnified = Sponsor.Sign(PreparedSponsoredTx());
            var viaExplicit = Sponsor.SignAsSponsor(PreparedSponsoredTx());
            Assert.AreEqual(viaExplicit.TxBlob, viaUnified.TxBlob);

            JsonObject decoded = XrplBinaryCodec.Decode(viaUnified.TxBlob).AsObject();
            Assert.IsNotNull(decoded["SponsorSignature"], "sponsor path must add SponsorSignature");
            Assert.IsNull(decoded["TxnSignature"], "sponsor path must not add the main signature");
        }

        [TestMethod]
        public void TestUSign_SequentialFlow_MatchesPinnedBlob()
        {
            // Full V3 through the standard Sign on both sides
            var sponsorPart = Sponsor.Sign(PreparedSponsoredTx());

            Dictionary<string, object> handedOver = JsonSerializer.Deserialize<Dictionary<string, object>>(
                XrplBinaryCodec.Decode(sponsorPart.TxBlob).ToJsonString(), XrplJsonOptions.Default);
            var final = Submitter.Sign(handedOver);

            // Byte-identical to the pinned explicit-helper output
            var pinned = SponsorSigningHelper.SubmitterSign(
                Sponsor.SignAsSponsor(PreparedSponsoredTx()).TxBlob, Submitter);
            Assert.AreEqual(pinned.TxBlob, final.TxBlob);

            JsonObject decoded = XrplBinaryCodec.Decode(final.TxBlob).AsObject();
            Assert.IsNotNull(decoded["TxnSignature"]);
            Assert.IsNotNull(decoded["SponsorSignature"]);
        }

        [TestMethod]
        public void TestUSign_SubmitterMismatchedPubKey_Throws()
        {
            var sponsorPart = Sponsor.Sign(PreparedSponsoredTx());
            Dictionary<string, object> handedOver = JsonSerializer.Deserialize<Dictionary<string, object>>(
                XrplBinaryCodec.Decode(sponsorPart.TxBlob).ToJsonString(), XrplJsonOptions.Default);

            // Destination is neither the submitter the sponsor co-signed for nor the sponsor
            Assert.ThrowsExactly<ValidationException>(() => Destination.Sign(handedOver));
        }

        [TestMethod]
        public void TestUSign_MultisignBypassesSponsorRouting()
        {
            // multisign: true must keep producing a portable Signer entry even
            // when the wallet address equals tx.Sponsor (composition decides the section)
            Dictionary<string, object> tx = PreparedSponsoredTx();
            tx["SigningPubKey"] = "";
            var result = Sponsor.Sign(tx, multisign: true);

            JsonObject decoded = XrplBinaryCodec.Decode(result.TxBlob).AsObject();
            Assert.IsNotNull(decoded["Signers"], "multisign must produce a Signer entry");
            Assert.IsNull(decoded["SponsorSignature"], "multisign must not route to the sponsor path");
        }
    }
}
