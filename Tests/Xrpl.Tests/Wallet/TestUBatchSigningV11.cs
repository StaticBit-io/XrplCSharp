using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.AddressCodec;
using Xrpl.BinaryCodec;
using Xrpl.Client.Json;
using Xrpl.Keypairs;
using Xrpl.Models.Utils;
using Xrpl.Wallet;

namespace Xrpl.Tests.Wallet.Tests
{
    /// <summary>
    /// Unit tests for Batch (XLS-56) signing under the BatchV1_1 amendment rules:
    /// preimage includes outer Account + Sequence, single-sig appends the BatchSigner
    /// account id, inner multisign appends owner + signer account ids.
    /// </summary>
    [TestClass]
    public class TestUBatchSigningV11
    {
        private const uint TfAllOrNothing = 0x00010000;
        private const uint TfInnerBatchTxn = 0x40000000;

        private static JsonObject InnerPayment(string account, string destination, uint sequence) => new JsonObject
        {
            ["TransactionType"] = "Payment",
            ["Account"] = account,
            ["Destination"] = destination,
            ["Amount"] = "1000000",
            ["Sequence"] = sequence,
            ["Fee"] = "0",
            ["SigningPubKey"] = "",
            ["Flags"] = TfInnerBatchTxn
        };

        // An inner transaction as a caller may well hand it over: none of the three
        // fields normalisation is responsible for is present.
        private static JsonObject UnnormalizedInnerPayment(string account, string destination, uint sequence) => new JsonObject
        {
            ["TransactionType"] = "Payment",
            ["Account"] = account,
            ["Destination"] = destination,
            ["Amount"] = "1000000",
            ["Sequence"] = sequence
        };

        private static Dictionary<string, object> BuildBatchDictionary(JsonObject outer) =>
            JsonSerializer.Deserialize<Dictionary<string, object>>(outer.ToJsonString(), XrplJsonOptions.Default)
                ?? throw new InvalidOperationException("Failed to build tx dictionary.");

        private static JsonObject BuildOuterBatch(string outerAccount, uint outerSequence, params JsonObject[] inners)
        {
            var rawTransactions = new JsonArray();
            foreach (JsonObject inner in inners)
                rawTransactions.Add(new JsonObject { ["RawTransaction"] = inner.DeepClone() });

            return new JsonObject
            {
                ["TransactionType"] = "Batch",
                ["Account"] = outerAccount,
                ["Sequence"] = outerSequence,
                ["Flags"] = TfAllOrNothing,
                ["Fee"] = "40",
                ["RawTransactions"] = rawTransactions
            };
        }

        private static byte[] ComputeBasePreimage(string outerAccount, uint outerSequence, uint flags, params JsonObject[] inners)
        {
            var txIds = new List<string>(inners.Length);
            foreach (JsonObject inner in inners)
            {
                JsonObject normalized = inner.NormalizeInnerTransaction();
                txIds.Add(normalized.ComputeInnerTxId());
            }
            return XrplBinaryCodec.EncodeForSigningBatch(outerAccount, outerSequence, flags, txIds);
        }

        private static byte[] Concat(params byte[][] parts)
        {
            int total = 0;
            foreach (byte[] part in parts) total += part.Length;
            byte[] result = new byte[total];
            int offset = 0;
            foreach (byte[] part in parts)
            {
                Buffer.BlockCopy(part, 0, result, offset, part.Length);
                offset += part.Length;
            }
            return result;
        }

        [TestMethod]
        public void TestUSignAsBatchPart_SingleSig_SignatureBindsSignerAccount()
        {
            XrplWallet submitter = XrplWallet.Generate();
            XrplWallet participant = XrplWallet.Generate();
            XrplWallet destination = XrplWallet.Generate();

            JsonObject inner1 = InnerPayment(submitter.ClassicAddress, destination.ClassicAddress, 4);
            JsonObject inner2 = InnerPayment(participant.ClassicAddress, destination.ClassicAddress, 20);
            JsonObject outer = BuildOuterBatch(submitter.ClassicAddress, 3, inner1, inner2);

            SignatureResult result = participant.SignAsBatchPart(BuildBatchDictionary(outer), multisign: false, signingFor: participant.ClassicAddress);

            JsonNode decoded = XrplBinaryCodec.Decode(result.TxBlob);
            JsonArray batchSigners = decoded["BatchSigners"]?.AsArray()
                ?? throw new AssertFailedException("BatchSigners missing in signed tx.");
            Assert.AreEqual(1, batchSigners.Count);

            JsonObject signer = batchSigners[0]!["BatchSigner"]!.AsObject();
            Assert.AreEqual(participant.ClassicAddress, signer["Account"]?.GetValue<string>());
            Assert.AreEqual(participant.PublicKey, signer["SigningPubKey"]?.GetValue<string>());

            string signature = signer["TxnSignature"]?.GetValue<string>()
                ?? throw new AssertFailedException("TxnSignature missing.");

            // BatchV1_1: подпись строится над preimage + AccountID подписанта (finishMultiSigningData)
            byte[] basePreimage = ComputeBasePreimage(submitter.ClassicAddress, 3, TfAllOrNothing, inner1, inner2);
            byte[] signerAccountId = XrplCodec.DecodeAccountID(participant.ClassicAddress);
            byte[] expectedSignedData = Concat(basePreimage, signerAccountId);

            Assert.IsTrue(XrplKeypairs.Verify(expectedSignedData, signature, participant.PublicKey),
                "Signature must verify over preimage + BatchSigner account id.");

            // Старый (до-V1_1) вариант без суффикса аккаунта не должен проходить проверку
            Assert.IsFalse(XrplKeypairs.Verify(basePreimage, signature, participant.PublicKey),
                "Signature must NOT verify over the bare preimage (pre-V1_1 format).");
        }

        [TestMethod]
        public void TestUSignAsBatchPart_MultiSig_SignatureBindsOwnerAndSigner()
        {
            XrplWallet submitter = XrplWallet.Generate();
            XrplWallet owner = XrplWallet.Generate();
            XrplWallet cosigner = XrplWallet.Generate();
            XrplWallet destination = XrplWallet.Generate();

            JsonObject inner1 = InnerPayment(submitter.ClassicAddress, destination.ClassicAddress, 4);
            JsonObject inner2 = InnerPayment(owner.ClassicAddress, destination.ClassicAddress, 20);
            JsonObject outer = BuildOuterBatch(submitter.ClassicAddress, 3, inner1, inner2);

            SignatureResult result = cosigner.SignAsBatchPart(BuildBatchDictionary(outer), multisign: true, signingFor: owner.ClassicAddress);

            JsonNode decoded = XrplBinaryCodec.Decode(result.TxBlob);
            JsonArray batchSigners = decoded["BatchSigners"]?.AsArray()
                ?? throw new AssertFailedException("BatchSigners missing in signed tx.");
            Assert.AreEqual(1, batchSigners.Count);

            JsonObject signer = batchSigners[0]!["BatchSigner"]!.AsObject();
            Assert.AreEqual(owner.ClassicAddress, signer["Account"]?.GetValue<string>());

            JsonArray signers = signer["Signers"]?.AsArray()
                ?? throw new AssertFailedException("BatchSigner.Signers missing.");
            Assert.AreEqual(1, signers.Count);

            JsonObject signerEntry = signers[0]!["Signer"]!.AsObject();
            Assert.AreEqual(cosigner.ClassicAddress, signerEntry["Account"]?.GetValue<string>());

            string signature = signerEntry["TxnSignature"]?.GetValue<string>()
                ?? throw new AssertFailedException("Signer.TxnSignature missing.");

            // BatchV1_1: data = preimage + owner(20) + signer(20)
            byte[] basePreimage = ComputeBasePreimage(submitter.ClassicAddress, 3, TfAllOrNothing, inner1, inner2);
            byte[] ownerAccountId = XrplCodec.DecodeAccountID(owner.ClassicAddress);
            byte[] cosignerAccountId = XrplCodec.DecodeAccountID(cosigner.ClassicAddress);
            byte[] expectedSignedData = Concat(basePreimage, ownerAccountId, cosignerAccountId);

            Assert.IsTrue(XrplKeypairs.Verify(expectedSignedData, signature, cosigner.PublicKey),
                "Signature must verify over preimage + owner + signer account ids.");

            // Формат без owner-аккаунта (до-V1_1) не должен проходить проверку
            byte[] preV11Data = Concat(basePreimage, cosignerAccountId);
            Assert.IsFalse(XrplKeypairs.Verify(preV11Data, signature, cosigner.PublicKey),
                "Signature must NOT verify over preimage + signer id only (pre-V1_1 format).");
        }

        [TestMethod]
        public void TestUSignAsBatchPart_TicketSequence_SerializesSequenceZero()
        {
            XrplWallet submitter = XrplWallet.Generate();
            XrplWallet participant = XrplWallet.Generate();
            XrplWallet destination = XrplWallet.Generate();

            JsonObject inner1 = InnerPayment(submitter.ClassicAddress, destination.ClassicAddress, 4);
            JsonObject inner2 = InnerPayment(participant.ClassicAddress, destination.ClassicAddress, 20);
            JsonObject outer = BuildOuterBatch(submitter.ClassicAddress, 0, inner1, inner2);
            outer.Remove("Sequence");
            outer["TicketSequence"] = 15;

            SignatureResult result = participant.SignAsBatchPart(BuildBatchDictionary(outer), multisign: false, signingFor: participant.ClassicAddress);

            // The serialized blob must carry the explicit Sequence: 0 required for ticket transactions
            JsonNode decoded = XrplBinaryCodec.Decode(result.TxBlob);
            Assert.AreEqual(0u, decoded["Sequence"]?.GetValue<uint>(),
                "Serialized Batch must contain Sequence = 0 when TicketSequence is used.");
            Assert.AreEqual(15u, decoded["TicketSequence"]?.GetValue<uint>());

            // Signature must verify over the preimage built with outerSequence = 0
            JsonObject signer = decoded["BatchSigners"]!.AsArray()[0]!["BatchSigner"]!.AsObject();
            string signature = signer["TxnSignature"]!.GetValue<string>();
            byte[] basePreimage = ComputeBasePreimage(submitter.ClassicAddress, 0, TfAllOrNothing, inner1, inner2);
            byte[] signerAccountId = XrplCodec.DecodeAccountID(participant.ClassicAddress);
            Assert.IsTrue(XrplKeypairs.Verify(Concat(basePreimage, signerAccountId), signature, participant.PublicKey));
        }

        [TestMethod]
        public void TestUSignAsBatchPart_MissingSequence_Throws()
        {
            XrplWallet submitter = XrplWallet.Generate();
            XrplWallet participant = XrplWallet.Generate();
            XrplWallet destination = XrplWallet.Generate();

            JsonObject inner = InnerPayment(participant.ClassicAddress, destination.ClassicAddress, 20);
            JsonObject outer = BuildOuterBatch(submitter.ClassicAddress, 3, inner);
            outer.Remove("Sequence");

            Assert.ThrowsExactly<Xrpl.Client.Exceptions.ValidationException>(() =>
                participant.SignAsBatchPart(BuildBatchDictionary(outer), multisign: false, signingFor: participant.ClassicAddress));
        }

        [TestMethod]
        public void TestUSignAsBatchPart_EmittedBlobCarriesNormalizedInnerTransactions()
        {
            XrplWallet submitter = XrplWallet.Generate();
            XrplWallet participant = XrplWallet.Generate();
            XrplWallet destination = XrplWallet.Generate();

            JsonObject inner = UnnormalizedInnerPayment(participant.ClassicAddress, destination.ClassicAddress, 7);
            JsonObject outer = BuildOuterBatch(submitter.ClassicAddress, 3, inner);

            SignatureResult result = participant.SignAsBatchPart(BuildBatchDictionary(outer), multisign: false, signingFor: participant.ClassicAddress);

            JsonNode decoded = XrplBinaryCodec.Decode(result.TxBlob);
            JsonObject decodedInner = decoded["RawTransactions"]?.AsArray()?[0]?["RawTransaction"]?.AsObject()
                ?? throw new AssertFailedException("Decoded blob has no inner RawTransaction.");

            Assert.AreEqual("0", decodedInner["Fee"]?.GetValue<string>(),
                "Fee of the inner transaction in the emitted blob.");
            Assert.AreEqual(string.Empty, decodedInner["SigningPubKey"]?.GetValue<string>(),
                "SigningPubKey of the inner transaction in the emitted blob.");

            // Read through the JSON text: the numeric type backing the node depends on how
            // the codec built it, and a TryGetValue that guesses wrong would quietly read 0.
            JsonNode flagsNode = decodedInner["Flags"]
                ?? throw new AssertFailedException("Inner transaction in the emitted blob carries no Flags.");
            uint decodedFlags = uint.Parse(flagsNode.ToJsonString(), CultureInfo.InvariantCulture);
            Assert.AreEqual(TfInnerBatchTxn, decodedFlags & TfInnerBatchTxn,
                "tfInnerBatchTxn of the inner transaction in the emitted blob.");

            // The point of the three assertions above: the batch preimage commits to the
            // txIDs of the NORMALISED inner transactions, so the blob has to carry those
            // same transactions. Were it to carry the originals, the signature would
            // attest to something the blob does not contain.
            string signedTxId = inner.NormalizeInnerTransaction().ComputeInnerTxId();
            Assert.AreEqual(signedTxId, decodedInner.ComputeInnerTxId(),
                "The inner transaction in the blob must hash to the txID the signature was made over.");

            // That equality alone still leaves one half unpinned: it says the blob agrees with a
            // txID computed here, not that the signature was made over a preimage containing it.
            // A regression hashing the originals into the preimage while writing the normalised
            // form into the blob would satisfy everything above. So verify the signature against
            // a preimage built from the normalised inner transactions.
            JsonObject batchSigner = decoded["BatchSigners"]?.AsArray()?[0]?["BatchSigner"]?.AsObject()
                ?? throw new AssertFailedException("BatchSigners missing in signed tx.");
            string signature = batchSigner["TxnSignature"]?.GetValue<string>()
                ?? throw new AssertFailedException("TxnSignature missing.");

            byte[] preimage = ComputeBasePreimage(submitter.ClassicAddress, 3, TfAllOrNothing, inner);
            byte[] signerAccountId = XrplCodec.DecodeAccountID(participant.ClassicAddress);
            Assert.IsTrue(XrplKeypairs.Verify(Concat(preimage, signerAccountId), signature, participant.PublicKey),
                "The signature must cover a preimage built from the normalised inner transactions.");
        }

        [TestMethod]
        public void TestUNormalizeInnerTransaction_LeavesItsArgumentUntouched()
        {
            XrplWallet participant = XrplWallet.Generate();
            XrplWallet destination = XrplWallet.Generate();

            JsonObject source = UnnormalizedInnerPayment(participant.ClassicAddress, destination.ClassicAddress, 7);
            source["LastLedgerSequence"] = 500u;
            string before = source.ToJsonString();

            JsonObject normalized = source.NormalizeInnerTransaction();

            Assert.AreEqual(before, source.ToJsonString(),
                "NormalizeInnerTransaction must leave the transaction it was given alone.");
            Assert.AreNotSame(source, normalized,
                "NormalizeInnerTransaction must return an object of its own.");

            // And the returned object is the normalised one, so the guarantee above is not
            // bought by the method having quietly stopped doing its work.
            Assert.AreEqual("0", normalized["Fee"]?.GetValue<string>());
            Assert.AreEqual(string.Empty, normalized["SigningPubKey"]?.GetValue<string>());
            Assert.IsNull(normalized["LastLedgerSequence"],
                "LastLedgerSequence must be absent from the normalised result.");
        }

        [TestMethod]
        public void TestUSortBatchSigners_DuplicateAccount_Throws()
        {
            XrplWallet walletA = XrplWallet.Generate();
            XrplWallet walletB = XrplWallet.Generate();

            var batchSigners = new JsonArray(
                new JsonObject { ["BatchSigner"] = new JsonObject { ["Account"] = walletA.ClassicAddress } },
                new JsonObject { ["BatchSigner"] = new JsonObject { ["Account"] = walletB.ClassicAddress } },
                new JsonObject { ["BatchSigner"] = new JsonObject { ["Account"] = walletA.ClassicAddress } });

            Assert.ThrowsExactly<InvalidOperationException>(() => BatchSigningHelper.SortBatchSigners(batchSigners));
        }

        [TestMethod]
        public void TestUSortBatchSigners_UniqueAccounts_Sorted()
        {
            XrplWallet walletA = XrplWallet.Generate();
            XrplWallet walletB = XrplWallet.Generate();
            XrplWallet walletC = XrplWallet.Generate();

            var batchSigners = new JsonArray(
                new JsonObject { ["BatchSigner"] = new JsonObject { ["Account"] = walletC.ClassicAddress } },
                new JsonObject { ["BatchSigner"] = new JsonObject { ["Account"] = walletA.ClassicAddress } },
                new JsonObject { ["BatchSigner"] = new JsonObject { ["Account"] = walletB.ClassicAddress } });

            JsonArray sorted = BatchSigningHelper.SortBatchSigners(batchSigners);
            Assert.AreEqual(3, sorted.Count);

            byte[]? previous = null;
            foreach (JsonNode? node in sorted)
            {
                string account = node!["BatchSigner"]!["Account"]!.GetValue<string>();
                byte[] current = XrplCodec.DecodeAccountID(account);
                if (previous != null)
                    Assert.IsTrue(SignerUtilities.ByteArrayComparer.Instance.Compare(previous, current) < 0,
                        "BatchSigners must be sorted ascending by account id.");
                previous = current;
            }
        }
    }
}
