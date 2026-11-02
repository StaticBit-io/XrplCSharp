using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Text.Json;

using Xrpl.AddressCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// Trust line side helpers. Expected values follow rippled: the Low side of a RippleState is the
    /// account with the numerically smaller 160-bit AccountID, <c>Balance</c> is stored from the Low
    /// account's side (<c>trustCreate</c> writes <c>-balance</c> when the High account is the one
    /// being set), and <c>lsfLowReserve</c>/<c>lsfHighReserve</c> mark the side whose owner count the
    /// line is in (<c>trustCreate</c>, <c>trustDelete</c>, <c>deleteAMMTrustLine</c>).
    /// </summary>
    [TestClass]
    public class TestURippleStateExtensions
    {
        // The RippleState example from xrpl.org (account_objects): rsA2... is Low, rf1Bi... is High.
        // 'rf' sorts before 'rs' as text, so a base58 string comparison names the wrong side - the
        // fixture is chosen to catch exactly that.
        private const string Low = "rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW";
        private const string High = "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn";
        private const string Stranger = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";

        private const uint HighReserveAndLowAuth = 393216; // 0x60000 in the xrpl.org example

        private static LORippleState Line(
            RippleStateFlags? flags = null,
            string balance = "-5",
            string lowLimit = "0",
            string highLimit = "110")
        {
            return new LORippleState
            {
                Flags = flags,
                Balance = balance == null ? null : new Currency { CurrencyCode = "USD", Issuer = "rrrrrrrrrrrrrrrrrrrrBZbvji", Value = balance },
                LowLimit = lowLimit == null ? null : new Currency { CurrencyCode = "USD", Issuer = Low, Value = lowLimit },
                HighLimit = highLimit == null ? null : new Currency { CurrencyCode = "USD", Issuer = High, Value = highLimit },
            };
        }

        private static int CompareAccountIds(string a, string b)
        {
            byte[] x = XrplCodec.DecodeAccountID(a);
            byte[] y = XrplCodec.DecodeAccountID(b);
            for (int i = 0; i < x.Length; i++)
            {
                if (x[i] != y[i])
                {
                    return x[i].CompareTo(y[i]);
                }
            }

            return 0;
        }

        #region Fixture sanity

        [TestMethod]
        public void Fixture_LowIsNumericallySmallerAccountId_AndStringOrderDisagrees()
        {
            Assert.IsLessThan(0, CompareAccountIds(Low, High), "Low must be the smaller AccountID.");
            Assert.IsGreaterThan(0, string.CompareOrdinal(Low, High), "The fixture must distinguish ID order from text order.");
        }

        #endregion

        #region IsLowAccount / GetCounterparty

        [TestMethod]
        public void IsLowAccount_TellsSidesByAccountNotByAddressText()
        {
            LORippleState line = Line();

            Assert.IsTrue(line.IsLowAccount(Low));
            Assert.IsFalse(line.IsLowAccount(High));
        }

        [TestMethod]
        public void IsLowAccount_AccountNotOnTheLine_Throws()
        {
            Assert.ThrowsExactly<ArgumentException>(() => Line().IsLowAccount(Stranger));
        }

        [TestMethod]
        public void IsLowAccount_NullArguments_Throw()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => ((LORippleState)null).IsLowAccount(Low));
            Assert.ThrowsExactly<ArgumentNullException>(() => Line().IsLowAccount(null));
        }

        [TestMethod]
        public void IsLowAccount_SparseLineWithOnlyTheAccountsOwnLimit_StillAnswers()
        {
            LORippleState lowOnly = Line(lowLimit: "0", highLimit: null);
            LORippleState highOnly = Line(lowLimit: null, highLimit: "110");

            Assert.IsTrue(lowOnly.IsLowAccount(Low));
            Assert.IsFalse(highOnly.IsLowAccount(High));
        }

        [TestMethod]
        public void IsLowAccount_SparseLineWithOnlyThePeersLimit_Throws()
        {
            // Without the account's own limit the side cannot be read from the entry, and guessing
            // would hand a wrong answer to a caller that passed the wrong account.
            Assert.ThrowsExactly<ArgumentException>(() => Line(lowLimit: null).IsLowAccount(Low));
            Assert.ThrowsExactly<ArgumentException>(() => Line(highLimit: null).IsLowAccount(High));
        }

        [TestMethod]
        public void GetCounterparty_IsTheOtherSide()
        {
            LORippleState line = Line();

            Assert.AreEqual(High, line.GetCounterparty(Low));
            Assert.AreEqual(Low, line.GetCounterparty(High));
        }

        [TestMethod]
        public void GetCounterparty_AccountNotOnTheLine_Throws()
        {
            Assert.ThrowsExactly<ArgumentException>(() => Line().GetCounterparty(Stranger));
        }

        [TestMethod]
        public void GetCounterparty_PeerLimitMissing_Throws()
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => Line(highLimit: null).GetCounterparty(Low));
        }

        #endregion

        #region Limits

        [TestMethod]
        public void GetLimitOf_And_GetPeerLimitOf_ReadTheAccountsOwnAndTheOtherSide()
        {
            LORippleState line = Line();

            Assert.AreEqual("0", line.GetLimitOf(Low).Value);
            Assert.AreEqual("110", line.GetPeerLimitOf(Low).Value);
            Assert.AreEqual("110", line.GetLimitOf(High).Value);
            Assert.AreEqual("0", line.GetPeerLimitOf(High).Value);
        }

        [TestMethod]
        public void GetLimitOf_MissingLimit_IsNull()
        {
            LORippleState line = Line(highLimit: null);

            Assert.IsNull(line.GetPeerLimitOf(Low));
            Assert.IsNotNull(line.GetLimitOf(Low));
        }

        [TestMethod]
        public void GetLimitOf_MaximumLimit_DoesNotThrow()
        {
            LORippleState line = Line(highLimit: "9999999999999999e80");

            Assert.AreEqual("9999999999999999e80", line.GetLimitOf(High).Value);
            Assert.AreEqual("9999999999999999e80", line.GetPeerLimitOf(Low).Value);
        }

        #endregion

        #region Balance

        [TestMethod]
        public void GetBalanceFor_IsStoredFromTheLowSideAndNegatedForTheHighSide()
        {
            // Balance -5: the Low account has issued 5 to the High account, so the High account holds 5.
            LORippleState line = Line(balance: "-5");

            Assert.AreEqual(-5m, line.GetBalanceFor(Low));
            Assert.AreEqual(5m, line.GetBalanceFor(High));
        }

        [TestMethod]
        public void GetBalanceFor_PositiveStoredBalance_MeansTheLowAccountHolds()
        {
            LORippleState line = Line(balance: "12.5");

            Assert.AreEqual(12.5m, line.GetBalanceFor(Low));
            Assert.AreEqual(-12.5m, line.GetBalanceFor(High));
        }

        [TestMethod]
        public void GetBalanceFor_BeyondDecimal_SaturatesInsteadOfThrowing()
        {
            LORippleState issued = Line(balance: "-9999999999999999e80");

            Assert.AreEqual(decimal.MinValue, issued.GetBalanceFor(Low));
            Assert.AreEqual(decimal.MaxValue, issued.GetBalanceFor(High));
        }

        [TestMethod]
        public void GetBalanceFor_MissingBalance_ReadsAsZero()
        {
            LORippleState line = Line(balance: null);

            Assert.AreEqual(0m, line.GetBalanceFor(Low));
            Assert.AreEqual(0m, line.GetBalanceFor(High));
        }

        [TestMethod]
        public void GetBalanceFor_AccountNotOnTheLine_Throws()
        {
            Assert.ThrowsExactly<ArgumentException>(() => Line().GetBalanceFor(Stranger));
        }

        [TestMethod]
        public void GetBalanceValueFor_KeepsEveryDigitOfTheLedgerText()
        {
            // A decimal round trip would saturate the first and round the second.
            LORippleState huge = Line(balance: "-9999999999999999e80");
            LORippleState dust = Line(balance: "1.234567890123456e-30");

            Assert.AreEqual("-9999999999999999e80", huge.GetBalanceValueFor(Low));
            Assert.AreEqual("9999999999999999e80", huge.GetBalanceValueFor(High));
            Assert.AreEqual("1.234567890123456e-30", dust.GetBalanceValueFor(Low));
            Assert.AreEqual("-1.234567890123456e-30", dust.GetBalanceValueFor(High));
        }

        [TestMethod]
        public void GetBalanceValueFor_ZeroHasNoSign()
        {
            foreach (string zero in new[] { "0", "-0", "0.00", "0e5" })
            {
                LORippleState line = Line(balance: zero);

                Assert.AreEqual(zero, line.GetBalanceValueFor(Low), "The Low side gets the stored text.");
                Assert.IsFalse(line.GetBalanceValueFor(High).StartsWith("-", StringComparison.Ordinal), $"'{zero}' negated must not become negative.");
            }
        }

        [TestMethod]
        public void GetBalanceValueFor_NotANumber_IsNullForTheHighSide()
        {
            foreach (string text in new[] { "abc", "", "1e", "NaN", "Infinity", "1e999" })
            {
                Assert.IsNull(Line(balance: text).GetBalanceValueFor(High), $"'{text}' is not a ledger amount.");
            }

            Assert.IsNull(Line(balance: null).GetBalanceValueFor(Low));
            Assert.IsNull(Line(balance: null).GetBalanceValueFor(High));
        }

        #endregion

        #region IsSet

        [TestMethod]
        public void IsSet_NullFlags_NothingIsSet()
        {
            RippleStateFlags? absent = null;

            foreach (RippleStateFlags flag in Enum.GetValues<RippleStateFlags>())
            {
                Assert.IsFalse(absent.IsSet(flag), $"{flag} must not read as set on an absent flag word.");
            }
        }

        [TestMethod]
        public void IsSet_ReadsTheBit()
        {
            RippleStateFlags? flags = (RippleStateFlags)HighReserveAndLowAuth;

            Assert.IsTrue(flags.IsSet(RippleStateFlags.lsfHighReserve));
            Assert.IsTrue(flags.IsSet(RippleStateFlags.lsfLowAuth));
            Assert.IsFalse(flags.IsSet(RippleStateFlags.lsfLowReserve));
            Assert.IsFalse(flags.IsSet(RippleStateFlags.lsfHighFreeze));
        }

        [TestMethod]
        public void IsSet_ZeroFlagWord_NothingIsSet()
        {
            RippleStateFlags? zero = 0;

            Assert.IsFalse(zero.IsSet(RippleStateFlags.lsfLowReserve));
        }

        [TestMethod]
        public void IsSet_CombinedFlag_RequiresEveryBit()
        {
            RippleStateFlags? flags = RippleStateFlags.lsfLowFreeze;
            RippleStateFlags both = RippleStateFlags.lsfLowFreeze | RippleStateFlags.lsfHighFreeze;

            Assert.IsFalse(flags.IsSet(both));
            Assert.IsTrue(((RippleStateFlags?)both).IsSet(both));
        }

        #endregion

        #region Flags of a side

        [TestMethod]
        public void GetFlagOf_And_GetPeerFlagOf_ProjectPairedFlagsToTheAccount()
        {
            // Low auth only: the Low account has authorized the High account.
            LORippleState line = Line(RippleStateFlags.lsfLowAuth | RippleStateFlags.lsfHighNoRipple);

            Assert.IsTrue(line.GetFlagOf(Low, RippleStateFlags.lsfLowAuth, RippleStateFlags.lsfHighAuth));
            Assert.IsFalse(line.GetFlagOf(High, RippleStateFlags.lsfLowAuth, RippleStateFlags.lsfHighAuth));
            Assert.IsFalse(line.GetPeerFlagOf(Low, RippleStateFlags.lsfLowAuth, RippleStateFlags.lsfHighAuth));
            Assert.IsTrue(line.GetPeerFlagOf(High, RippleStateFlags.lsfLowAuth, RippleStateFlags.lsfHighAuth));

            Assert.IsFalse(line.GetFlagOf(Low, RippleStateFlags.lsfLowNoRipple, RippleStateFlags.lsfHighNoRipple));
            Assert.IsTrue(line.GetFlagOf(High, RippleStateFlags.lsfLowNoRipple, RippleStateFlags.lsfHighNoRipple));
            Assert.IsTrue(line.GetPeerFlagOf(Low, RippleStateFlags.lsfLowNoRipple, RippleStateFlags.lsfHighNoRipple));
        }

        [TestMethod]
        public void GetFlagOf_AbsentFlagWord_IsFalse()
        {
            LORippleState line = Line(flags: null);

            Assert.IsFalse(line.GetFlagOf(Low, RippleStateFlags.lsfLowFreeze, RippleStateFlags.lsfHighFreeze));
            Assert.IsFalse(line.GetPeerFlagOf(Low, RippleStateFlags.lsfLowFreeze, RippleStateFlags.lsfHighFreeze));
        }

        [TestMethod]
        public void GetFlagOf_AccountNotOnTheLine_Throws()
        {
            Assert.ThrowsExactly<ArgumentException>(
                () => Line(RippleStateFlags.lsfLowFreeze).GetFlagOf(Stranger, RippleStateFlags.lsfLowFreeze, RippleStateFlags.lsfHighFreeze));
        }

        #endregion

        #region Reserve

        [TestMethod]
        public void OccupiesReserveOf_FollowsTheReserveFlagOfTheAccountsSide()
        {
            // 393216 from the xrpl.org example: lsfHighReserve | lsfLowAuth. The High account's owner
            // count includes the line; the Low account's does not.
            LORippleState line = Line((RippleStateFlags)HighReserveAndLowAuth);

            Assert.IsFalse(line.OccupiesReserveOf(Low));
            Assert.IsTrue(line.OccupiesReserveOf(High));
            Assert.IsTrue(line.PeerOccupiesReserveOf(Low));
            Assert.IsFalse(line.PeerOccupiesReserveOf(High));
        }

        [TestMethod]
        public void OccupiesReserveOf_BothSidesReserved()
        {
            LORippleState line = Line(RippleStateFlags.lsfLowReserve | RippleStateFlags.lsfHighReserve);

            Assert.IsTrue(line.OccupiesReserveOf(Low));
            Assert.IsTrue(line.OccupiesReserveOf(High));
            Assert.IsTrue(line.PeerOccupiesReserveOf(Low));
            Assert.IsTrue(line.PeerOccupiesReserveOf(High));
        }

        [TestMethod]
        public void OccupiesReserveOf_LowReserveOnly()
        {
            LORippleState line = Line(RippleStateFlags.lsfLowReserve);

            Assert.IsTrue(line.OccupiesReserveOf(Low));
            Assert.IsFalse(line.OccupiesReserveOf(High));
            Assert.IsFalse(line.PeerOccupiesReserveOf(Low));
            Assert.IsTrue(line.PeerOccupiesReserveOf(High));
        }

        [TestMethod]
        public void OccupiesReserveOf_AbsentFlagWord_IsFalse()
        {
            LORippleState line = Line(flags: null);

            Assert.IsFalse(line.OccupiesReserveOf(Low));
            Assert.IsFalse(line.PeerOccupiesReserveOf(Low));
        }

        [TestMethod]
        public void OccupiesReserveOf_AccountNotOnTheLine_Throws()
        {
            LORippleState line = Line(RippleStateFlags.lsfLowReserve);

            Assert.ThrowsExactly<ArgumentException>(() => line.OccupiesReserveOf(Stranger));
            Assert.ThrowsExactly<ArgumentException>(() => line.PeerOccupiesReserveOf(Stranger));
        }

        #endregion

        #region Quality

        [TestMethod]
        public void Quality_ReadsTheAccountsOwnAndThePeersValues()
        {
            LORippleState line = Line();
            line.LowQualityIn = 950_000_000;
            line.LowQualityOut = 1_010_000_000;
            line.HighQualityIn = 990_000_000;
            line.HighQualityOut = 1_020_000_000;

            Assert.AreEqual(950_000_000u, line.GetQualityInOf(Low));
            Assert.AreEqual(1_010_000_000u, line.GetQualityOutOf(Low));
            Assert.AreEqual(990_000_000u, line.GetPeerQualityInOf(Low));
            Assert.AreEqual(1_020_000_000u, line.GetPeerQualityOutOf(Low));

            Assert.AreEqual(990_000_000u, line.GetQualityInOf(High));
            Assert.AreEqual(1_020_000_000u, line.GetQualityOutOf(High));
            Assert.AreEqual(950_000_000u, line.GetPeerQualityInOf(High));
            Assert.AreEqual(1_010_000_000u, line.GetPeerQualityOutOf(High));
        }

        [TestMethod]
        public void Quality_NotSet_IsNullNotZero()
        {
            // rippled writes the field only when non-zero; absence is face value and is not invented as 0.
            LORippleState line = Line();

            Assert.IsNull(line.GetQualityInOf(Low));
            Assert.IsNull(line.GetQualityOutOf(High));
            Assert.IsNull(line.GetPeerQualityInOf(Low));
            Assert.IsNull(line.GetPeerQualityOutOf(High));
        }

        [TestMethod]
        public void Quality_AccountNotOnTheLine_Throws()
        {
            Assert.ThrowsExactly<ArgumentException>(() => Line().GetQualityInOf(Stranger));
        }

        #endregion

        #region MergePreviousOver

        [TestMethod]
        public void MergePreviousOver_NoPrevious_ReturnsTheFinalState()
        {
            LORippleState final = Line(RippleStateFlags.lsfLowReserve);

            Assert.AreSame(final, final.MergePreviousOver(null));
        }

        [TestMethod]
        public void MergePreviousOver_NullFinal_Throws()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => ((LORippleState)null).MergePreviousOver(Line()));
        }

        [TestMethod]
        public void MergePreviousOver_DeletedLineWithOneLimitChanged_KeepsTheFinalFlags()
        {
            // The common shape of a deleted line in metadata: FinalFields carries the line, and
            // PreviousFields lists only what the transaction changed - here the Low limit the
            // initiator set back to zero. Flags is absent from PreviousFields and must survive.
            LORippleState final = Line(
                RippleStateFlags.lsfLowReserve | RippleStateFlags.lsfLowNoRipple,
                balance: "0",
                lowLimit: "0",
                highLimit: "0");
            final.Index = "AAA";
            final.PreviousTxnID = "FINALTX";
            LORippleState previous = new LORippleState
            {
                LowLimit = new Currency { CurrencyCode = "USD", Issuer = Low, Value = "100" },
            };

            LORippleState merged = final.MergePreviousOver(previous);

            Assert.AreEqual("100", merged.LowLimit.Value);
            Assert.AreEqual("0", merged.HighLimit.Value);
            Assert.AreEqual(final.Flags, merged.Flags);
            Assert.IsTrue(merged.Flags.IsSet(RippleStateFlags.lsfLowReserve));
            Assert.IsTrue(merged.OccupiesReserveOf(Low));
            Assert.AreEqual("AAA", merged.Index);
            Assert.AreEqual("FINALTX", merged.PreviousTxnID);
        }

        [TestMethod]
        public void MergePreviousOver_PreviousFlagsOfZero_IsARealDiffAndWins()
        {
            // PreviousFields.Flags == 0 means the transaction cleared every flag; it is not "absent".
            LORippleState final = Line(RippleStateFlags.lsfLowReserve);
            LORippleState previous = new LORippleState { Flags = 0 };

            LORippleState merged = final.MergePreviousOver(previous);

            Assert.AreEqual((RippleStateFlags)0, merged.Flags);
            Assert.IsFalse(merged.OccupiesReserveOf(Low));
        }

        [TestMethod]
        public void MergePreviousOver_PreviousFieldsWinOverFinalFields()
        {
            LORippleState final = Line(RippleStateFlags.lsfHighReserve, balance: "-3");
            final.LowQualityIn = 1;
            final.HighQualityOut = 2;
            final.LowNode = "0";
            final.HighNode = "1";
            final.PreviousTxnLgrSeq = 10;
            final.LowSponsor = "rLowSponsorFinal";
            LORippleState previous = new LORippleState
            {
                Flags = RippleStateFlags.lsfLowReserve,
                Balance = new Currency { CurrencyCode = "USD", Issuer = "rrrrrrrrrrrrrrrrrrrrBZbvji", Value = "-7" },
                LowQualityIn = 5,
                HighQualityOut = 6,
                LowNode = "9",
                HighNode = "8",
                PreviousTxnLgrSeq = 3,
                LowSponsor = "rLowSponsorPrevious",
                HighSponsor = "rHighSponsorPrevious",
            };

            LORippleState merged = final.MergePreviousOver(previous);

            Assert.AreEqual(RippleStateFlags.lsfLowReserve, merged.Flags);
            Assert.AreEqual("-7", merged.Balance.Value);
            Assert.AreEqual(5u, merged.LowQualityIn);
            Assert.AreEqual(6u, merged.HighQualityOut);
            Assert.AreEqual("9", merged.LowNode);
            Assert.AreEqual("8", merged.HighNode);
            Assert.AreEqual(3u, merged.PreviousTxnLgrSeq);
            Assert.AreEqual("rLowSponsorPrevious", merged.LowSponsor);
            Assert.AreEqual("rHighSponsorPrevious", merged.HighSponsor);
            // Not in PreviousFields: taken from the final state.
            Assert.AreEqual("110", merged.HighLimit.Value);
            Assert.IsNull(merged.HighQualityIn);
        }

        [TestMethod]
        public void MergePreviousOver_DoesNotChangeEitherInput()
        {
            LORippleState final = Line(RippleStateFlags.lsfLowReserve, lowLimit: "0");
            LORippleState previous = new LORippleState
            {
                LowLimit = new Currency { CurrencyCode = "USD", Issuer = Low, Value = "100" },
            };

            LORippleState merged = final.MergePreviousOver(previous);

            Assert.AreNotSame(final, merged);
            Assert.AreEqual("0", final.LowLimit.Value);
            Assert.AreEqual("100", previous.LowLimit.Value);
            Assert.IsNull(previous.Flags);
        }

        [TestMethod]
        public void MergePreviousOver_UnknownFieldsAreMergedWithPreviousWinning()
        {
            LORippleState final = Line();
            final.UnknownFields = new Dictionary<string, JsonElement>
            {
                ["OnlyFinal"] = JsonDocument.Parse("1").RootElement.Clone(),
                ["Both"] = JsonDocument.Parse("2").RootElement.Clone(),
            };
            LORippleState previous = new LORippleState
            {
                UnknownFields = new Dictionary<string, JsonElement>
                {
                    ["Both"] = JsonDocument.Parse("3").RootElement.Clone(),
                },
            };

            LORippleState merged = final.MergePreviousOver(previous);

            Assert.AreEqual(1, merged.UnknownFields["OnlyFinal"].GetInt32());
            Assert.AreEqual(3, merged.UnknownFields["Both"].GetInt32());
            Assert.HasCount(2, final.UnknownFields);
        }

        #endregion

        #region ToDecimalSaturating

        private static Currency Iou(string value) => new Currency { CurrencyCode = "USD", Issuer = Low, Value = value };

        [TestMethod]
        public void ToDecimalSaturating_InRange_IsTheExactValue()
        {
            Assert.AreEqual(12.5m, Iou("12.5").ToDecimalSaturating());
            Assert.AreEqual(-0.000001m, Iou("-0.000001").ToDecimalSaturating());
            Assert.AreEqual(0m, Iou("0").ToDecimalSaturating());
        }

        [TestMethod]
        public void ToDecimalSaturating_AboveDecimalRange_SaturatesWithTheSign()
        {
            foreach (string big in new[] { "1e29", "8e28", "9999999999999999e80" })
            {
                Assert.AreEqual(decimal.MaxValue, Iou(big).ToDecimalSaturating(), big);
                Assert.AreEqual(decimal.MinValue, Iou("-" + big).ToDecimalSaturating(), "-" + big);
            }
        }

        [TestMethod]
        public void ToDecimalSaturating_NullAndBlank_AreZero()
        {
            Assert.AreEqual(0m, ((Currency)null).ToDecimalSaturating());
            Assert.AreEqual(0m, Iou(null).ToDecimalSaturating());
            Assert.AreEqual(0m, Iou("  ").ToDecimalSaturating());
        }

        [TestMethod]
        public void ToDecimalSaturating_BelowDecimalPrecision_ReadsAsZeroLikeValueAsNumber()
        {
            Assert.AreEqual(0m, Iou("1e-81").ToDecimalSaturating());
        }

        [TestMethod]
        public void ToDecimalSaturating_NotANumber_StillThrowsFormatException()
        {
            // Saturation answers "too large", not "not a quantity".
            Assert.ThrowsExactly<FormatException>(() => Iou("abc").ToDecimalSaturating());
            Assert.ThrowsExactly<FormatException>(() => Iou("NaN").ToDecimalSaturating());
        }

        [TestMethod]
        public void ToDecimalSaturating_XrpIsReadInDrops_LikeValueAsNumber()
        {
            Currency drops = new Currency { CurrencyCode = "XRP", Value = "1000000" };

            Assert.AreEqual(1000000m, drops.ToDecimalSaturating());
        }

        [TestMethod]
        public void ValueAsNumber_StillThrowsWhereTheSaturatingReadDoesNot()
        {
            Currency big = Iou("9999999999999999e80");

            Assert.ThrowsExactly<AmountOutOfRangeException>(() => _ = big.ValueAsNumber);
            Assert.AreEqual(decimal.MaxValue, big.ToDecimalSaturating());
        }

        #endregion
    }
}
