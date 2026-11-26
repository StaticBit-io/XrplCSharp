// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/integration/requests/nftBuyOffers.ts

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
    public class TestINFTBuyOffers
    {
        public TestContext TestContext { get; set; }
        private static TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;

        /// <summary>
        /// Paging through more buy offers than one page holds returns each offer once,
        /// following the marker on one fixed ledger.
        /// </summary>
        [TestMethod]
        [Timeout(300000)]
        public async Task TestINFTBuyOffers_PagesThroughEveryOfferOnce()
        {
            IXrplClient client = await IntegrationTestConfig.CreateClientAsync(nodeType);
            try
            {
                XrplWallet minter = XrplWallet.Generate();
                XrplWallet buyer = XrplWallet.Generate();
                await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, minter, buyer);

                string nftId = await NFTOffersPaging.MintAsync(client, minter);
                await NFTOffersPaging.CreateOffersAsync(client, NFTOffersPaging.OfferCount, buyer, nftId, minter.ClassicAddress, sell: false);
                HashSet<string> expected = await NFTOffersPaging.OfferKeysAsync(client, buyer.ClassicAddress);
                Assert.AreEqual(NFTOffersPaging.OfferCount, expected.Count);

                LedgerIndex ledger = await NFTOffersPaging.ValidatedLedgerAsync(client);
                List<(List<NFTOffer>, int?, string)> pages = new List<(List<NFTOffer>, int?, string)>();
                string marker = null;
                do
                {
                    NFTBuyOffersRequest request = new NFTBuyOffersRequest(nftId)
                    {
                        LedgerIndex = ledger,
                        Limit = NFTOffersPaging.PageSize,
                        Marker = marker,
                    };
                    NFTBuyOffers page = await client.NFTBuyOffers(request).Typed();
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
        /// A marker taken from the sell side of the same NFT names an offer that is not in this
        /// directory, which rippled rejects.
        /// </summary>
        [TestMethod]
        [Timeout(120000)]
        public async Task TestINFTBuyOffers_RejectsAMarkerFromTheSellSide()
        {
            IXrplClient client = await IntegrationTestConfig.CreateClientAsync(nodeType);
            try
            {
                XrplWallet minter = XrplWallet.Generate();
                XrplWallet buyer = XrplWallet.Generate();
                await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, minter, buyer);

                string nftId = await NFTOffersPaging.MintAsync(client, minter);
                XrplWallet otherMaker = minter;
                await NFTOffersPaging.CreateOffersAsync(client, 1, otherMaker, nftId, minter.ClassicAddress, sell: true);
                await NFTOffersPaging.CreateOffersAsync(client, 1, buyer, nftId, minter.ClassicAddress, sell: false);
                string otherSideKey = (await NFTOffersPaging.OfferKeysAsync(client, otherMaker.ClassicAddress)).Single();

                NFTBuyOffersRequest request = new NFTBuyOffersRequest(nftId)
                {
                    LedgerIndex = await NFTOffersPaging.ValidatedLedgerAsync(client),
                    Marker = otherSideKey,
                };
                RippledException error = await Assert.ThrowsAsync<RippledException>(
                    () => client.NFTBuyOffers(request).Typed());
                StringAssert.Contains(error.Message, "invalidParams");
            }
            finally
            {
                client.Dispose();
            }
        }
    }
}
