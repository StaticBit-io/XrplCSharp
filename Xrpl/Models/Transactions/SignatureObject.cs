#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Xrpl.Client.Exceptions;
using Xrpl.Client.Json.Converters;

namespace Xrpl.Models.Transactions
{
    /// <summary>
    /// Typed model of an inner signature STObject shared by all co-signing
    /// surfaces: <c>SponsorSignature</c> (XLS-68), <c>CounterpartySignature</c>
    /// (LoanSet) and <c>BatchSigner</c> entries. Two alternative forms exist,
    /// mirroring rippled's generic <c>STTx::checkSign(sigObject)</c>:
    /// single-signature (<see cref="SigningPubKey"/> + <see cref="TxnSignature"/>)
    /// or multisig (a nested <see cref="Signers"/> array with an empty
    /// <see cref="SigningPubKey"/>).
    /// </summary>
    /// <remarks>
    /// Serialized through <see cref="SignatureObjectConverter"/> in the wire shape, with each
    /// nested signer wrapped in a <c>Signer</c> object. Reading does not enforce either form, so
    /// a Batch inner co-signature marker - an empty object, or one with an empty
    /// <c>SigningPubKey</c> - reads and writes back unchanged.
    /// </remarks>
    [JsonConverter(typeof(SignatureObjectConverter))]
    public class SignatureObject
    {
        /// <summary>Signer account (present on BatchSigner entries; absent on Sponsor/Counterparty).</summary>
        public string? Account { get; set; }

        /// <summary>Public key for the single-signature form; empty string marks the multisig form.</summary>
        public string? SigningPubKey { get; set; }

        /// <summary>Signature for the single-signature form.</summary>
        public string? TxnSignature { get; set; }

        /// <summary>Signer entries for the multisig form.</summary>
        public List<SignatureObject>? Signers { get; set; }

        /// <summary>True when this object carries the multisig form (empty SigningPubKey per rippled).</summary>
        public bool IsMultisig => string.IsNullOrEmpty(SigningPubKey) && Signers is { Count: > 0 };

        /// <summary>Creates the single-signature form.</summary>
        public static SignatureObject Single(string signingPubKey, string txnSignature, string? account = null) =>
            new SignatureObject { SigningPubKey = signingPubKey, TxnSignature = txnSignature, Account = account };

        /// <summary>Serializes to the wire-shape JsonObject (field order is irrelevant — the binary codec sorts canonically).</summary>
        public JsonObject ToJsonObject()
        {
            JsonObject json = new JsonObject();
            if (Account is not null)
                json["Account"] = Account;
            if (SigningPubKey is not null)
                json["SigningPubKey"] = SigningPubKey;
            if (TxnSignature is not null)
                json["TxnSignature"] = TxnSignature;
            if (Signers is { Count: > 0 })
            {
                JsonArray signers = new JsonArray();
                foreach (SignatureObject signer in Signers)
                    signers.Add(new JsonObject { ["Signer"] = signer.ToJsonObject() });
                json["Signers"] = signers;
            }
            return json;
        }

        /// <summary>Parses the wire-shape JsonObject produced by <see cref="ToJsonObject"/> / the binary codec.</summary>
        /// <exception cref="ValidationException">When the object matches neither protocol shape (see <see cref="ValidateShape"/>).</exception>
        public static SignatureObject FromJsonObject(JsonObject json)
        {
            SignatureObject result = ReadJsonObject(json);
            result.ValidateShape();
            return result;
        }

        /// <summary>
        /// Reads the wire-shape JsonObject without enforcing either protocol shape, so an unsigned
        /// object such as a Batch inner co-signature marker survives.
        /// </summary>
        internal static SignatureObject ReadJsonObject(JsonObject json)
        {
            if (json is null)
                throw new ValidationException("Signature object JSON must not be null.");

            SignatureObject result = new SignatureObject
            {
                Account = json["Account"]?.GetValue<string>(),
                SigningPubKey = json["SigningPubKey"]?.GetValue<string>(),
                TxnSignature = json["TxnSignature"]?.GetValue<string>(),
            };

            if (json["Signers"] is JsonArray signers)
            {
                result.Signers = signers
                    .Select(node => node?["Signer"]?.AsObject()
                        ?? throw new ValidationException("Signers entries must be wrapped in a Signer object."))
                    .Select(ReadJsonObject)
                    .ToList();
            }

            return result;
        }

        /// <summary>
        /// Enforces the two protocol shapes: multisig (non-empty Signers, empty
        /// SigningPubKey, no TxnSignature) or single-signature (SigningPubKey +
        /// TxnSignature, no Signers). Empty and mixed forms are rejected.
        /// </summary>
        /// <exception cref="ValidationException">When the object matches neither shape.</exception>
        public void ValidateShape()
        {
            if (Signers is not null)
            {
                if (Signers.Count == 0)
                    throw new ValidationException("Multisig signature objects require at least one Signer entry.");
                if (!string.IsNullOrEmpty(TxnSignature))
                    throw new ValidationException("Signature object mixes the single-signature and multisig forms (both TxnSignature and Signers present).");
                if (!string.IsNullOrEmpty(SigningPubKey))
                    throw new ValidationException("Multisig signature objects require an empty SigningPubKey.");
                foreach (SignatureObject signer in Signers)
                {
                    // A nested Signer is the rippled Signer STObject: Account is required
                    if (string.IsNullOrEmpty(signer.Account))
                        throw new ValidationException("Each Signer entry requires the Account field.");
                    if (string.IsNullOrEmpty(signer.SigningPubKey) || string.IsNullOrEmpty(signer.TxnSignature))
                        throw new ValidationException("Each Signer entry requires SigningPubKey and TxnSignature.");
                    if (signer.Signers is not null)
                        throw new ValidationException("A Signer entry cannot nest Signers.");
                }
                return;
            }

            if (string.IsNullOrEmpty(SigningPubKey) || string.IsNullOrEmpty(TxnSignature))
                throw new ValidationException("Single-signature objects require SigningPubKey and TxnSignature.");
        }
    }
}
