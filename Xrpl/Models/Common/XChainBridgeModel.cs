using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Models.Common;

/// <summary>
/// Represents the XChainBridge field, which identifies a cross-chain bridge.
/// The bridge is defined by the door accounts and issued currencies on both chains.
/// </summary>
public class XChainBridgeModel
{
    /// <summary>
    /// The door account on the locking chain.
    /// </summary>
    [JsonPropertyName("LockingChainDoor")]
    public string LockingChainDoor { get; set; }

    /// <summary>
    /// The asset that is locked on the locking chain.
    /// </summary>
    [JsonPropertyName("LockingChainIssue")]
    [JsonConverter(typeof(IssuedCurrencyConverter))]
    public IssuedCurrency LockingChainIssue { get; set; }

    /// <summary>
    /// The door account on the issuing chain.
    /// </summary>
    [JsonPropertyName("IssuingChainDoor")]
    public string IssuingChainDoor { get; set; }

    /// <summary>
    /// The asset that is issued on the issuing chain.
    /// </summary>
    [JsonPropertyName("IssuingChainIssue")]
    [JsonConverter(typeof(IssuedCurrencyConverter))]
    public IssuedCurrency IssuingChainIssue { get; set; }
}
