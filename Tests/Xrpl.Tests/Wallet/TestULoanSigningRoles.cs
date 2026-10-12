using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec;
using Xrpl.BinaryCodec.Hashing;
using Xrpl.Client.Exceptions;
using Xrpl.Keypairs;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.Wallet
{
    /// <summary>
    /// XLS-66 LoanSet roles: the submitter is <c>Account</c>, the counterparty is
    /// <c>Counterparty</c> or the broker owner when omitted, and either of them may be the
    /// broker owner (rippled <c>LoanSet::preclaim</c>, <c>LoanSet::checkSign</c>).
    /// </summary>
    [TestClass]
    public class TestULoanSigningRoles
    {
        private static readonly XrplWallet BrokerOwner = XrplWallet.Generate();
        private static readonly XrplWallet Borrower = XrplWallet.Generate("secp256k1");
        private static readonly XrplWallet Stranger = XrplWallet.Generate();

        private const string BrokerId = "1111111111111111111111111111111111111111111111111111111111111111";

        private static JsonObject Prepared(XrplWallet submitter, string counterparty)
        {
            JsonObject tx = new JsonObject
            {
                ["TransactionType"] = "LoanSet",
                ["Account"] = submitter.ClassicAddress,
                ["LoanBrokerID"] = BrokerId,
                ["PrincipalRequested"] = "10000000",
                ["Fee"] = "24",
                ["Sequence"] = 5u,
            };
            if (counterparty is not null)
                tx["Counterparty"] = counterparty;
            return LoanSigningHelper.PrepareForSigning(tx, submitter);
        }

        private static void AssertSignedBy(string txBlob, XrplWallet submitter, XrplWallet counterparty)
        {
            JsonObject decoded = XrplBinaryCodec.Decode(txBlob).AsObject();
            Assert.AreEqual(submitter.PublicKey, decoded["SigningPubKey"]!.GetValue<string>(), "the submitter holds the main signature");

            JsonObject coSignature = decoded["CounterpartySignature"]!.AsObject();
            Assert.AreEqual(counterparty.PublicKey, coSignature["SigningPubKey"]!.GetValue<string>(), "the counterparty holds CounterpartySignature");

            byte[] submitterPreimage = global::Xrpl.AddressCodec.Utils.FromHex(XrplBinaryCodec.EncodeForSigning(decoded, HashPrefix.TransactionSig));
            byte[] counterpartyPreimage = global::Xrpl.AddressCodec.Utils.FromHex(XrplBinaryCodec.EncodeForSigning(decoded, HashPrefix.CounterpartyTransactionSig));

            Assert.IsTrue(XrplKeypairs.Verify(submitterPreimage, decoded["TxnSignature"]!.GetValue<string>(), submitter.PublicKey),
                "TxnSignature must verify under the transaction prefix with the submitter's key");
            Assert.IsTrue(XrplKeypairs.Verify(counterpartyPreimage, coSignature["TxnSignature"]!.GetValue<string>(), counterparty.PublicKey),
                "CounterpartySignature must verify under the counterparty prefix with the counterparty's key");
        }

        [TestMethod]
        public void TestUSignLoanSet_BrokerSubmits_EitherWalletOrder()
        {
            JsonObject prepared = Prepared(BrokerOwner, Borrower.ClassicAddress);

            SignatureResult inOrder = LoanSigningHelper.SignLoanSet(prepared, BrokerOwner, Borrower);
            SignatureResult swapped = LoanSigningHelper.SignLoanSet(prepared, Borrower, BrokerOwner);

            AssertSignedBy(inOrder.TxBlob, BrokerOwner, Borrower);
            AssertSignedBy(swapped.TxBlob, BrokerOwner, Borrower);
            Assert.AreEqual(inOrder.TxBlob, swapped.TxBlob, "the wallet order must not change the signed transaction");
        }

        [TestMethod]
        public void TestUSignLoanSet_BorrowerSubmits_BrokerOwnerCoSigns_EitherWalletOrder()
        {
            JsonObject prepared = Prepared(Borrower, BrokerOwner.ClassicAddress);

            SignatureResult inOrder = LoanSigningHelper.SignLoanSet(prepared, Borrower, BrokerOwner);
            SignatureResult swapped = LoanSigningHelper.SignLoanSet(prepared, BrokerOwner, Borrower);

            AssertSignedBy(inOrder.TxBlob, Borrower, BrokerOwner);
            AssertSignedBy(swapped.TxBlob, Borrower, BrokerOwner);
            Assert.AreEqual(inOrder.TxBlob, swapped.TxBlob, "the wallet order must not change the signed transaction");
        }

        [TestMethod]
        public void TestUSignLoanSet_BorrowerSubmits_CounterpartyOmitted_BrokerOwnerChecked()
        {
            JsonObject prepared = Prepared(Borrower, null);

            SignatureResult withoutOwner = LoanSigningHelper.SignLoanSet(prepared, Borrower, BrokerOwner);
            AssertSignedBy(withoutOwner.TxBlob, Borrower, BrokerOwner);

            SignatureResult checkedResult = LoanSigningHelper.SignLoanSet(prepared, BrokerOwner, Borrower, BrokerOwner.ClassicAddress);
            AssertSignedBy(checkedResult.TxBlob, Borrower, BrokerOwner);

            ValidationException ex = Assert.ThrowsExactly<ValidationException>(
                () => LoanSigningHelper.SignLoanSet(prepared, Borrower, Stranger, BrokerOwner.ClassicAddress));
            StringAssert.Contains(ex.Message, "broker owner");
        }

        [TestMethod]
        public void TestUSignLoanSet_CoSignerIsNotCounterparty_Throws()
        {
            JsonObject prepared = Prepared(BrokerOwner, Borrower.ClassicAddress);

            ValidationException ex = Assert.ThrowsExactly<ValidationException>(
                () => LoanSigningHelper.SignLoanSet(prepared, BrokerOwner, Stranger));
            StringAssert.Contains(ex.Message, "Counterparty field");
        }

        [TestMethod]
        public void TestUSignLoanSet_NeitherWalletIsAccount_Throws()
        {
            JsonObject prepared = Prepared(BrokerOwner, Borrower.ClassicAddress);

            ValidationException ex = Assert.ThrowsExactly<ValidationException>(
                () => LoanSigningHelper.SignLoanSet(prepared, Stranger, Borrower));
            StringAssert.Contains(ex.Message, "LoanSet Account");
        }

        [TestMethod]
        public void TestUSignLoanSet_NeitherPartyIsBrokerOwner_Throws()
        {
            JsonObject prepared = Prepared(Borrower, Stranger.ClassicAddress);

            ValidationException ex = Assert.ThrowsExactly<ValidationException>(
                () => LoanSigningHelper.SignLoanSet(prepared, Borrower, Stranger, BrokerOwner.ClassicAddress));
            StringAssert.Contains(ex.Message, "Neither Account");
        }

        [TestMethod]
        public void TestUSubmitterSign_BorrowerSubmits_BrokerOwnerSignsFirst()
        {
            JsonObject prepared = Prepared(Borrower, BrokerOwner.ClassicAddress);

            SignatureResult withCounterparty = BrokerOwner.SignAsLoanCounterparty(
                System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(
                    prepared.ToJsonString(), global::Xrpl.Client.Json.XrplJsonOptions.Default));
            SignatureResult final = LoanSigningHelper.SubmitterSign(withCounterparty.TxBlob, Borrower);

            AssertSignedBy(final.TxBlob, Borrower, BrokerOwner);
            Assert.AreEqual(LoanSigningHelper.SignLoanSet(prepared, Borrower, BrokerOwner).TxBlob, final.TxBlob,
                "the sequential flow must produce the same transaction as signing both at once");
        }
    }
}
