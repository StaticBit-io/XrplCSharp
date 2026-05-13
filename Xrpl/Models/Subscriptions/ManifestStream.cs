using System.Text.Json.Serialization;

// https://xrpl.org/docs/references/http-websocket-apis/public-api-methods/subscription-methods/subscribe#manifest-stream

namespace Xrpl.Models.Subscriptions
{
    /// <summary>
    /// The manifest stream sends a message whenever the server receives a manifest
    /// (also called a validator token) from a validator it trusts.
    /// <see href="https://xrpl.org/docs/references/http-websocket-apis/public-api-methods/subscription-methods/subscribe#manifest-stream"/>
    /// </summary>
    public class ManifestStream : BaseStream
    {
        /// <summary>
        /// The master public key of the validator, in the XRP Ledger's base58 format.
        /// </summary>
        [JsonPropertyName("master_key")]
        public string MasterKey { get; set; }

        /// <summary>
        /// The signature of the manifest data, in hexadecimal.
        /// </summary>
        [JsonPropertyName("master_signature")]
        public string MasterSignature { get; set; }

        /// <summary>
        /// The sequence number of the manifest. Higher sequence numbers indicate newer manifests.
        /// </summary>
        [JsonPropertyName("seq")]
        public uint Seq { get; set; }

        /// <summary>
        /// The ephemeral signing public key for this manifest, in the XRP Ledger's base58 format.
        /// </summary>
        [JsonPropertyName("signing_key")]
        public string SigningKey { get; set; }

        /// <summary>
        /// The signature of the manifest data using the ephemeral key, in hexadecimal.
        /// </summary>
        [JsonPropertyName("signature")]
        public string Signature { get; set; }

        /// <summary>
        /// (May be omitted) The domain name associated with this validator, if any.
        /// </summary>
        [JsonPropertyName("domain")]
        public string Domain { get; set; }
    }
}
