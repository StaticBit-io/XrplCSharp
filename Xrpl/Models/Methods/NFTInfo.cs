using System.Text.Json.Serialization;

// https://github.com/XRPLF/clio/blob/develop/src/rpc/handlers/NFTInfo.cpp
namespace Xrpl.Models.Methods
{
    /// <summary>
    /// Response expected from an <see cref="NFTInfoRequest"/>.
    /// </summary>
    public class NFTInfo : BaseMethodResult
    {
        /// <summary>
        /// The token this describes.
        /// </summary>
        [JsonPropertyName("nft_id")]
        public string NFTokenID { get; set; }

        /// <summary>
        /// The ledger the answer was read from.
        /// </summary>
        [JsonPropertyName("ledger_index")]
        public uint? LedgerIndex { get; set; }

        /// <summary>
        /// Who holds the token now.
        /// </summary>
        /// <remarks>
        /// The reason this command is worth having. An owner cannot be worked out from
        /// <see cref="NFTSellOffers"/>: selling a token does not remove offers for it from the
        /// ledger, so offers made by a previous owner keep being returned long after they can be
        /// accepted, and the current owner may have made none at all.
        /// </remarks>
        [JsonPropertyName("owner")]
        public string Owner { get; set; }

        /// <summary>
        /// Whether the token has been burned, in which case it has no owner any more.
        /// </summary>
        [JsonPropertyName("is_burned")]
        public bool? IsBurned { get; set; }

        /// <summary>
        /// The flags the token was minted with.
        /// </summary>
        [JsonPropertyName("flags")]
        public uint? Flags { get; set; }

        /// <summary>
        /// The issuer's cut of secondary sales, in units of 1/100 000.
        /// </summary>
        [JsonPropertyName("transfer_fee")]
        public uint? TransferFee { get; set; }

        /// <summary>
        /// The account that minted the token.
        /// </summary>
        [JsonPropertyName("issuer")]
        public string Issuer { get; set; }

        /// <summary>
        /// The issuer's own grouping of their tokens.
        /// </summary>
        [JsonPropertyName("nft_taxon")]
        public uint? Taxon { get; set; }

        /// <summary>
        /// The token's sequence within that issuer and taxon.
        /// </summary>
        /// <remarks>
        /// Clio sends this as <c>nft_serial</c>; its own source notes that the documentation calls
        /// it <c>nft_sequence</c>. The name here follows what actually arrives on the wire.
        /// </remarks>
        [JsonPropertyName("nft_serial")]
        public uint? Serial { get; set; }

        /// <summary>
        /// The URI the token was minted with, as hex.
        /// </summary>
        [JsonPropertyName("uri")]
        public string URI { get; set; }

        /// <summary>
        /// Whether the answer comes from a validated ledger.
        /// </summary>
        [JsonPropertyName("validated")]
        public bool? Validated { get; set; }
    }

    /// <summary>
    /// The <c>nft_info</c> method asks who owns a token and what it was minted with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A Clio method, not a rippled one. A plain rippled node answers <c>unknownCmd</c>, which
    /// arrives as an ordinary node error rather than something special - a caller who needs to work
    /// against both can catch it and fall back.
    /// </para>
    /// <para>
    /// There is no substitute for it on a rippled node. Ownership cannot be read out of
    /// <c>nft_sell_offers</c>: a sale leaves the seller's offers in the ledger, so they keep being
    /// returned by an account that no longer owns the token, and the new owner usually has no
    /// offers at all - which is exactly the state a token is in right after being bought.
    /// </para>
    /// </remarks>
    public class NFTInfoRequest : BaseLedgerRequest
    {
        public NFTInfoRequest(string nft_id)
        {
            NFTokenID = nft_id;
            Command = "nft_info";
        }

        /// <summary>
        /// The unique identifier of the NFToken to describe.
        /// </summary>
        [JsonPropertyName("nft_id")]
        public string NFTokenID { get; set; }
    }
}
