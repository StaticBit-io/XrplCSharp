using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec;
using Xrpl.BinaryCodec.Hashing;
using Xrpl.Client.Json;
using Xrpl.Models.Transactions;
using Xrpl.Models.Utils;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// <c>CounterpartySignature</c> (LoanSet) and <c>SponsorSignature</c> (every transaction) are
    /// typed <see cref="SignatureObject"/> members: they survive a model round trip, stay out of
    /// every signing preimage, and keep the Batch inner marker form.
    /// </summary>
    [TestClass]
    public class TestUCoSignatureFields
    {
        private static readonly XrplWallet BrokerOwner = XrplWallet.FromSeed("sEdVJXQmtqNy1pp8uMqsqgxMGL9QdzP");
        private static readonly XrplWallet Borrower = XrplWallet.FromSeed("sEdVYaN7HpU9U7S17zkzPW7pCKqWXzR");
        private static readonly XrplWallet Sponsor = XrplWallet.FromSeed("sEdTTqBarUA64vciRMqd1KwpBguQuXJ");

        private const string BrokerId = "1111111111111111111111111111111111111111111111111111111111111111";

        private static JsonObject SingleSignature() => new JsonObject
        {
            ["SigningPubKey"] = "ED0000000000000000000000000000000000000000000000000000000000000001",
            ["TxnSignature"] = "AABBCC",
        };

        private static JsonObject MultiSignature() => new JsonObject
        {
            ["SigningPubKey"] = "",
            ["Signers"] = new JsonArray
            {
                new JsonObject
                {
                    ["Signer"] = new JsonObject
                    {
                        ["Account"] = "rBTwLga3i2gz3doX6Gva3MgEV8ZCD8jjah",
                        ["SigningPubKey"] = "ED0000000000000000000000000000000000000000000000000000000000000002",
                        ["TxnSignature"] = "DDEEFF",
                    },
                },
            },
        };

        private static JsonObject LoanSetJson(JsonObject counterpartySignature, JsonObject sponsorSignature = null)
        {
            JsonObject tx = new JsonObject
            {
                ["TransactionType"] = "LoanSet",
                ["Account"] = BrokerOwner.ClassicAddress,
                ["LoanBrokerID"] = BrokerId,
                ["Counterparty"] = Borrower.ClassicAddress,
                ["PrincipalRequested"] = "10000000",
                ["Fee"] = "24",
                ["Sequence"] = 5,
                ["SigningPubKey"] = BrokerOwner.PublicKey,
            };
            if (counterpartySignature is not null)
                tx["CounterpartySignature"] = counterpartySignature;
            if (sponsorSignature is not null)
            {
                tx["Sponsor"] = Sponsor.ClassicAddress;
                tx["SponsorFlags"] = 1;
                tx["SponsorSignature"] = sponsorSignature;
            }
            return tx;
        }

        private static JsonObject Reemit(ITransactionCommon tx) => JsonNode.Parse(tx.ToJson())!.AsObject();

        [TestMethod]
        public void TestULoanSet_SingleCounterpartySignature_RoundTrips()
        {
            JsonObject source = LoanSetJson(SingleSignature());

            LoanSet loanSet = (LoanSet)JsonSerializer.Deserialize<TransactionRequest>(source.ToJsonString(), XrplJsonOptions.Default);

            Assert.IsNotNull(loanSet.CounterpartySignature);
            Assert.AreEqual("AABBCC", loanSet.CounterpartySignature.TxnSignature);
            Assert.IsFalse(loanSet.CounterpartySignature.IsMultisig);
            Assert.IsTrue(JsonNode.DeepEquals(source["CounterpartySignature"], Reemit(loanSet)["CounterpartySignature"]));
        }

        [TestMethod]
        public void TestULoanSet_MultisigCounterpartySignature_RoundTrips()
        {
            JsonObject source = LoanSetJson(MultiSignature());

            LoanSet loanSet = (LoanSet)JsonSerializer.Deserialize<TransactionRequest>(source.ToJsonString(), XrplJsonOptions.Default);

            Assert.IsTrue(loanSet.CounterpartySignature.IsMultisig);
            Assert.AreEqual("rBTwLga3i2gz3doX6Gva3MgEV8ZCD8jjah", loanSet.CounterpartySignature.Signers!.Single().Account);
            Assert.IsTrue(JsonNode.DeepEquals(source["CounterpartySignature"], Reemit(loanSet)["CounterpartySignature"]));
        }

        [TestMethod]
        public void TestULoanSetResponse_CoSignatures_AreTyped()
        {
            JsonObject source = LoanSetJson(SingleSignature(), MultiSignature());

            LoanSetResponse response = JsonSerializer.Deserialize<LoanSetResponse>(source.ToJsonString(), XrplJsonOptions.Default);

            Assert.AreEqual("AABBCC", response.CounterpartySignature.TxnSignature);
            Assert.IsTrue(response.SponsorSignature.IsMultisig);
            Assert.IsFalse(response.UnknownFields?.ContainsKey("CounterpartySignature") ?? false, "a modelled field must not land in UnknownFields");
            Assert.IsFalse(response.UnknownFields?.ContainsKey("SponsorSignature") ?? false, "a modelled field must not land in UnknownFields");

            JsonObject reemitted = Reemit(response);
            Assert.IsTrue(JsonNode.DeepEquals(source["CounterpartySignature"], reemitted["CounterpartySignature"]));
            Assert.IsTrue(JsonNode.DeepEquals(source["SponsorSignature"], reemitted["SponsorSignature"]));
        }

        [TestMethod]
        public void TestUPayment_SponsorSignature_RoundTrips()
        {
            JsonObject source = new JsonObject
            {
                ["TransactionType"] = "Payment",
                ["Account"] = BrokerOwner.ClassicAddress,
                ["Destination"] = Borrower.ClassicAddress,
                ["Amount"] = "1000000",
                ["Fee"] = "12",
                ["Sequence"] = 7,
                ["Sponsor"] = Sponsor.ClassicAddress,
                ["SponsorFlags"] = 1,
                ["SponsorSignature"] = SingleSignature(),
            };

            ITransactionRequest request = JsonSerializer.Deserialize<TransactionRequest>(source.ToJsonString(), XrplJsonOptions.Default);
            Assert.IsInstanceOfType(request, typeof(Payment));
            Assert.AreEqual("AABBCC", request.SponsorSignature.TxnSignature);
            Assert.IsTrue(JsonNode.DeepEquals(source["SponsorSignature"], Reemit(request)["SponsorSignature"]));

            TransactionResponse response = JsonSerializer.Deserialize<PaymentResponse>(source.ToJsonString(), XrplJsonOptions.Default);
            Assert.IsTrue(JsonNode.DeepEquals(source["SponsorSignature"], Reemit(response)["SponsorSignature"]));
        }

        [TestMethod]
        public void TestUAbsentCoSignatures_AreNotWritten()
        {
            LoanSet loanSet = new LoanSet
            {
                Account = BrokerOwner.ClassicAddress,
                LoanBrokerID = BrokerId,
                PrincipalRequested = 10000000,
            };

            JsonObject reemitted = Reemit(loanSet);
            Assert.IsFalse(reemitted.ContainsKey("CounterpartySignature"));
            Assert.IsFalse(reemitted.ContainsKey("SponsorSignature"));
        }

        [TestMethod]
        [DataRow("TransactionSig")]
        [DataRow("CounterpartyTransactionSig")]
        [DataRow("SponsorTransactionSig")]
        public void TestUSigningPreimage_IgnoresCoSignatures(string prefixName)
        {
            HashPrefix prefix = (HashPrefix)typeof(HashPrefix).GetField(prefixName)!.GetValue(null)!;

            LoanSet without = (LoanSet)JsonSerializer.Deserialize<TransactionRequest>(LoanSetJson(null).ToJsonString(), XrplJsonOptions.Default);
            without.Sponsor = Sponsor.ClassicAddress;
            without.SponsorFlags = (global::Xrpl.Models.Transactions.SponsorCoverage)1;
            LoanSet with = (LoanSet)JsonSerializer.Deserialize<TransactionRequest>(LoanSetJson(null).ToJsonString(), XrplJsonOptions.Default);
            with.Sponsor = Sponsor.ClassicAddress;
            with.SponsorFlags = (global::Xrpl.Models.Transactions.SponsorCoverage)1;
            with.CounterpartySignature = SignatureObject.FromJsonObject(SingleSignature());
            with.SponsorSignature = SignatureObject.FromJsonObject(MultiSignature());

            string withoutHex = XrplBinaryCodec.EncodeForSigning(without.ToDictionary(), prefix);
            string withHex = XrplBinaryCodec.EncodeForSigning(with.ToDictionary(), prefix);

            Assert.AreEqual(withoutHex, withHex, "a co-signature is not a signing field and must not change what anyone signs");
            Assert.AreNotEqual(XrplBinaryCodec.Encode(without.ToDictionary()), XrplBinaryCodec.Encode(with.ToDictionary()),
                "the co-signatures are still part of the encoded transaction");
        }

        [TestMethod]
        public void TestUGetTx_KeepsCounterpartySignature_SubmitterSignsTypedTransaction()
        {
            JsonObject prepared = LoanSigningHelper.PrepareForSigning(LoanSetJson(null), BrokerOwner);
            SignatureResult withCounterparty = Borrower.SignAsLoanCounterparty(
                JsonSerializer.Deserialize<Dictionary<string, object>>(prepared.ToJsonString(), XrplJsonOptions.Default));

            LoanSet typed = (LoanSet)withCounterparty.GetTx();
            Assert.AreEqual(Borrower.PublicKey, typed.CounterpartySignature.SigningPubKey);

            SignatureResult viaModel = BrokerOwner.Sign(typed.ToDictionary());
            SignatureResult viaBlob = LoanSigningHelper.SubmitterSign(withCounterparty.TxBlob, BrokerOwner);

            Assert.AreEqual(viaBlob.TxBlob, viaModel.TxBlob, "signing the typed transaction must keep the co-signature and match the blob-level flow");
        }

        [TestMethod]
        public void TestUBatch_InnerCoSignatureMarkers_RoundTripAndRouteSigners()
        {
            JsonObject sponsoredInner = new JsonObject
            {
                ["TransactionType"] = "Payment",
                ["Flags"] = 0x40000000u,
                ["Account"] = BrokerOwner.ClassicAddress,
                ["Destination"] = Borrower.ClassicAddress,
                ["Amount"] = "1000000",
                ["Fee"] = "0",
                ["Sequence"] = 0u,
                ["SigningPubKey"] = "",
                ["Sponsor"] = Sponsor.ClassicAddress,
                ["SponsorFlags"] = 2u,
                ["SponsorSignature"] = new JsonObject(),
            };
            JsonObject loanInner = new JsonObject
            {
                ["TransactionType"] = "LoanSet",
                ["Flags"] = 0x40000000u,
                ["Account"] = BrokerOwner.ClassicAddress,
                ["LoanBrokerID"] = BrokerId,
                ["Counterparty"] = Borrower.ClassicAddress,
                ["PrincipalRequested"] = "10000000",
                ["Fee"] = "0",
                ["Sequence"] = 0u,
                ["SigningPubKey"] = "",
                ["CounterpartySignature"] = new JsonObject { ["SigningPubKey"] = "" },
            };
            JsonObject batch = new JsonObject
            {
                ["TransactionType"] = "Batch",
                ["Account"] = BrokerOwner.ClassicAddress,
                ["Flags"] = 0x00010000u,
                ["Fee"] = "100",
                ["Sequence"] = 5u,
                ["RawTransactions"] = new JsonArray
                {
                    new JsonObject { ["RawTransaction"] = sponsoredInner },
                    new JsonObject { ["RawTransaction"] = loanInner },
                },
            };

            Batch typed = (Batch)JsonSerializer.Deserialize<TransactionRequest>(batch.ToJsonString(), XrplJsonOptions.Default);
            Assert.IsNotNull(typed.RawTransactions[0].RawTransaction.SponsorSignature, "the sponsor marker must be kept");

            JsonObject reemitted = Reemit(typed);
            JsonArray inners = reemitted["RawTransactions"]!.AsArray();
            Assert.IsTrue(JsonNode.DeepEquals(new JsonObject(), inners[0]!["RawTransaction"]!["SponsorSignature"]), "an empty marker must write back empty");
            Assert.IsTrue(JsonNode.DeepEquals(new JsonObject { ["SigningPubKey"] = "" }, inners[1]!["RawTransaction"]!["CounterpartySignature"]));

            // Not ValidateBatch: it refuses a LoanSet inner, which rippled allows only once LendingProtocolV1_2 is enabled
            Dictionary<string, object> dict = typed.ToDictionary();
            var accounts = dict.GetBatchSignerAccounts();
            CollectionAssert.Contains(accounts.Raw, Sponsor.ClassicAddress, "the marker still makes the sponsor a batch signer");
            CollectionAssert.Contains(accounts.Raw, Borrower.ClassicAddress, "the inner counterparty is a batch signer");
        }
    }
}
