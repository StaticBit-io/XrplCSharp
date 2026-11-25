using System.Text.Json.Serialization;

using System;
using System.Collections.Generic;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;

//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/nftSellOffers.ts
namespace Xrpl.Models.Methods
{
    /// <summary>
    /// Response expected from an  <see cref="NFTSellOffersRequest"/> .
    /// </summary>
    public class NFTSellOffers : BaseMethodResult//todo rename to NFTSellOffersResponse extends BaseResponse
    {

        /// <summary>
        /// A list of sell offers for the specified NFToken.
        /// </summary>
        [JsonPropertyName("offers")]
        public List<NFTOffer> Offers { get; set; }

        /// <summary>
        /// The token ID of the NFToken to which these offers pertain.
        /// </summary>
        [JsonPropertyName("nft_id")]
        public string TokenID { get; set; }

        /// <summary>
        /// The page size the server applied. Present only together with <see cref="Marker"/>,
        /// that is, when more sell offers remain after this page.
        /// </summary>
        [JsonPropertyName("limit")]
        public int? Limit { get; set; }

        /// <summary>
        /// Where the next page starts: the ledger index (hex object key) of the first
        /// NFTokenOffer of that page, which the next request passes back in
        /// <see cref="NFTSellOffersRequest.Marker"/>. <c>null</c> on the last page.
        /// </summary>
        /// <remarks>
        /// The key identifies an offer, not a ledger version, and the response does not say which
        /// ledger it was read from. See <see cref="NFTSellOffersRequest.Marker"/> for how to page
        /// consistently.
        /// </remarks>
        [JsonPropertyName("marker")]
        public string Marker { get; set; }
    }

    public class NFTOffer : BaseMethodResult, IDestination
    {
        [JsonPropertyName("amount")]
        [JsonConverter(typeof(CurrencyConverter))]
        public Currency Amount { get; set; }

        [JsonPropertyName("flags")]
        public uint Flags { get; set; }

        [JsonPropertyName("nft_offer_index")]
        public string Index { get; set; }

        [JsonPropertyName("owner")]
        public string Owner { get; set; }

        [JsonPropertyName("destination")]
        public string Destination { get; set; }

        [JsonPropertyName("expiration")]
        [JsonConverter(typeof(RippleDateTimeConverter))]
        public DateTime? Expiration { get; set; }
    }

    /// <summary>
    /// The `nft_sell_offers` method retrieves all of sell offers for the specified  NFToken.
    /// </summary>
    public class NFTSellOffersRequest : BaseLedgerRequest
    {
        public NFTSellOffersRequest(string nft_id)
        {
            NFTokenID = nft_id;
            Command = "nft_sell_offers";
        }

        /// <summary>
        /// The unique identifier of an NFToken.<br/>
        /// The request returns sell offers for this NFToken.
        /// </summary>
        [JsonPropertyName("nft_id")]
        public string NFTokenID { get; set; }

        /// <summary>
        /// The maximum number of offers to return. rippled clamps it to 50..500 and uses 250 when
        /// it is omitted; on an admin connection the value is not clamped. Offers come in the
        /// directory's order, not sorted by amount.
        /// </summary>
        [JsonPropertyName("limit")]
        public int? Limit { get; set; }

        /// <summary>
        /// The <see cref="NFTSellOffers.Marker"/> of the previous page, to resume where it stopped.
        /// </summary>
        /// <remarks>
        /// To read every page from one ledger version, pick a ledger before the first request (for
        /// example the validated ledger index from <c>server_info</c>) and set the same
        /// <see cref="BaseLedgerRequest.LedgerIndex"/> on every page: the response carries no
        /// <c>ledger_index</c> to take it from. Without a fixed ledger each page reads a newer
        /// one, and a marker whose offer has since been accepted or cancelled is rejected with
        /// <c>invalidParams</c>. So is a marker taken from the other side (buy offers).
        /// </remarks>
        [JsonPropertyName("marker")]
        public string Marker { get; set; }
    }
}
