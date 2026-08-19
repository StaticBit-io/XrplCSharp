using System.Text.Json.Serialization;
using Xrpl.Models.Enums;
//https://github.com/XRPLF/xrpl.js/blob/b20c05c3680d80344006d20c44b4ae1c3b0ffcac/packages/xrpl/src/models/common/index.ts#L62
//https://xrpl.org/paths.html#path-steps
namespace Xrpl.Models.Methods
{
    /// <summary>
    /// A path set is an array.<br/>
    /// Each member of the path set is another array that represents an individual path.<br/>
    /// Each member of a path is an object that specifies the step.
    /// </summary>
    // No unknown-field capture here on purpose: a Path step is not only read off a
    // ripple_path_find/path_find response, it is fed straight back into an outgoing
    // Payment (Transactions/Payment.cs Paths) and PathFindCreateRequest. Capturing
    // unknown members would let a field read from one node's response ride back out
    // inside a transaction the user never put it in - and worse, StObject.FromJson
    // passes signingOnly only to the top level, so a nested unknown member reaches the
    // displayed tx_json but not the signed blob. Show-one-sign-another, the exact
    // failure this branch exists to remove, arriving from the outgoing side.
    public class Path//todo rename to path steps?
    {
        /// <summary>
        /// (Optional) If present, this path step represents rippling through the specified address.<br/>
        /// MUST NOT be provided if this step specifies the currency or issuer fields.
        /// </summary>
        [JsonPropertyName("account")]
        public string Account { get; set; }

        /// <summary>
        /// (Optional) If present, this path step represents changing currencies through an order book.<br/>
        /// The currency specified indicates the new currency.<br/>
        /// MUST NOT be provided if this step specifies the account field.<br/>
        /// MUST NOT be combined with the mpt_issuance_id field.
        /// </summary>
        [JsonPropertyName("currency")]
        public string CurrencyCode { get; set; }

        /// <summary>
        /// (Optional) If present, this path step represents changing currencies and this address defines the issuer of the new currency.<br/>
        /// If omitted in a step with a non-XRP currency, a previous step of the path defines the issuer.<br/>
        /// If present when currency is omitted, indicates a path step that uses an order book between same-named currencies with different issuers.<br/>
        /// MUST be omitted if the currency is XRP.<br/>
        /// MUST NOT be provided if this step specifies the account field.
        /// </summary>
        [JsonPropertyName("issuer")]
        public string Issuer { get; set; }

        /// <summary>
        /// (Optional) If present, this path step represents changing assets through an MPT order book.<br/>
        /// Requires rippled 3.2.0+ with the MPTokensV2 amendment enabled.<br/>
        /// MUST NOT be combined with the currency field.
        /// </summary>
        [JsonPropertyName("mpt_issuance_id")]
        public string MPTokenIssuanceID { get; set; }

        /// <summary>
        /// (Optional) A bitfield indicating which fields are present in this path step.<br/>
        /// Serialized as the number rippled sends: 0x01 account, 0x10 currency, 0x20 issuer,
        /// 0x40 mpt_issuance_id — a value the enum does not declare is preserved as-is.<br/>
        /// The XRPL documentation marks the field as deprecated, but every rippled version still emits it on
        /// every path step of every response.<br/>
        /// Read-only in practice: the value is ignored both by rippled when it parses a submitted transaction
        /// and by the binary codec, which derives the byte from the fields actually present in the step.
        /// </summary>
        [JsonPropertyName("type")]
        public PathStepType? Type { get; set; }
    }
}
