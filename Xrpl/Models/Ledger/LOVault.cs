using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Models.Ledger;

/// <summary>
/// Flags for the Vault ledger object.
/// </summary>
[Flags]
public enum VaultLedgerFlags : uint
{
    /// <summary>
    /// If set, indicates that the vault is private.
    /// This flag can only be set when creating the vault.
    /// </summary>
    lsfVaultPrivate = 0x00010000,
}

/// <summary>
/// Recommended structure for the Vault Data field.
/// The JSON is whitespace-removed and hex-encoded (max 256 bytes).
/// </summary>
public class VaultDataFormat
{
    /// <summary>
    /// Human-readable vault identifier reflecting its strategy (short key: "n").
    /// </summary>
    [JsonPropertyName("n")]
    public string Name { get; set; }

    /// <summary>
    /// Associated website URL (omit protocol prefix and www, short key: "w").
    /// </summary>
    [JsonPropertyName("w")]
    public string Website { get; set; }

    /// <summary>
    /// Serializes to compact JSON, then hex-encodes for the Data field.
    /// </summary>
    public string ToHex()
    {
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        return Convert.ToHexString(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Attempts to deserialize a VaultDataFormat from a hex-encoded Data field.
    /// </summary>
    /// <param name="hex">The hex string from the Data field.</param>
    /// <returns>Deserialized <see cref="VaultDataFormat"/> or null if parsing fails.</returns>
    public static VaultDataFormat FromHex(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        try
        {
            byte[] bytes = Convert.FromHexString(hex);
            string json = Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Deserialize<VaultDataFormat>(json);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// A Vault ledger object represents a pooled asset vault.
/// </summary>
/// <remarks>Requires the Vault amendment (XLS-65d). This feature is in draft and subject to change.</remarks>
public class LOVault : BaseLedgerEntry
{
    public LOVault()
    {
        LedgerEntryType = LedgerEntryType.Vault;
    }

    /// <summary>
    /// The address of the vault's pseudo-account.
    /// </summary>
    [JsonPropertyName("Account")]
    public string Account { get; init; }

    /// <summary>
    /// The account address of the Vault Owner.
    /// </summary>
    [JsonPropertyName("Owner")]
    public string Owner { get; init; }

    /// <summary>
    /// The primary asset held by the vault. Supports XRP, trust line tokens, and MPTs.
    /// </summary>
    [JsonPropertyName("Asset")]
    [JsonConverter(typeof(IssuedCurrencyConverter))]
    public IssuedCurrency Asset { get; init; }

    /// <summary>
    /// The total value of the vault (STNumber type, serialized as string in JSON).
    /// </summary>
    [JsonPropertyName("AssetsTotal")]
    public string AssetsTotal { get; init; }

    /// <summary>
    /// The asset amount that is available in the vault (STNumber type, serialized as string in JSON).
    /// </summary>
    [JsonPropertyName("AssetsAvailable")]
    public string AssetsAvailable { get; init; }

    /// <summary>
    /// The maximum amount of assets the vault can hold, or 0 for no limit (STNumber type).
    /// </summary>
    [JsonPropertyName("AssetsMaximum")]
    public string AssetsMaximum { get; init; }

    /// <summary>
    /// The potential loss amount that is not yet realized, expressed as the vault's asset (STNumber type).
    /// </summary>
    [JsonPropertyName("LossUnrealized")]
    public string LossUnrealized { get; init; }

    /// <summary>
    /// The identifier of the share MPTokenIssuance object.
    /// </summary>
    [JsonPropertyName("ShareMPTID")]
    public string ShareMPTID { get; init; }

    /// <summary>
    /// The withdrawal policy for the vault (UInt8).
    /// </summary>
    [JsonPropertyName("WithdrawalPolicy")]
    public uint? WithdrawalPolicy { get; init; }

    /// <summary>
    /// The scale (decimal precision) for share calculations.
    /// 0-18 for trust line tokens; fixed at 0 for XRP/MPTs.
    /// </summary>
    [JsonPropertyName("Scale")]
    public uint? Scale { get; init; }

    /// <summary>
    /// Arbitrary hex-encoded data associated with the vault, limited to 256 bytes.
    /// Use <see cref="DataParsed"/> for a human-readable representation.
    /// </summary>
    [JsonPropertyName("Data")]
    public string Data { get; init; }

    /// <summary>
    /// Decoded human-readable value of the Data field.
    /// Follows the recommended format: {"n":"name","w":"website"}.
    /// Returns null if parsing fails or Data is empty.
    /// </summary>
    [JsonIgnore]
    public VaultDataFormat DataParsed => VaultDataFormat.FromHex(Data);

    /// <summary>
    /// Decoded human-readable UTF-8 string of the Data field.
    /// </summary>
    [JsonIgnore]
    public string DataRaw
    {
        get
        {
            if (string.IsNullOrEmpty(Data)) return null;
            try { return Encoding.UTF8.GetString(Convert.FromHexString(Data)); }
            catch { return null; }
        }
    }

    /// <summary>
    /// The ID of a permissioned domain associated with the vault.
    /// </summary>
    [JsonPropertyName("DomainID")]
    public string DomainID { get; init; }

    /// <summary>
    /// The transaction sequence number that created the vault.
    /// </summary>
    [JsonPropertyName("Sequence")]
    public uint? Sequence { get; init; }

    /// <summary>
    /// A hint indicating which page of the owner's directory links to this object (UInt64).
    /// </summary>
    [JsonPropertyName("OwnerNode")]
    public string OwnerNode { get; init; }

    /// <summary>
    /// The identifying hash of the transaction that most recently modified this object.
    /// </summary>
    [JsonPropertyName("PreviousTxnID")]
    public string PreviousTxnID { get; init; }

    /// <summary>
    /// The index of the ledger that contains the transaction that most recently modified this object.
    /// </summary>
    [JsonPropertyName("PreviousTxnLgrSeq")]
    public uint? PreviousTxnLgrSeq { get; init; }
}
