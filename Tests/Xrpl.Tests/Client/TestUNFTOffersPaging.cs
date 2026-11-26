using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;

namespace Xrpl.Tests.ClientLib
{
    /// <summary>
    /// limit and marker of nft_buy_offers / nft_sell_offers - issue #353.
    /// </summary>
    /// <remarks>
    /// The body below is shaped the way rippled's <c>NFTOffersCommon.cpp</c> writes it: <c>limit</c>
    /// and <c>marker</c> appear only when another page follows, and there is no <c>ledger_index</c>.
    /// </remarks>
    [TestClass]
    public class TestUNFTOffersPaging
    {
        private const string TokenId = "00080000B5F762798A53D543A014CAF8B297CFF8F2F937E816E5DA9C00000001";
        private const string Marker = "1C5D3A0E04A0EA5DF4CB1C46BF5E40A00A4B6F7AE1B5E50B3BA5A0D8A9A3E8F1";

        private const string PagedBody = """
            {
              "nft_id": "00080000B5F762798A53D543A014CAF8B297CFF8F2F937E816E5DA9C00000001",
              "offers": [
                {
                  "amount": "1000",
                  "flags": 1,
                  "nft_offer_index": "0B4B4E1E7D2B1F4DE8F1E5C9B2A1F0E7D6C5B4A39281706F5E4D3C2B1A090807",
                  "owner": "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"
                }
              ],
              "limit": 50,
              "marker": "1C5D3A0E04A0EA5DF4CB1C46BF5E40A00A4B6F7AE1B5E50B3BA5A0D8A9A3E8F1"
            }
            """;

        private const string LastPageBody = """
            {
              "nft_id": "00080000B5F762798A53D543A014CAF8B297CFF8F2F937E816E5DA9C00000001",
              "offers": []
            }
            """;

        [TestMethod]
        public void TestUNFTSellOffersRequestCarriesLimitAndMarker()
        {
            NFTSellOffersRequest request = new NFTSellOffersRequest(TokenId)
            {
                LedgerIndex = new LedgerIndex(7),
                Limit = 50,
                Marker = Marker,
            };

            string json = JsonSerializer.Serialize(request, XrplJsonOptions.Default);

            StringAssert.Contains(json, "\"command\":\"nft_sell_offers\"");
            StringAssert.Contains(json, "\"limit\":50");
            StringAssert.Contains(json, "\"marker\":\"" + Marker + "\"");
            StringAssert.Contains(json, "\"ledger_index\":7");
        }

        [TestMethod]
        public void TestUNFTBuyOffersRequestCarriesLimitAndMarker()
        {
            NFTBuyOffersRequest request = new NFTBuyOffersRequest(TokenId) { Limit = 50, Marker = Marker };

            string json = JsonSerializer.Serialize(request, XrplJsonOptions.Default);

            StringAssert.Contains(json, "\"command\":\"nft_buy_offers\"");
            StringAssert.Contains(json, "\"limit\":50");
            StringAssert.Contains(json, "\"marker\":\"" + Marker + "\"");
        }

        [TestMethod]
        public void TestUNFTOffersRequestOmitsUnsetPaging()
        {
            string json = JsonSerializer.Serialize(new NFTBuyOffersRequest(TokenId), XrplJsonOptions.Default);

            Assert.IsFalse(json.Contains("\"limit\""), json);
            Assert.IsFalse(json.Contains("\"marker\""), json);
        }

        [TestMethod]
        public void TestUNFTSellOffersReadsLimitAndMarker()
        {
            NFTSellOffers page = JsonSerializer.Deserialize<NFTSellOffers>(PagedBody, XrplJsonOptions.Default);

            Assert.AreEqual(50, page.Limit);
            Assert.AreEqual(Marker, page.Marker);
            Assert.AreEqual(1, page.Offers.Count);
        }

        [TestMethod]
        public void TestUNFTBuyOffersReadsLimitAndMarker()
        {
            NFTBuyOffers page = JsonSerializer.Deserialize<NFTBuyOffers>(PagedBody, XrplJsonOptions.Default);

            Assert.AreEqual(TokenId, page.TokenID);
            Assert.AreEqual(50, page.Limit);
            Assert.AreEqual(Marker, page.Marker);
        }

        [TestMethod]
        public void TestUNFTOffersLastPageHasNoMarker()
        {
            NFTBuyOffers buy = JsonSerializer.Deserialize<NFTBuyOffers>(LastPageBody, XrplJsonOptions.Default);
            NFTSellOffers sell = JsonSerializer.Deserialize<NFTSellOffers>(LastPageBody, XrplJsonOptions.Default);

            Assert.IsNull(buy.Limit);
            Assert.IsNull(buy.Marker);
            Assert.IsNull(sell.Limit);
            Assert.IsNull(sell.Marker);
        }
    }
}
