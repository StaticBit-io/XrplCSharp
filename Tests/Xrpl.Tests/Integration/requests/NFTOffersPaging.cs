using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration
{
    /// <summary>
    /// Shared setup for the nft_buy_offers / nft_sell_offers paging tests: one NFT with more
    /// offers than a single page holds.
    /// </summary>
    internal static class NFTOffersPaging
    {
        /// <summary>The smallest limit rippled honours on a non-admin connection.</summary>
        public const int PageSize = 50;

        /// <summary>More than one page at <see cref="PageSize"/>, so the second page is partial.</summary>
        public const int OfferCount = PageSize + 5;

        /// <summary>
        /// Mints a transferable NFT from <paramref name="minter"/>.
        /// </summary>
        public static async Task<string> MintAsync(IXrplClient client, XrplWallet minter)
        {
            NFTokenMint mint = new NFTokenMint
            {
                Account = minter.ClassicAddress,
                NFTokenTaxon = 0,
                Flags = NFTokenMintFlags.tfTransferable,
            };
            await Utils.TestTransaction(client, mint.ToDictionary(), minter);

            AccountNFTs nfts = await client.AccountNFTs(new AccountNFTsRequest(minter.ClassicAddress)).Typed();
            return nfts.NFTs.Single().NFTokenID;
        }

        /// <summary>
        /// Submits <paramref name="count"/> offers from <paramref name="maker"/> and waits until the
        /// last one is in a validated ledger.
        /// </summary>
        /// <remarks>
        /// Each offer is one transaction; going through <see cref="Utils.TestTransaction(IXrplClient, Dictionary{string, object}, XrplWallet)"/>
        /// would close two ledgers per offer. The offers go into the open ledger back to back,
        /// which autofill sequences correctly, and a ledger is closed every ten.
        /// </remarks>
        public static async Task CreateOffersAsync(
            IXrplClient client, int count, XrplWallet maker, string nftId, string owner, bool sell)
        {
            Submit last = null;
            for (int i = 0; i < count; i++)
            {
                NFTokenCreateOffer offer = new NFTokenCreateOffer
                {
                    Account = maker.ClassicAddress,
                    NFTokenID = nftId,
                    Amount = new Currency { Value = (1000 + i).ToString(), CurrencyCode = "XRP" },
                    Owner = sell ? null : owner,
                    Flags = sell ? NFTokenCreateOfferFlags.tfSellNFToken : null,
                };
                last = await client.Submit(offer.ToDictionary(), maker);
                Assert.AreEqual("tesSUCCESS", last.EngineResult, $"offer {i} was not accepted");

                if (i % 10 == 9)
                    await Utils.LedgerAccept(client);
            }

            await Utils.LedgerAccept(client);
            if (last.TxJson is JsonObject txJson)
                txJson.Remove("hash");
            await Utils.VerifySubmittedTransaction(client, last.TxJson);
        }

        /// <summary>
        /// The keys of the NFTokenOffer objects <paramref name="maker"/> owns.
        /// </summary>
        public static async Task<HashSet<string>> OfferKeysAsync(IXrplClient client, string maker)
        {
            AccountObjects objects = await client.AccountObjects(
                new AccountObjectsRequest(maker) { Type = LedgerEntryType.NFTokenOffer }).Typed();
            return objects.AccountObjectList.OfType<LONFTokenOffer>().Select(o => o.Index).ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>
        /// The ledger every page is read from. The responses carry no ledger_index, so it has to
        /// be chosen before the first request.
        /// </summary>
        public static async Task<LedgerIndex> ValidatedLedgerAsync(IXrplClient client)
        {
            ServerInfo info = await client.ServerInfo(new ServerInfoRequest()).Typed();
            return new LedgerIndex((uint)info.Info.ValidatedLedger.Sequence);
        }

        /// <summary>
        /// Checks the pages a paging loop collected: every page but the last is full and carries a
        /// marker, the last carries none, and the offers seen are exactly the expected ones, once each.
        /// </summary>
        public static void AssertPages(
            IReadOnlyList<(List<NFTOffer> Offers, int? Limit, string Marker)> pages, HashSet<string> expected)
        {
            Assert.AreEqual(2, pages.Count, "55 offers at a page size of 50 are two pages");

            (List<NFTOffer> first, int? firstLimit, string firstMarker) = pages[0];
            Assert.AreEqual(PageSize, first.Count);
            Assert.AreEqual(PageSize, firstLimit);
            Assert.IsFalse(string.IsNullOrEmpty(firstMarker), "a full page must carry a marker");

            (List<NFTOffer> last, int? lastLimit, string lastMarker) = pages[1];
            Assert.AreEqual(OfferCount - PageSize, last.Count);
            Assert.IsNull(lastLimit, "limit is written only together with a marker");
            Assert.IsNull(lastMarker, "the last page carries no marker");

            Assert.AreEqual(last[0].Index, firstMarker, "the marker is the key of the next page's first offer");

            List<string> seen = pages.SelectMany(p => p.Offers).Select(o => o.Index).ToList();
            Assert.AreEqual(seen.Count, seen.Distinct(StringComparer.Ordinal).Count(), "an offer came back twice");
            Assert.IsTrue(expected.SetEquals(seen), "the pages must return every offer the maker owns");
        }
    }
}
