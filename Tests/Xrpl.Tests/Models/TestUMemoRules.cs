using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

namespace Xrpl.Tests.Models
{
    /// <summary>
    /// Memos a node refuses locally are refused before the transaction is signed - issue #119.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>isMemoOkay</c> runs in rippled's <c>passesLocalChecks</c>: the transaction is not relayed,
    /// reaches no ledger and costs no fee, and the answer names no field. The consumer has by then
    /// built, autofilled and signed it. These tests go through <see cref="XrplWallet.Sign"/> rather
    /// than a validator, because that is the point of the fix: <c>Validation.Validate</c> is called
    /// nowhere in production, so a rule that lived only there would be a rule nobody runs.
    /// </para>
    /// <para>
    /// The two rules the codec already enforces - a member other than <c>MemoType</c>,
    /// <c>MemoData</c> or <c>MemoFormat</c> inside a <c>Memo</c>, and a value that is not hex - are
    /// not retested here. <c>TestUStrictNestedFields</c> covers the first, and duplicating them
    /// would create a second place to keep in step with the same truth.
    /// </para>
    /// </remarks>
    [TestClass]
    public class TestUMemoRules
    {
        private const string Seed = "snGHNrPbHrdUcszeuDEigMdC1Lyyd";

        /// <summary>
        /// The largest <c>MemoData</c> that fits in a memo carrying nothing else.
        /// </summary>
        /// <remarks>
        /// 1 byte of object start marker + 1 of field id + 2 of length prefix (a value over 192
        /// bytes takes two) + the data + 1 of object end marker = 1024 exactly.
        /// </remarks>
        private const int LargestMemoDataInOneMemo = 1019;

        private static Dictionary<string, object> Payment(XrplWallet wallet) => new Dictionary<string, object>
        {
            { "TransactionType", "Payment" },
            { "Account", wallet.ClassicAddress },
            { "Destination", "rQ3fNyLjbvcDaPNS4EAJY8aT9zR3uGk17c" },
            { "Amount", "1000" },
            { "Fee", "12" },
            { "Sequence", 1u },
            { "LastLedgerSequence", 100u },
        };

        private static Dictionary<string, object> Memo(string memoData, string memoType = null, string memoFormat = null)
        {
            Dictionary<string, object> memo = new Dictionary<string, object>();
            if (memoType != null)
            {
                memo["MemoType"] = memoType;
            }

            if (memoData != null)
            {
                memo["MemoData"] = memoData;
            }

            if (memoFormat != null)
            {
                memo["MemoFormat"] = memoFormat;
            }

            return new Dictionary<string, object> { { "Memo", memo } };
        }

        private static string HexOf(int byteCount) => new string('A', byteCount * 2);

        /// <summary>
        /// The boundary from below: a memo filling the limit exactly still signs.
        /// </summary>
        /// <remarks>
        /// Without this, a check that refused every memo would satisfy every other test here.
        /// </remarks>
        [TestMethod]
        public void TestUMemoAtTheLimitStillSigns()
        {
            XrplWallet wallet = XrplWallet.FromSeed(Seed);
            Dictionary<string, object> tx = Payment(wallet);
            tx["Memos"] = new List<object> { Memo(HexOf(LargestMemoDataInOneMemo)) };

            SignatureResult signed = wallet.Sign(tx);

            Assert.IsFalse(
                string.IsNullOrEmpty(signed.TxBlob),
                "A memo of exactly the maximum size is legal and must still produce a blob.");
        }

        /// <summary>
        /// One byte over, and it is refused - before a signature exists.
        /// </summary>
        [TestMethod]
        public void TestUMemoOverTheLimitIsRefusedBeforeSigning()
        {
            XrplWallet wallet = XrplWallet.FromSeed(Seed);
            Dictionary<string, object> tx = Payment(wallet);
            tx["Memos"] = new List<object> { Memo(HexOf(LargestMemoDataInOneMemo + 1)) };

            ValidationException error = Assert.ThrowsExactly<ValidationException>(
                () => wallet.Sign(tx),
                "one byte past the limit is what the node refuses, so signing it is work thrown away");

            StringAssert.Contains(
                error.Message,
                "1025",
                "The message must say how large the array actually came out, or the caller is left guessing how much to cut.");
            Assert.IsFalse(
                tx.ContainsKey("TxnSignature"),
                "Refused before signing: nothing may have been added to the transaction.");
        }

        /// <summary>
        /// The limit is on the array, not on one memo, so splitting the content does not get round it.
        /// </summary>
        /// <remarks>
        /// Worth its own test because the opposite is the natural assumption, and acting on it
        /// costs another round trip to a node that refuses the transaction just the same.
        /// </remarks>
        [TestMethod]
        public void TestUSeveralMemosShareOneLimit()
        {
            XrplWallet wallet = XrplWallet.FromSeed(Seed);
            Dictionary<string, object> tx = Payment(wallet);
            tx["Memos"] = new List<object>
            {
                Memo(HexOf(600)),
                Memo(HexOf(600)),
            };

            ValidationException error = Assert.ThrowsExactly<ValidationException>(() => wallet.Sign(tx));

            StringAssert.Contains(
                error.Message,
                "whole array",
                "The message must say the limit is on the array, since splitting is the obvious thing to try next.");
        }

        /// <summary>
        /// <c>MemoType</c> and <c>MemoFormat</c> may only decode to characters a URL allows.
        /// </summary>
        [TestMethod]
        public void TestUMemoTypeMustDecodeToUrlSafeCharacters()
        {
            XrplWallet wallet = XrplWallet.FromSeed(Seed);
            Dictionary<string, object> tx = Payment(wallet);

            // "a b" - the space is legal hex and legal UTF-8, and not legal here.
            tx["Memos"] = new List<object> { Memo(memoData: "72656E74", memoType: "612062") };

            ValidationException error = Assert.ThrowsExactly<ValidationException>(() => wallet.Sign(tx));

            StringAssert.Contains(error.Message, "MemoType");
            StringAssert.Contains(
                error.Message,
                "0x20",
                "Naming the byte is what turns this from a puzzle into a fix.");
        }

        /// <summary>
        /// The same restriction on <c>MemoFormat</c>, which is the field it is easiest to forget.
        /// </summary>
        [TestMethod]
        public void TestUMemoFormatMustDecodeToUrlSafeCharacters()
        {
            XrplWallet wallet = XrplWallet.FromSeed(Seed);
            Dictionary<string, object> tx = Payment(wallet);
            tx["Memos"] = new List<object> { Memo(memoData: "72656E74", memoFormat: "612062") };

            ValidationException error = Assert.ThrowsExactly<ValidationException>(() => wallet.Sign(tx));

            StringAssert.Contains(error.Message, "MemoFormat");
        }

        /// <summary>
        /// The restriction stops at <c>MemoData</c>: it carries arbitrary bytes by design.
        /// </summary>
        /// <remarks>
        /// The same bytes that are refused in a <c>MemoType</c> above must go through here, or the
        /// check has been applied one field too widely - and a memo is mostly used for exactly the
        /// content that is not URL-safe.
        /// </remarks>
        [TestMethod]
        public void TestUMemoDataMayHoldAnythingAtAll()
        {
            XrplWallet wallet = XrplWallet.FromSeed(Seed);
            Dictionary<string, object> tx = Payment(wallet);
            tx["Memos"] = new List<object> { Memo(memoData: "612062FF00", memoType: "687474703A2F2F612E62") };

            SignatureResult signed = wallet.Sign(tx);

            Assert.IsFalse(string.IsNullOrEmpty(signed.TxBlob));
        }

        /// <summary>
        /// A transaction without memos is not touched by any of this.
        /// </summary>
        [TestMethod]
        public void TestUNoMemosSignsUnchanged()
        {
            XrplWallet wallet = XrplWallet.FromSeed(Seed);

            SignatureResult signed = wallet.Sign(Payment(wallet));

            Assert.IsFalse(string.IsNullOrEmpty(signed.TxBlob));
        }

        /// <summary>
        /// The measurement is the node's own: the array's serialized length, without the array's
        /// own markers.
        /// </summary>
        /// <remarks>
        /// Asserted directly rather than only through signing, because the arithmetic is the part
        /// that is easy to get wrong by a byte or two and hard to see afterwards.
        /// </remarks>
        [TestMethod]
        public void TestUTheLimitIsMeasuredTheWayANodeMeasuresIt()
        {
            List<object> atTheLimit = new List<object> { Memo(HexOf(LargestMemoDataInOneMemo)) };
            List<object> justOver = new List<object> { Memo(HexOf(LargestMemoDataInOneMemo + 1)) };

            MemoRules.Validate(atTheLimit);

            ValidationException error = Assert.ThrowsExactly<ValidationException>(() => MemoRules.Validate(justOver));

            StringAssert.Contains(error.Message, $"{MemoRules.MaxSerializedLength + 1} bytes");
        }
    }
}
