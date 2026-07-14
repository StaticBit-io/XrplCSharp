#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

using Xrpl.BinaryCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;
using Xrpl.Utils.Hashes;

namespace Xrpl.Wallet
{
    /// <summary>
    /// Composes a fully signed transaction from partially signed blobs (#43).
    /// Devices sign with whatever keys they hold — single main signature,
    /// sponsor co-signature, or portable multisig Signer entries — and the
    /// composer routes everything into the right sections. Signer entries are
    /// section-agnostic by protocol (identical preimage for tx.Signers and
    /// SponsorSignature.Signers, see rippled STTx::checkMultiSign), so only
    /// the composer needs to know which signer belongs to which side.
    /// </summary>
    public static class SignatureComposer
    {
        private static readonly string[] SignatureFields = { "TxnSignature", "SigningPubKey", "Signers", "SponsorSignature" };

        /// <summary>
        /// Offline composition. Signer entries from accounts listed in
        /// <paramref name="sponsorSignerAccounts"/> go into
        /// <c>SponsorSignature.Signers</c>; all other entries go into
        /// <c>tx.Signers</c>. For ledger-driven routing use the
        /// <c>IXrplClient.ComposeSignatures</c> extension instead.
        /// </summary>
        /// <param name="partBlobs">Partially signed blobs of the same transaction.</param>
        /// <param name="sponsorSignerAccounts">Accounts whose Signer entries belong to the sponsor's SignerList.</param>
        public static SignatureResult ComposeSignatures(
            IEnumerable<string> partBlobs,
            IReadOnlyCollection<string>? sponsorSignerAccounts = null)
        {
            List<JsonObject> parts = partBlobs?.Select(b => XrplBinaryCodec.Decode(b).AsObject()).ToList()
                ?? throw new ValidationException("At least one partially signed blob is required.");
            if (parts.Count == 0)
                throw new ValidationException("At least one partially signed blob is required.");

            HashSet<string> sponsorSide = new HashSet<string>(
                (sponsorSignerAccounts ?? Array.Empty<string>()).Select(SignerUtilities.NormalizeClassicAddress),
                StringComparer.Ordinal);

            // All parts must agree on every non-signature field
            JsonObject canonical = parts[0].WithoutFields(SignatureFields);
            foreach (JsonObject part in parts.Skip(1))
            {
                if (!JsonNode.DeepEquals(canonical, part.WithoutFields(SignatureFields)))
                    throw new ValidationException("Incompatible transaction bodies. All parts must have identical non-signing fields.");
            }

            string? mainPubKey = null;
            string? mainSignature = null;
            SignatureObject? sponsorSingle = null;
            JsonArray accountEntries = new JsonArray();
            JsonArray sponsorEntries = new JsonArray();

            void RouteEntry(JsonNode entry)
            {
                string account = entry?["Signer"]?["Account"]?.GetValue<string>()
                    ?? throw new ValidationException("Signer entry is missing the Account field.");
                JsonArray target = sponsorSide.Contains(SignerUtilities.NormalizeClassicAddress(account))
                    ? sponsorEntries
                    : accountEntries;
                target.Add(entry.DeepClone());
            }

            foreach (JsonObject part in parts)
            {
                string? pubKey = part["SigningPubKey"]?.GetValue<string>();
                string? signature = part["TxnSignature"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(pubKey) && !string.IsNullOrEmpty(signature))
                {
                    if (mainSignature is not null && !string.Equals(mainSignature, signature, StringComparison.Ordinal))
                        throw new ValidationException("Multiple conflicting main signatures supplied.");
                    mainPubKey = pubKey;
                    mainSignature = signature;
                }

                if (part["Signers"] is JsonArray entries)
                {
                    foreach (JsonNode? entry in entries)
                        RouteEntry(entry!);
                }

                if (part["SponsorSignature"] is JsonObject sponsorJson)
                {
                    SignatureObject parsed = SignatureObject.FromJsonObject(sponsorJson);
                    if (parsed.Signers is { Count: > 0 })
                    {
                        foreach (SignatureObject inner in parsed.Signers)
                        {
                            sponsorEntries.Add(new JsonObject
                            {
                                ["Signer"] = SignatureObject
                                    .Single(inner.SigningPubKey ?? "", inner.TxnSignature ?? "", inner.Account)
                                    .ToJsonObject(),
                            });
                        }
                    }
                    else if (!string.IsNullOrEmpty(parsed.TxnSignature))
                    {
                        if (sponsorSingle is not null && !string.Equals(sponsorSingle.TxnSignature, parsed.TxnSignature, StringComparison.Ordinal))
                            throw new ValidationException("Multiple conflicting sponsor signatures supplied.");
                        sponsorSingle = parsed;
                    }
                }
            }

            if (mainSignature is not null && accountEntries.Count > 0)
                throw new ValidationException("Both a single main signature and main-side Signer entries were supplied; a transaction carries one or the other.");
            if (sponsorSingle is not null && sponsorEntries.Count > 0)
                throw new ValidationException("Both a single sponsor signature and sponsor-side Signer entries were supplied; SponsorSignature carries one or the other.");
            if (mainSignature is null && accountEntries.Count == 0)
                throw new ValidationException("No main signature material supplied. The transaction is not signed by all participants.");

            JsonObject result = canonical;
            if (mainSignature is not null)
            {
                result["SigningPubKey"] = mainPubKey;
                result["TxnSignature"] = mainSignature;
            }
            else
            {
                result["SigningPubKey"] = "";
                result["Signers"] = SignerUtilities.DedupeAndSortSigners(accountEntries);
            }

            if (sponsorSingle is not null)
            {
                result["SponsorSignature"] = sponsorSingle.ToJsonObject();
            }
            else if (sponsorEntries.Count > 0)
            {
                result["SponsorSignature"] = new JsonObject
                {
                    ["SigningPubKey"] = "",
                    ["Signers"] = SignerUtilities.DedupeAndSortSigners(sponsorEntries),
                };
            }

            string txBlob = XrplBinaryCodec.Encode(result);
            return new SignatureResult(txBlob, HashLedger.HashSignedTx(txBlob));
        }
    }
}
