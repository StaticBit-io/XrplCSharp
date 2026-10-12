using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text.Json;

using Xrpl.Amounts;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Tests.ClientLib
{
    /// <summary>
    /// <c>book_offers</c> over an MPT side (MPTokensV2, XLS-82), against rippled develop's
    /// <c>BookOffers.cpp</c> (<c>validateTakerJSON</c>, <c>parseTakerAssetJSON</c>,
    /// <c>parseTakerIssuerJSON</c>) and the offers <c>getBookPage</c> writes.
    /// </summary>
    [TestClass]
    public class TestUBookOffersMpt
    {
        private const string IssuanceId = "0000012FFD9EE5DA93AC614B4DB94D7E0FCE415CA51BED47";
        private const string OtherIssuanceId = "00000130FD9EE5DA93AC614B4DB94D7E0FCE415CA51BED47";
        private const string Issuer = "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B";
        private const string DomainId = "F10D0CC9A0F9A3CBF585B80BE09A186483668FDBDD39AA7E3370F3649CE134E5";

        private static TakerAmount Mpt(string id = IssuanceId) => new TakerAmount { MptIssuanceId = id };

        private static TakerAmount Xrp => new TakerAmount { Currency = "XRP" };

        private static TakerAmount Usd => new TakerAmount { Currency = "USD", Issuer = Issuer };

        private static BookOffersRequest Request(TakerAmount pays, TakerAmount gets) =>
            new BookOffersRequest { TakerPays = pays, TakerGets = gets };

        private static void AssertRefused(BookOffersRequest request, string expected)
        {
            ValidationException exception = Assert.ThrowsExactly<ValidationException>(request.Validate);
            StringAssert.Contains(exception.Message, expected);
        }

        [TestMethod]
        public void TestUMptSideSerializesAsTheIssuanceIdAlone()
        {
            string json = JsonSerializer.Serialize(Request(Mpt(), Xrp), XrplJsonOptions.Default);

            StringAssert.Contains(json, $"\"taker_pays\":{{\"mpt_issuance_id\":\"{IssuanceId}\"}}");
            StringAssert.Contains(json, "\"taker_gets\":{\"currency\":\"XRP\"}");
        }

        [TestMethod]
        public void TestUFromAssetPicksTheSideOfEachKind()
        {
            TakerAmount mpt = TakerAmount.FromAsset(new IssuedCurrency { MptIssuanceId = IssuanceId });
            TakerAmount xrp = TakerAmount.FromAsset(new IssuedCurrency { Currency = "XRP" });
            TakerAmount iou = TakerAmount.FromAsset(new IssuedCurrency { Currency = "USD", Issuer = Issuer });

            Assert.AreEqual(IssuanceId, mpt.MptIssuanceId);
            Assert.IsNull(mpt.Currency);
            Assert.IsNull(mpt.Issuer);
            Assert.AreEqual("XRP", xrp.Currency);
            Assert.IsNull(xrp.Issuer);
            Assert.AreEqual("USD", iou.Currency);
            Assert.AreEqual(Issuer, iou.Issuer);
            Assert.ThrowsExactly<ArgumentNullException>(() => TakerAmount.FromAsset(null));
        }

        [TestMethod]
        public void TestUValidateAcceptsMptAgainstEveryKind()
        {
            Request(Mpt(), Xrp).Validate();
            Request(Xrp, Mpt()).Validate();
            Request(Mpt(), Usd).Validate();
            Request(Usd, Mpt()).Validate();
            Request(Mpt(), Mpt(OtherIssuanceId)).Validate();
            Request(Mpt(IssuanceId.ToLowerInvariant()), Xrp).Validate();
        }

        [TestMethod]
        public void TestUValidateAcceptsWhatTheNodeParses()
        {
            // toCurrency: an empty code is XRP, a 160-bit code is hex; toIssuer: 40 hex digits.
            Request(new TakerAmount { Currency = "" }, Usd).Validate();
            Request(new TakerAmount { Currency = "0158415500000000C1F76FF6ECB0BAC600000000", Issuer = Issuer }, Xrp).Validate();
            Request(new TakerAmount { Currency = "USD", Issuer = "0000000000000000000000000000000000000002" }, Xrp).Validate();
            new BookOffersRequest { TakerPays = Mpt(), TakerGets = Xrp, Taker = Issuer, Domain = DomainId }.Validate();
        }

        [TestMethod]
        public void TestUValidateRefusesAMissingSide()
        {
            AssertRefused(Request(null, Xrp), "Missing field 'taker_pays'");
            AssertRefused(Request(Xrp, null), "Missing field 'taker_gets'");
            AssertRefused(Request(new TakerAmount(), Xrp), "Missing field 'taker_pays.currency'");
            AssertRefused(Request(Xrp, new TakerAmount { Issuer = Issuer }), "Missing field 'taker_gets.currency'");
        }

        [TestMethod]
        public void TestUValidateRefusesAnMptWithACurrencyOrAnIssuer()
        {
            AssertRefused(Request(new TakerAmount { MptIssuanceId = IssuanceId, Currency = "USD" }, Xrp), "Invalid field 'taker_pays'");
            AssertRefused(Request(Xrp, new TakerAmount { MptIssuanceId = IssuanceId, Issuer = Issuer }), "Invalid field 'taker_gets'");
        }

        [TestMethod]
        public void TestUValidateRefusesAnIssuanceIdTheNodeCannotParse()
        {
            AssertRefused(Request(Mpt(IssuanceId.Substring(2)), Xrp), "Invalid field 'taker_pays.mpt_issuance_id'. (srcCurMalformed)");
            AssertRefused(Request(Xrp, Mpt("Z" + IssuanceId.Substring(1))), "Invalid field 'taker_gets.mpt_issuance_id'. (dstAmtMalformed)");
            AssertRefused(Request(Xrp, Mpt("")), "(dstAmtMalformed)");
        }

        [TestMethod]
        public void TestUValidateRefusesTheSameMptOnBothSides()
        {
            AssertRefused(Request(Mpt(), Mpt(IssuanceId.ToLowerInvariant())), "(badMarket)");
        }

        [TestMethod]
        public void TestUValidateRefusesTheSameIssuedCurrencyInEitherForm()
        {
            // "USD" and its 160-bit form are one currency; the issuer as hex is the same account.
            TakerAmount hexUsd = new TakerAmount { Currency = "0000000000000000000000005553440000000000", Issuer = Issuer };
            AssertRefused(Request(Usd, hexUsd), "(badMarket)");
            AssertRefused(Request(Xrp, new TakerAmount { Currency = "" }), "(badMarket)");
        }

        [TestMethod]
        public void TestUValidateRefusesACurrencyOrIssuerTheNodeRefuses()
        {
            AssertRefused(Request(new TakerAmount { Currency = "US", Issuer = Issuer }, Xrp), "Invalid field 'taker_pays.currency', bad currency. (srcCurMalformed)");
            AssertRefused(Request(Xrp, new TakerAmount { Currency = "U.D", Issuer = Issuer }), "(dstAmtMalformed)");
            AssertRefused(Request(new TakerAmount { Currency = "USD", Issuer = "not an account" }, Xrp), "bad issuer. (srcIsrMalformed)");
            AssertRefused(Request(new TakerAmount { Currency = "USD", Issuer = "rrrrrrrrrrrrrrrrrrrrBZbvji" }, Xrp), "bad issuer account one. (srcIsrMalformed)");
            AssertRefused(Request(new TakerAmount { Currency = "XRP", Issuer = Issuer }, Usd), "Unneeded field 'taker_pays.issuer'");
            AssertRefused(Request(Xrp, new TakerAmount { Currency = "USD" }), "expected non-XRP issuer. (dstIsrMalformed)");
        }

        [TestMethod]
        public void TestUValidateRefusesABadTakerOrDomain()
        {
            AssertRefused(new BookOffersRequest { TakerPays = Mpt(), TakerGets = Xrp, Taker = "rNotAnAccount" }, "Invalid field 'taker'");
            AssertRefused(new BookOffersRequest { TakerPays = Mpt(), TakerGets = Xrp, Domain = DomainId.Substring(1) }, "(domainMalformed)");
        }

        [TestMethod]
        public void TestUClientRefusesBeforeSending()
        {
            using XrplClient client = new XrplClient("ws://127.0.0.1:1");
            IXrplClient api = client;

            Assert.ThrowsExactly<ValidationException>(() => api.BookOffers(Request(Mpt(), Mpt())));
        }

        /// <summary>
        /// An offer giving an MPT for XRP as <c>getBookPage</c> writes it: the MPT amounts as
        /// objects with <c>mpt_issuance_id</c> and <c>value</c>, the funded amounts the same way.
        /// </summary>
        [TestMethod]
        public void TestUResponseReadsMptAmounts()
        {
            const string body = $$"""
                {
                  "ledger_current_index": 12,
                  "validated": false,
                  "offers": [
                    {
                      "Account": "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn",
                      "BookDirectory": "5C8970D155D65DB8FF49B291D7EFFA4A09F9E8A68D9974B25A07F01A195F8476",
                      "BookNode": "0",
                      "Flags": 0,
                      "LedgerEntryType": "Offer",
                      "OwnerNode": "0",
                      "PreviousTxnID": "D3E5A2E2E4F8A5B6C7D8E9F0A1B2C3D4E5F6A7B8C9D0E1F2A3B4C5D6E7F8A9B0",
                      "PreviousTxnLgrSeq": 11,
                      "Sequence": 5,
                      "TakerGets": { "mpt_issuance_id": "{{IssuanceId}}", "value": "100" },
                      "TakerPays": "1000000",
                      "index": "96F76F27D8A327FC48753167EC04A46AA0E382E6F57F32FD12274144D00F1797",
                      "owner_funds": "250",
                      "taker_gets_funded": { "mpt_issuance_id": "{{IssuanceId}}", "value": "40" },
                      "taker_pays_funded": "400000",
                      "quality": "10000"
                    }
                  ]
                }
                """;

            BookOffers response = JsonSerializer.Deserialize<BookOffers>(body, XrplJsonOptions.Default);

            Assert.HasCount(1, response.Offers);
            Xrpl.Models.Transactions.Offer offer = response.Offers[0];
            Assert.AreEqual(IssuanceId, offer.TakerGets.MPTokenIssuanceID);
            Assert.AreEqual("100", offer.TakerGets.Value);
            Assert.AreEqual("1000000", offer.TakerPays.Value);
            Assert.AreEqual(AmountKind.Mpt, offer.TakerGets.ToXrplAmount().Kind);
            Assert.AreEqual(AmountKind.Xrp, offer.TakerPays.ToXrplAmount().Kind);
            Assert.AreEqual(IssuanceId, offer.TakerGetsFunded.MPTokenIssuanceID);
            Assert.AreEqual("40", offer.TakerGetsFunded.Value);
            Assert.AreEqual("400000", offer.TakerPaysFunded.Value);
            Assert.AreEqual("250", offer.OwnerFunds);
            Assert.AreEqual(10000m, offer.Quality);
            Assert.AreEqual(XrplQuality.FromAmounts(offer.TakerPays.ToXrplAmount(), offer.TakerGets.ToXrplAmount()), offer.RemainingQuality);
        }
    }
}
