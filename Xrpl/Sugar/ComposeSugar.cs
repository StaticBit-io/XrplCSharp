#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.BinaryCodec;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Wallet;

namespace Xrpl.Sugar
{
    /// <summary>
    /// Ledger-driven signature composition (#43): routes portable multisig
    /// Signer entries into tx.Signers, SponsorSignature.Signers or
    /// CounterpartySignature.Signers by looking up the SignerLists of the
    /// transaction's Account, Sponsor and (for LoanSet) Counterparty.
    /// </summary>
    public static class ComposeSugar
    {
        /// <summary>
        /// Composes a fully signed transaction from partially signed blobs,
        /// resolving each Signer entry's section from the ledger SignerLists.
        /// A signer present in more than one list is an explicit error — use the
        /// offline <see cref="SignatureComposer.ComposeSignatures"/> overload
        /// with explicit side signers for that case.
        /// </summary>
        public static async Task<SignatureResult> ComposeSignatures(
            this IXrplClient client,
            IEnumerable<string> partBlobs,
            CancellationToken cancellationToken = default)
        {
            List<string> parts = partBlobs?.ToList() ?? throw new ValidationException("At least one partially signed blob is required.");
            if (parts.Count == 0)
                throw new ValidationException("At least one partially signed blob is required.");

            JsonObject first = XrplBinaryCodec.Decode(parts[0]).AsObject();
            string account = first["Account"]?.GetValue<string>()
                ?? throw new ValidationException("Transaction is missing the Account field.");
            string? sponsor = first["Sponsor"]?.GetValue<string>();
            // XLS-66: the LoanSet borrower co-signs through CounterpartySignature, with its own
            // SignerList when it is a multisig account
            string? counterparty = string.Equals(first["TransactionType"]?.GetValue<string>(), "LoanSet", StringComparison.OrdinalIgnoreCase)
                ? first["Counterparty"]?.GetValue<string>()
                : null;

            // Which signer accounts actually appear across the parts?
            HashSet<string> seenSigners = new HashSet<string>(StringComparer.Ordinal);
            foreach (string blob in parts)
            {
                JsonObject part = XrplBinaryCodec.Decode(blob).AsObject();
                CollectSignerAccounts(part["Signers"] as JsonArray, seenSigners);
                CollectSignerAccounts(part["SponsorSignature"]?["Signers"] as JsonArray, seenSigners);
                CollectSignerAccounts(part["CounterpartySignature"]?["Signers"] as JsonArray, seenSigners);
            }

            HashSet<string> sponsorSide = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> counterpartySide = new HashSet<string>(StringComparer.Ordinal);
            LOSignerList? accountSignerList = null;
            LOSignerList? sponsorSignerList = null;
            LOSignerList? counterpartySignerList = null;
            if (seenSigners.Count > 0)
            {
                accountSignerList = await GetSignerList(client, account, cancellationToken).ConfigureAwait(false);
                sponsorSignerList = sponsor is null
                    ? null
                    : await GetSignerList(client, sponsor, cancellationToken).ConfigureAwait(false);
                counterpartySignerList = counterparty is null
                    ? null
                    : await GetSignerList(client, counterparty, cancellationToken).ConfigureAwait(false);
                HashSet<string> accountList = ToAccountSet(accountSignerList);
                HashSet<string> sponsorList = ToAccountSet(sponsorSignerList);
                HashSet<string> counterpartyList = ToAccountSet(counterpartySignerList);

                foreach (string signer in seenSigners)
                {
                    bool inAccount = accountList.Contains(signer);
                    bool inSponsor = sponsorList.Contains(signer);
                    bool inCounterparty = counterpartyList.Contains(signer);
                    int roles = (inAccount ? 1 : 0) + (inSponsor ? 1 : 0) + (inCounterparty ? 1 : 0);
                    if (roles > 1)
                        throw new ValidationException($"Ambiguous signer role for {signer}: present in more than one of the Account's, the Sponsor's and the Counterparty's SignerLists. Compose offline with explicit side signers.");
                    if (roles == 0)
                        throw new ValidationException($"Unknown signer {signer}: not in the Account's SignerList{(sponsor is null ? "" : ", the Sponsor's SignerList")}{(counterparty is null ? "" : ", the Counterparty's SignerList")}.");
                    if (inSponsor)
                        sponsorSide.Add(signer);
                    if (inCounterparty)
                        counterpartySide.Add(signer);
                }
            }

            SignatureResult composed = SignatureComposer.ComposeSignatures(parts, sponsorSide, counterpartySide);

            // Quorum pre-check by weights, for each side using the multisig form
            JsonObject result = XrplBinaryCodec.Decode(composed.TxBlob).AsObject();
            ValidateQuorum(accountSignerList, result["Signers"] as JsonArray, "Account");
            ValidateQuorum(sponsorSignerList, result["SponsorSignature"]?["Signers"] as JsonArray, "Sponsor");
            ValidateQuorum(counterpartySignerList, result["CounterpartySignature"]?["Signers"] as JsonArray, "Counterparty");

            return composed;
        }

        private static HashSet<string> ToAccountSet(LOSignerList? list) =>
            list?.SignerEntries is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(
                    list.SignerEntries.Select(w => SignerUtilities.NormalizeClassicAddress(w.SignerEntry.Account)),
                    StringComparer.Ordinal);

        /// <summary>
        /// Verifies that the collected Signer entries reach the SignerList quorum
        /// by weight — fails fast with a readable message instead of a node-side
        /// tefBAD_QUORUM.
        /// </summary>
        private static void ValidateQuorum(LOSignerList? list, JsonArray? signers, string side)
        {
            if (signers is null || signers.Count == 0 || list?.SignerEntries is null)
                return;

            Dictionary<string, ushort> weights = list.SignerEntries.ToDictionary(
                w => SignerUtilities.NormalizeClassicAddress(w.SignerEntry.Account),
                w => w.SignerEntry.SignerWeight,
                StringComparer.Ordinal);

            uint collected = 0;
            foreach (JsonNode? entry in signers)
            {
                string? signerAccount = entry?["Signer"]?["Account"]?.GetValue<string>();
                if (signerAccount is not null &&
                    weights.TryGetValue(SignerUtilities.NormalizeClassicAddress(signerAccount), out ushort weight))
                {
                    collected += weight;
                }
            }

            // SignerQuorum is a required field of a live SignerList ledger entry (never legitimately absent);
            // treat a missing value as a malformed fetch rather than silently skip the quorum check
            // (collected < null is always false, which would defeat the whole point of failing fast here).
            if (list.SignerQuorum is not { } quorum)
                throw new ValidationException($"SignerList for the {side} side is missing SignerQuorum; cannot validate collected signatures.");

            if (collected < quorum)
                throw new ValidationException($"Insufficient signatures for the {side} SignerList: collected weight {collected} of the required quorum {quorum}.");
        }

        /// <summary>
        /// Fetches the accounts of an address's SignerList (empty set when the
        /// account has no SignerList).
        /// </summary>
        internal static async Task<HashSet<string>> GetSignerListAccounts(
            IXrplClient client, string address, CancellationToken cancellationToken = default)
        {
            LOSignerList? list = await GetSignerList(client, address, cancellationToken).ConfigureAwait(false);
            return list?.SignerEntries is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(
                    list.SignerEntries.Select(w => SignerUtilities.NormalizeClassicAddress(w.SignerEntry.Account)),
                    StringComparer.Ordinal);
        }

        /// <summary>
        /// Fetches an address's SignerList ledger object, or null when absent.
        /// </summary>
        internal static async Task<LOSignerList?> GetSignerList(
            IXrplClient client, string address, CancellationToken cancellationToken = default)
        {
            AccountObjectsRequest request = new AccountObjectsRequest(address)
            {
                Type = LedgerEntryType.SignerList,
            };
            AccountObjects response = await client.AccountObjects(request, cancellationToken).Typed().ConfigureAwait(false);
            return response?.AccountObjectList?.OfType<LOSignerList>().FirstOrDefault();
        }

        private static void CollectSignerAccounts(JsonArray? signers, HashSet<string> into)
        {
            if (signers is null)
                return;
            foreach (JsonNode? entry in signers)
            {
                string? account = entry?["Signer"]?["Account"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(account))
                    into.Add(SignerUtilities.NormalizeClassicAddress(account));
            }
        }
    }
}
