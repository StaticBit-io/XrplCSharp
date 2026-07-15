using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec;
using Xrpl.Client.Json;
using Xrpl.Models.Transactions;
using Xrpl.Models.Utils;
using Xrpl.Wallet;

namespace Xrpl.Tests.Wallet.Tests
{
    /// <summary>
    /// Batch × co-signing interplay (#43/#47): required batch signers now
    /// include the inner initiator (Delegate-aware), the inner Counterparty
    /// and the inner Sponsor carrying a SponsorSignature marker — mirroring
    /// rippled Batch::preflight requiredSigners; plus the outer-batch sponsor
    /// routing and the new client-side validation rules.
    /// </summary>
    [TestClass]
    public class TestUBatchCoSigning
    {
        private const string SubmitterSeed = "sEdVJXQmtqNy1pp8uMqsqgxMGL9QdzP";
        private const string SponsorSeed = "sEdTTqBarUA64vciRMqd1KwpBguQuXJ";
        private const string DestinationSeed = "sEdVPTJ6emfG3hCFdubKMpaskvkWLrT";
        private const string OtherSeed = "sEdVUGxDJ7sqTupycsVNowrQMeJn7UP";
        private const string CounterpartySeed = "sEdVYaN7HpU9U7S17zkzPW7pCKqWXzR";

        private static XrplWallet Root => XrplWallet.FromSeed(SubmitterSeed);
        private static XrplWallet Sponsor => XrplWallet.FromSeed(SponsorSeed);
        private static XrplWallet Destination => XrplWallet.FromSeed(DestinationSeed);
        private static XrplWallet Other => XrplWallet.FromSeed(OtherSeed);
        private static XrplWallet Counterparty => XrplWallet.FromSeed(CounterpartySeed);

        private static Dictionary<string, object> ToDict(JsonObject json) =>
            JsonSerializer.Deserialize<Dictionary<string, object>>(json.ToJsonString(), XrplJsonOptions.Default);

        private static JsonObject InnerPayment(string account, JsonObject? extra = null)
        {
            JsonObject inner = new JsonObject
            {
                ["TransactionType"] = "Payment",
                ["Flags"] = 0x40000000u, // tfInnerBatchTxn
                ["Account"] = account,
                ["Destination"] = Destination.ClassicAddress,
                ["Amount"] = "1000000",
                ["Fee"] = "0",
                ["Sequence"] = 0u,
                ["SigningPubKey"] = "",
            };
            if (extra is not null)
            {
                foreach (var kv in extra)
                    inner[kv.Key] = kv.Value?.DeepClone();
            }
            return new JsonObject { ["RawTransaction"] = inner };
        }

        private static JsonObject OuterBatch(params JsonObject[] inners)
        {
            JsonArray raw = new JsonArray();
            foreach (JsonObject inner in inners)
                raw.Add(inner.DeepClone());
            return new JsonObject
            {
                ["TransactionType"] = "Batch",
                ["Account"] = Root.ClassicAddress,
                ["Flags"] = 0x00010000u, // tfAllOrNothing
                ["Fee"] = "100",
                ["Sequence"] = 5u,
                ["LastLedgerSequence"] = 8000010u,
                ["RawTransactions"] = raw,
                ["SigningPubKey"] = Root.PublicKey,
            };
        }

        [TestMethod]
        public void TestURequiredSigners_CollectDelegateCounterpartySponsorMarker()
        {
            JsonObject batch = OuterBatch(
                // delegated inner: the Delegate is the required signer, not the Account
                InnerPayment(Destination.ClassicAddress, new JsonObject { ["Delegate"] = Other.ClassicAddress }),
                // LoanSet-style inner: the Counterparty is required too
                InnerPayment(Root.ClassicAddress, new JsonObject { ["Counterparty"] = Counterparty.ClassicAddress }),
                // sponsored inner WITH the marker: the sponsor is required
                InnerPayment(Root.ClassicAddress, new JsonObject
                {
                    ["Sponsor"] = Sponsor.ClassicAddress,
                    ["SponsorFlags"] = 2u,
                    ["SponsorSignature"] = new JsonObject(),
                    ["Amount"] = "2000000",
                }));

            var accounts = ToDict(batch).GetBatchSignerAccounts();

            CollectionAssert.Contains(accounts.Raw, Other.ClassicAddress, "the Delegate must be required");
            CollectionAssert.DoesNotContain(accounts.Raw, Destination.ClassicAddress, "a delegated inner's Account is not the initiator");
            CollectionAssert.Contains(accounts.Raw, Counterparty.ClassicAddress, "the inner Counterparty must be required");
            CollectionAssert.Contains(accounts.Raw, Sponsor.ClassicAddress, "the Sponsor with a marker must be required");
        }

        [TestMethod]
        public void TestURequiredSigners_SponsorWithoutMarkerIsNotRequired()
        {
            JsonObject batch = OuterBatch(
                InnerPayment(Other.ClassicAddress, new JsonObject
                {
                    ["Sponsor"] = Sponsor.ClassicAddress,
                    ["SponsorFlags"] = 2u,
                    // no SponsorSignature marker: relationship without require-sign
                }));

            var accounts = ToDict(batch).GetBatchSignerAccounts();

            CollectionAssert.Contains(accounts.Raw, Other.ClassicAddress);
            CollectionAssert.DoesNotContain(accounts.Raw, Sponsor.ClassicAddress,
                "without the SponsorSignature marker the sponsor is not a required batch signer");
        }

        [TestMethod]
        public void TestUSign_OuterBatchSponsor_RoutesToSponsorSignature()
        {
            JsonObject batch = OuterBatch(
                InnerPayment(Other.ClassicAddress),
                InnerPayment(Root.ClassicAddress, new JsonObject { ["Amount"] = "3000000" }));
            batch["Sponsor"] = Sponsor.ClassicAddress;
            batch["SponsorFlags"] = 1u; // spfSponsorFee

            var viaUnified = Sponsor.Sign(ToDict(batch));
            var viaExplicit = Sponsor.SignAsSponsor(ToDict(batch));
            Assert.AreEqual(viaExplicit.TxBlob, viaUnified.TxBlob);

            JsonObject decoded = XrplBinaryCodec.Decode(viaUnified.TxBlob).AsObject();
            Assert.IsNotNull(decoded["SponsorSignature"]);
            Assert.IsNull(decoded["TxnSignature"]);
        }

        [TestMethod]
        public async Task TestUValidateBatch_OuterReserveSponsorship_Throws()
        {
            Dictionary<string, object> batch = ToDict(OuterBatch(
                InnerPayment(Other.ClassicAddress),
                InnerPayment(Root.ClassicAddress, new JsonObject { ["Amount"] = "3000000" })));
            batch["Sponsor"] = Sponsor.ClassicAddress;
            batch["SponsorFlags"] = 2u; // spfSponsorReserve — forbidden on outer

            var ex = await Assert.ThrowsExactlyAsync<System.ArgumentException>(() => Validation.ValidateBatch(batch));
            StringAssert.Contains(ex.Message, "spfSponsorReserve");
        }

        [TestMethod]
        public async Task TestUValidateBatch_InnerFeeSponsorship_Throws()
        {
            Dictionary<string, object> batch = ToDict(OuterBatch(
                InnerPayment(Other.ClassicAddress, new JsonObject
                {
                    ["Sponsor"] = Sponsor.ClassicAddress,
                    ["SponsorFlags"] = 1u, // spfSponsorFee — forbidden on inner
                }),
                InnerPayment(Root.ClassicAddress, new JsonObject { ["Amount"] = "3000000" })));

            var ex = await Assert.ThrowsExactlyAsync<System.ArgumentException>(() => Validation.ValidateBatch(batch));
            StringAssert.Contains(ex.Message, "spfSponsorFee");
        }

        [TestMethod]
        public async Task TestUValidateBatch_MarkerWithSignatureMaterial_Throws()
        {
            Dictionary<string, object> batch = ToDict(OuterBatch(
                InnerPayment(Other.ClassicAddress, new JsonObject
                {
                    ["Sponsor"] = Sponsor.ClassicAddress,
                    ["SponsorFlags"] = 2u,
                    ["SponsorSignature"] = new JsonObject
                    {
                        ["SigningPubKey"] = Sponsor.PublicKey,
                        ["TxnSignature"] = "DEADBEEF",
                    },
                }),
                InnerPayment(Root.ClassicAddress, new JsonObject { ["Amount"] = "3000000" })));

            var ex = await Assert.ThrowsExactlyAsync<System.ArgumentException>(() => Validation.ValidateBatch(batch));
            StringAssert.Contains(ex.Message, "SponsorSignature");
        }
    }
}
