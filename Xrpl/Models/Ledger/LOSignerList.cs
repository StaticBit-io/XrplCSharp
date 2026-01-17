using System;
using System.Collections.Generic;
using System.Text;

using Newtonsoft.Json;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/ledger/SignerList.ts

namespace Xrpl.Models.Ledger;

/// <summary>
/// The SignerList object type represents a list of parties that, as a group,
/// are authorized to sign a transaction in place of an individual account. <br/>
/// You can create, replace, or remove a signer list using a SignerListSet transaction.
/// </summary>
public class LOSignerList : BaseLedgerEntry
{
    /// <summary> create base object </summary>
    public LOSignerList() => LedgerEntryType = LedgerEntryType.SignerList;

    /// <summary>
    /// A bit-map of Boolean flags enabled for this signer list.<br/>
    /// For more information, see SignerList Flags.
    /// </summary>
    public uint Flags { get; set; }

    /// <summary>
    /// A hint indicating which page of the owner directory links to this object, in case the directory consists of multiple pages.
    /// </summary>
    public string OwnerNode { get; set; }

    /// <summary>
    /// A target number for signer weights.<br/>
    /// To produce a valid signature for the owner of this SignerList,
    /// the signers must provide valid signatures whose weights sum to this value or more.
    /// </summary>
    public uint SignerQuorum { get; set; }

    /// <summary>
    /// An array of Signer Entry objects representing the parties who are part of this signer list.
    /// </summary>
    public List<SignerEntryWrapper> SignerEntries { get; set; }

    /// <summary>
    /// An ID for this signer list.<br/>
    /// Currently always set to 0.<br/>
    /// If a future amendment allows multiple signer lists for an account, this may change.
    /// </summary>
    public uint SignerListId { get; set; }

    /// <summary>
    /// The identifying hash of the transaction that most recently modified this object.
    /// </summary>
    public string PreviousTxnID { get; set; }

    /// <summary>
    /// The index of the ledger that contains the transaction that most recently modified this object.
    /// </summary>
    public uint PreviousTxnLgrSeq { get; set; }
}

public class SignerEntryWrapper
{
    public SignerEntry SignerEntry { get; set; }
}

//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/common/index.ts#L67
/// <summary>
/// The object that describes the signer in SignerEntries.
/// </summary>
public class SignerEntry
{
    public string Account { get; set; }

    public ushort SignerWeight { get; set; }

    private string _walletLocator;

    /// <summary>
    /// An arbitrary 256-bit (32-byte) field used to identify the signer.
    /// Always stored internally as HEX (UInt256).
    /// </summary>
    public string WalletLocator
    {
        get => _walletLocator;
        set => _walletLocator = NormalizeWalletLocator(value);
    }

    /// <summary>
    /// Decoded human-readable value (UTF-8, trimmed by 0x00).
    /// </summary>
    [JsonIgnore]
    public string WalletLocatorValue =>
        string.IsNullOrEmpty(_walletLocator)
            ? null
            : FromWalletLocator(_walletLocator);

    // =======================
    // Helpers
    // =======================

    private static string NormalizeWalletLocator(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        value = value.Trim();

        // 1️⃣ Если это уже валидный UInt256 HEX — принимаем как есть
        if (IsValidWalletLocatorHex(value))
            return value.ToUpperInvariant();

        // 2️⃣ Иначе считаем, что это текст → кодируем
        return ToWalletLocator(value);
    }

    private static bool IsValidWalletLocatorHex(string value)
    {
        if (value.Length != 64)
            return false;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool isHex =
                (c >= '0' && c <= '9') ||
                (c >= 'a' && c <= 'f') ||
                (c >= 'A' && c <= 'F');

            if (!isHex)
                return false;
        }

        return true;
    }

    private static string ToWalletLocator(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        if (bytes.Length > 32)
            throw new ArgumentException(
                "Text is too long for WalletLocator (max 32 bytes UTF-8)"
            );

        var buffer = new byte[32];
        Array.Copy(bytes, buffer, bytes.Length);

        return Convert.ToHexString(buffer);
    }

    private static string FromWalletLocator(string hex)
    {
        var bytes = Convert.FromHexString(hex);

        int len = Array.IndexOf(bytes, (byte)0x00);
        if (len < 0)
            len = bytes.Length;

        return Encoding.UTF8.GetString(bytes, 0, len);
    }
}
