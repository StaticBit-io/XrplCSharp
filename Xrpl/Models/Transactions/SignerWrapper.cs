using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Xrpl.Models.Transactions;

/// <summary>
/// Transaction signer
/// </summary>
public class SignerWrapper
{
    public InnerSigner Signer { get; set; }
}
/// <summary>
/// The Signers field contains a multi-signature, which has signatures from up to 8 key pairs, that together should authorize the transaction.
/// </summary>
public class InnerSigner
{
    /// <summary>
    /// The address associated with this signature, as it appears in the signer list.
    /// </summary>
    public string Account { get; set; }

    /// <summary>
    /// A signature for this transaction, verifiable using the SigningPubKey.
    /// </summary>
    [JsonPropertyName("TxnSignature")]
    public string TransactionSignature { get; set; }

    /// <summary>
    /// The public key used to create this signature.
    /// </summary>
    [JsonPropertyName("SigningPubKey")]
    public string SigningPublicKey { get; set; }
}

public static class SignerExtensions
{
    public static IEnumerable<InnerSigner> AsInner(this IEnumerable<SignerWrapper> signers) =>
        signers?.Select(s => s.Signer) ?? Enumerable.Empty<InnerSigner>();

    public static InnerSigner? FirstByAccount(
        this IEnumerable<SignerWrapper> signers,
        string account
    ) =>
        signers?
            .Select(s => s.Signer)
            .FirstOrDefault(s =>
                s.Account.Equals(account, StringComparison.OrdinalIgnoreCase));
}