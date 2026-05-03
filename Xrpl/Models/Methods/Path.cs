using Newtonsoft.Json;
//https://github.com/XRPLF/xrpl.js/blob/b20c05c3680d80344006d20c44b4ae1c3b0ffcac/packages/xrpl/src/models/common/index.ts#L62
//https://xrpl.org/paths.html#path-steps
namespace Xrpl.Models.Methods
{
    /// <summary>
    /// A path set is an array.<br/>
    /// Each member of the path set is another array that represents an individual path.<br/>
    /// Each member of a path is an object that specifies the step.
    /// </summary>
    public class Path //todo rename to path steps?
    {
        /// <summary>
        /// (Optional) If present, this path step represents rippling through the specified address.<br/>
        /// MUST NOT be provided if this step specifies the currency or issuer fields.
        /// </summary>
        [JsonProperty("account")]
        public string Account { get; set; }

        /// <summary>
        /// (Optional) If present, this path step represents changing currencies through an order book.<br/>
        /// The currency specified indicates the new currency.<br/>
        /// MUST NOT be provided if this step specifies the account field.
        /// </summary>
        [JsonProperty("currency")]
        public string CurrencyCode { get; set; }

        /// <summary>
        /// (Optional) If present, this path step represents changing currencies and this address defines the issuer of the new currency.<br/>
        /// If omitted in a step with a non-XRP currency, a previous step of the path defines the issuer.<br/>
        /// If present when currency is omitted, indicates a path step that uses an order book between same-named currencies with different issuers.<br/>
        /// MUST be omitted if the currency is XRP.<br/>
        /// MUST NOT be provided if this step specifies the account field.
        /// </summary>
        [JsonProperty("issuer")]
        public string Issuer { get; set; }

        /// <summary>
        /// (Optional) An integer bitfield indicating which fields are present in this path step.<br/>
        /// 0x01 = account, 0x10 = currency, 0x20 = issuer.
        /// </summary>
        [JsonProperty("type")]
        public int? Type { get; set; }

        /// <summary>
        /// (Optional) Hex representation of the type field.
        /// </summary>
        [JsonProperty("type_hex")]
        public string TypeHex { get; set; }
    }
}
