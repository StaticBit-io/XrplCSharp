using System;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;
using Xrpl.Models.Enums;
using Xrpl.Wallet;

namespace Xrpl.Tests.Wallet.Tests
{
    /// <summary>
    /// <see cref="SignatureResult.GetTx"/> decodes a signed blob back into a typed transaction,
    /// and anything the models do not declare disappears in that step. Signing the result then
    /// produces a blob without it — which is how a co-signature got dropped: rippled and the
    /// codec both carry <c>CounterpartySignature</c> and <c>SponsorSignature</c>, but no request
    /// model declares either, and the co-signing docs recommended exactly this round trip.
    /// </summary>
    [TestClass]
    public class TestUSignatureResultRoundTrip
    {
        private static string EncodeBlob(JsonObject tx) => XrplBinaryCodec.Encode(tx);

        private static JsonObject SignedLoanSetWithCounterpartySignature()
        {
            JsonObject signature = new JsonObject
            {
                ["SigningPubKey"] = "ED0000000000000000000000000000000000000000000000000000000000000001",
                ["TxnSignature"] = "AABBCC",
            };

            return new JsonObject
            {
                ["TransactionType"] = "LoanSet",
                ["Account"] = "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd",
                ["LoanBrokerID"] = new string('A', 64),
                ["Sequence"] = 7L,
                ["Fee"] = "12",
                ["Flags"] = 0L,
                ["SigningPubKey"] = "",
                ["CounterpartySignature"] = signature,
            };
        }

        /// <summary>
        /// The blob carries the co-signature, so the round trip must refuse rather than hand back
        /// a transaction that silently lost it.
        /// </summary>
        [TestMethod]
        public void TestUGetTxRefusesABlobCarryingAFieldNoModelDeclares()
        {
            string blob = EncodeBlob(SignedLoanSetWithCounterpartySignature());
            SignatureResult result = new SignatureResult(blob, new string('0', 64));

            ValidationException failure = Assert.ThrowsExactly<ValidationException>(() => result.GetTx());

            StringAssert.Contains(failure.Message, "CounterpartySignature",
                "the caller has to be told which member would have been lost, not merely that something was");
        }

        /// <summary>
        /// The guard must not fire on an ordinary blob — every member of which the models do carry.
        /// Without this, a guard that always threw would pass the test above.
        /// </summary>
        [TestMethod]
        public void TestUGetTxStillDecodesABlobEveryMemberOfWhichIsModelled()
        {
            JsonObject tx = new JsonObject
            {
                ["TransactionType"] = "Payment",
                ["Account"] = "rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd",
                ["Destination"] = "rBTwLga3i2gz3doX6Gva3MgEV8ZCD8jjah",
                ["Amount"] = "1000000",
                ["Sequence"] = 7L,
                ["Fee"] = "12",
                ["Flags"] = 0L,
                ["SigningPubKey"] = "",
            };

            SignatureResult result = new SignatureResult(EncodeBlob(tx), new string('0', 64));

            ITransactionRequest decoded = result.GetTx();

            Assert.IsNotNull(decoded);
            Assert.AreEqual("Payment", decoded.TransactionType.ToString());
            Assert.AreEqual("rP9jPyP5kyvFRb6ZiRghAGw5u8SGAmU4bd", decoded.Account);
        }
    }
}
