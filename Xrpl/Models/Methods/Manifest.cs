// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/manifest.ts
// https://github.com/XRPLF/rippled/blob/develop/src/xrpld/rpc/handlers/server_info/Manifest.cpp

using System;
using System.Text.Json.Serialization;

namespace Xrpl.Models.Methods;

/// <summary>
/// The <c>manifest</c> method reports the current manifest of a validator: the block of data with which
/// the validator's master key authorizes its ephemeral signing key. Returns a <see cref="ManifestResponse"/>.
/// </summary>
public class ManifestRequest : BaseRequest
{
    /// <summary>
    /// Creates an empty request; set <see cref="PublicKey"/>.
    /// </summary>
    public ManifestRequest() { Command = "manifest"; }

    /// <summary>
    /// Creates a request for the manifest of the validator with <paramref name="publicKey"/>.
    /// </summary>
    public ManifestRequest(string publicKey) : this()
    {
        PublicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
    }

    /// <summary>
    /// The validator's master or ephemeral public key, base58-encoded as a node public key (<c>n...</c>).
    /// </summary>
    [JsonPropertyName("public_key")]
    public string PublicKey { get; set; }
}

/// <summary>
/// Response expected from a <see cref="ManifestRequest"/>.
/// </summary>
/// <remarks>
/// A key the node has no manifest for is not an error. rippled then answers with <see cref="Details"/>
/// naming the requested key as both master and ephemeral key, and no <see cref="Manifest"/> or
/// <see cref="ManifestDetails.Seq"/>: its manifest cache maps an unknown key to itself.
/// </remarks>
public class ManifestResponse : BaseMethodResult
{
    /// <summary>
    /// The public key the request asked about, as given.
    /// </summary>
    [JsonPropertyName("requested")]
    public string Requested { get; set; }

    /// <summary>
    /// The full manifest, base64-encoded. Absent when the node knows no manifest for the key.
    /// </summary>
    [JsonPropertyName("manifest")]
    public string Manifest { get; set; }

    /// <summary>
    /// The decoded parts of the manifest; for a key with no manifest, the key itself as master and ephemeral key.
    /// </summary>
    [JsonPropertyName("details")]
    public ManifestDetails Details { get; set; }
}

/// <summary>
/// The decoded parts of a validator manifest.
/// </summary>
public class ManifestDetails
{
    /// <summary>
    /// The validator's master public key.
    /// </summary>
    [JsonPropertyName("master_key")]
    public string MasterKey { get; set; }

    /// <summary>
    /// The ephemeral signing key the manifest authorizes.
    /// </summary>
    [JsonPropertyName("ephemeral_key")]
    public string EphemeralKey { get; set; }

    /// <summary>
    /// The sequence number of the manifest. Absent when the node knows no manifest for the key.
    /// </summary>
    [JsonPropertyName("seq")]
    public uint? Seq { get; set; }

    /// <summary>
    /// The domain the validator claims, when its manifest names one.
    /// </summary>
    [JsonPropertyName("domain")]
    public string Domain { get; set; }
}
