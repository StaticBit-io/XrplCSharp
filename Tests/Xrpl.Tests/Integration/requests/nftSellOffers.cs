// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/integration/requests/nftSellOffers.ts

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration
{
    [TestClass]
    public class TestINFTSellOffers
    {
        public TestContext TestContext { get; set; }
        private static TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;

        /// <summary>
        /// Paging through more sell offers than one page holds returns each offer once,
        /// following the marker on one fixed ledger.
        /// </summary>
        [TestMethod]
        [Timeout(300000)]
        public async Task TestINFTSellOffers_PagesThroughEveryOfferOnce()
        {
            IXrplClient client = await IntegrationTestConfig.CreateClientAsync(nodeType);
            try
            {
                XrplWallet minter = XrplWallet.Generate();
                await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, minter);

                string nftId = await NFTOffersPaging.MintAsync(client, minter);
                await NFTOffersPaging.CreateOffersAsync(client, NFTOffersPaging.OfferCount, minter, nftId, minter.ClassicAddress, sell: true);
                HashSet<string> expected = await NFTOffersPaging.OfferKeysAsync(client, minter.ClassicAddress);
                Assert.AreEqual(NFTOffersPaging.OfferCount, expected.Count);

                LedgerIndex ledger = await NFTOffersPaging.ValidatedLedgerAsync(client);
                List<(List<NFTOffer>, int?, string)> pages = new List<(List<NFTOffer>, int?, string)>();
                string marker = null;
                do
                {
                    NFTSellOffersRequest request = new NFTSellOffersRequest(nftId)
                    {
                        LedgerIndex = ledger,
                        Limit = NFTOffersPaging.PageSize,
                        Marker = marker,
                    };
                    NFTSellOffers page = await client.NFTSellOffers(request).Typed();
                    Assert.AreEqual(nftId, page.TokenID);
                    pages.Add((page.Offers, page.Limit, page.Marker));
                    marker = page.Marker;
                }
                while (marker != null && pages.Count <= 3);

                NFTOffersPaging.AssertPages(pages, expected);
            }
            finally
            {
                client.Dispose();
            }
        }

        /// <summary>
        /// A marker taken from the buy side of the same NFT names an offer that is not in this
        /// directory, which rippled rejects.
        /// </summary>
        [TestMethod]
        [Timeout(120000)]
        public async Task TestINFTSellOffers_RejectsAMarkerFromTheBuySide()
        {
            IXrplClient client = await IntegrationTestConfig.CreateClientAsync(nodeType);
            try
            {
                XrplWallet minter = XrplWallet.Generate();
                XrplWallet buyer = XrplWallet.Generate();
                await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, minter, buyer);

                string nftId = await NFTOffersPaging.MintAsync(client, minter);
                XrplWallet otherMaker = buyer;
                await NFTOffersPaging.CreateOffersAsync(client, 1, otherMaker, nftId, minter.ClassicAddress, sell: false);
                await NFTOffersPaging.CreateOffersAsync(client, 1, minter, nftId, minter.ClassicAddress, sell: true);
                string otherSideKey = (await NFTOffersPaging.OfferKeysAsync(client, otherMaker.ClassicAddress)).Single();

                NFTSellOffersRequest request = new NFTSellOffersRequest(nftId)
                {
                    LedgerIndex = await NFTOffersPaging.ValidatedLedgerAsync(client),
                    Marker = otherSideKey,
                };
                RippledException error = await Assert.ThrowsAsync<RippledException>(
                    () => client.NFTSellOffers(request).Typed());
                StringAssert.Contains(error.Message, "invalidParams");
            }
            finally
            {
                client.Dispose();
            }
        }
    }
}
