using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

// Client-side implementation of the `domain_access` check proposed in
// https://github.com/XRPLF/rippled/issues/7743.
// rippled has two checks. credentials::validDomain (CredentialHelpers.cpp), used for
// vaults, MPT issuances and the other domain-gated objects: an account has access iff
// it holds a Credential matching one of the domain's AcceptedCredentials (Issuer +
// CredentialType) that is accepted (lsfAccepted) and not expired at the checked
// ledger's close time. permissioned_dex::accountInDomain (PermissionedDEXHelpers.cpp),
// used for permissioned DEX offers and payments: the same, except that the domain
// owner is always in its domain.

namespace Xrpl.Sugar
{
    /// <summary>
    /// Which of rippled's two domain checks a <see cref="DomainAccessSugar.GetDomainAccess"/> call applies.
    /// </summary>
    public enum DomainAccessScope
    {
        /// <summary>
        /// rippled's <c>credentials::validDomain</c>, for vaults, MPT issuances and other domain-gated
        /// objects: access requires a valid credential, also for the domain owner.
        /// </summary>
        Credentials,

        /// <summary>
        /// rippled's <c>permissioned_dex::accountInDomain</c>, for offers and payments on the permissioned
        /// DEX: the domain owner has access without a credential; anyone else needs a valid one.
        /// </summary>
        PermissionedDex,
    }

    /// <summary>
    /// Status of a single credential held by the account that matches one of the
    /// domain's accepted (issuer, credential_type) pairs but is not currently valid.
    /// </summary>
    public class DomainCredentialStatus
    {
        /// <summary> The account that issued the credential. </summary>
        public string Issuer { get; set; }

        /// <summary> The credential type, as hexadecimal. </summary>
        public string CredentialType { get; set; }

        /// <summary> True if the subject account has accepted the credential. </summary>
        public bool Accepted { get; set; }

        /// <summary> True if the credential is past its Expiration at the checked ledger's close time. </summary>
        public bool Expired { get; set; }
    }

    /// <summary>
    /// Result of a permissioned domain access check.
    /// </summary>
    public class DomainAccessResult
    {
        /// <summary> True if the account passes the check of the requested <see cref="DomainAccessScope"/>. </summary>
        public bool HasAccess { get; set; }

        /// <summary>
        /// True if access was granted because the account owns the domain, which only
        /// <see cref="DomainAccessScope.PermissionedDex"/> allows. No credentials are looked up then.
        /// </summary>
        public bool IsDomainOwner { get; set; }

        /// <summary>
        /// Credentials held by the account that match the domain's accepted pairs but are
        /// not valid (not accepted or expired). Populated only when <see cref="HasAccess"/>
        /// is false; an empty list then means the account holds no matching credential at all.
        /// </summary>
        public List<DomainCredentialStatus> InvalidCredentials { get; set; } = new List<DomainCredentialStatus>();

        /// <summary> The validated ledger index the check was performed against. </summary>
        public uint LedgerIndex { get; set; }
    }

    public static class DomainAccessSugar
    {
        /// <summary>
        /// Checks whether an account has access to a permissioned domain.
        /// Performs one `ledger_entry` lookup for the domain plus up to 10 parallel
        /// `ledger_entry` credential lookups, all pinned to the same validated ledger.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="account">The account whose domain access is checked.</param>
        /// <param name="domainId">The ledger entry ID of the PermissionedDomain, as hexadecimal.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="RippleException">The entry at <paramref name="domainId"/> is not a PermissionedDomain.</exception>
        public static Task<DomainAccessResult> GetDomainAccess(this IXrplClient client, string account, string domainId, CancellationToken cancellationToken = default) =>
            GetDomainAccess(client, account, domainId, DomainAccessScope.Credentials, cancellationToken);

        /// <summary>
        /// Checks whether an account has access to a permissioned domain under the check of
        /// <paramref name="scope"/>: the credential check for vaults and other domain-gated objects,
        /// or the permissioned DEX check, which also admits the domain owner.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="account">The account whose domain access is checked.</param>
        /// <param name="domainId">The ledger entry ID of the PermissionedDomain, as hexadecimal.</param>
        /// <param name="scope">Which of rippled's two domain checks to apply.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <exception cref="RippleException">The entry at <paramref name="domainId"/> is not a PermissionedDomain.</exception>
        public static async Task<DomainAccessResult> GetDomainAccess(this IXrplClient client, string account, string domainId, DomainAccessScope scope, CancellationToken cancellationToken = default)
        {
            // Pin all lookups to one validated ledger; its close time is the reference
            // point for expiration. rippled deletes expired credentials lazily (only
            // when a transaction path touches them via verifyValidDomain), so a
            // read-path check must compare Expiration itself instead of trusting
            // the entry's existence.
            LedgerRequest ledgerRequest = new LedgerRequest { LedgerIndex = new LedgerIndex(LedgerIndexType.Validated) };
            LOLedger ledgerResponse = await client.Ledger(ledgerRequest, cancellationToken).Typed();
            // LedgerEntity is interface-typed, and the cast has two ways to go wrong that look
            // nothing alike at the call site: a response without a "ledger" member casts to null
            // and faults on the next dereference, while a binary response deserializes to
            // LedgerBinaryEntity and throws InvalidCastException. Both are protocol conditions and
            // should read as such.
            if (ledgerResponse.LedgerEntity is not LedgerEntity ledger)
            {
                throw new ValidationException(
                    "Validated ledger response did not include a JSON ledger object"
                    + (ledgerResponse.LedgerEntity is null ? "." : " - got " + ledgerResponse.LedgerEntity.GetType().Name + ", which a binary request produces."));
            }

            uint ledgerIndex = Convert.ToUInt32(ledger.LedgerIndex);
            DateTime closeTime = ledger.CloseTime
                ?? throw new RippleException("Validated ledger response did not include a close time.");
            LedgerIndex pinnedIndex = new LedgerIndex(ledgerIndex);

            LedgerEntryRequest domainRequest = new LedgerEntryRequest
            {
                Index = domainId,
                LedgerIndex = pinnedIndex
            };
            LedgerEntryResponse domainResponse = await client.LedgerEntry(domainRequest, cancellationToken).Typed();
            if (domainResponse.Node is not LOPermissionedDomain domain)
                throw new RippleException($"Ledger entry {domainId} is not a PermissionedDomain.");

            if (scope == DomainAccessScope.PermissionedDex && string.Equals(domain.Owner, account, StringComparison.Ordinal))
                return new DomainAccessResult { HasAccess = true, IsDomainOwner = true, LedgerIndex = ledgerIndex };

            List<Task<LOCredential>> lookups = domain.AcceptedCredentials
                .Select(wrapper => LookupCredential(client, account, wrapper.Credential, pinnedIndex, cancellationToken))
                .ToList();
            LOCredential[] credentials = await Task.WhenAll(lookups);

            return EvaluateDomainAccess(credentials, closeTime, ledgerIndex);
        }

        /// <summary>
        /// Evaluates domain access from the credential entries found for the account.
        /// Null entries mean the account does not hold the corresponding credential.
        /// A credential is expired only when the close time is strictly greater than
        /// its Expiration, matching rippled's credentials::checkExpired.
        /// </summary>
        /// <param name="credentials">Credential entries matching the domain's accepted pairs; null where not held.</param>
        /// <param name="closeTime">Close time of the ledger the check is performed against.</param>
        /// <param name="ledgerIndex">Index of the ledger the check is performed against.</param>
        public static DomainAccessResult EvaluateDomainAccess(IReadOnlyList<LOCredential> credentials, DateTime closeTime, uint ledgerIndex)
        {
            DomainAccessResult result = new DomainAccessResult { LedgerIndex = ledgerIndex };
            foreach (LOCredential credential in credentials)
            {
                if (credential is null)
                    continue;

                // Flags is nullable because these models double as metadata projections, where a
                // member can be absent. Absence must not read as accepted: a lifted `!=` returns
                // true when either side is null — unlike a lifted `<`, which returns false — so the
                // presence check has to be explicit.
                bool accepted = credential.Flags is { } flags
                    && (flags & (uint)CredentialFlags.lsfAccepted) != 0;
                bool expired = credential.Expiration is DateTime expiration && closeTime > expiration;
                if (accepted && !expired)
                {
                    result.HasAccess = true;
                    result.InvalidCredentials.Clear();
                    return result;
                }

                result.InvalidCredentials.Add(new DomainCredentialStatus
                {
                    Issuer = credential.Issuer,
                    CredentialType = credential.CredentialType,
                    Accepted = accepted,
                    Expired = expired
                });
            }

            return result;
        }

        private static async Task<LOCredential> LookupCredential(IXrplClient client, string subject, AcceptedCredential accepted, LedgerIndex ledgerIndex, CancellationToken cancellationToken)
        {
            LedgerEntryRequest request = new LedgerEntryRequest
            {
                Credential = new CredentialQuery
                {
                    Subject = subject,
                    Issuer = accepted.Issuer,
                    CredentialType = accepted.CredentialType
                },
                LedgerIndex = ledgerIndex
            };
            try
            {
                LedgerEntryResponse response = await client.LedgerEntry(request, cancellationToken).Typed();
                return response.Node as LOCredential;
            }
            catch (RippledException ex) when (ex.Response?.Error == XrplErrorCodes.EntryNotFound)
            {
                return null;
            }
        }
    }
}
