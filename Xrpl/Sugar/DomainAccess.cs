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
// Mirrors rippled's credentials::validDomain (CredentialHelpers.cpp): an account
// has access to a permissioned domain iff it holds a Credential matching one of
// the domain's AcceptedCredentials (Issuer + CredentialType) that is accepted
// (lsfAccepted) and not expired at the checked ledger's close time.

namespace Xrpl.Sugar
{
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
        /// <summary> True if the account can use the domain (permissioned DEX, vaults, ...). </summary>
        public bool HasAccess { get; set; }

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
        public static async Task<DomainAccessResult> GetDomainAccess(this IXrplClient client, string account, string domainId, CancellationToken cancellationToken = default)
        {
            // Pin all lookups to one validated ledger; its close time is the reference
            // point for expiration. rippled deletes expired credentials lazily (only
            // when a transaction path touches them via verifyValidDomain), so a
            // read-path check must compare Expiration itself instead of trusting
            // the entry's existence.
            LedgerRequest ledgerRequest = new LedgerRequest { LedgerIndex = new LedgerIndex(LedgerIndexType.Validated) };
            LOLedger ledgerResponse = (await client.Ledger(ledgerRequest, cancellationToken)).Result;
            LedgerEntity ledger = (LedgerEntity)ledgerResponse.LedgerEntity;
            uint ledgerIndex = Convert.ToUInt32(ledger.LedgerIndex);
            DateTime closeTime = ledger.CloseTime
                ?? throw new RippleException("Validated ledger response did not include a close time.");
            LedgerIndex pinnedIndex = new LedgerIndex(ledgerIndex);

            LedgerEntryRequest domainRequest = new LedgerEntryRequest
            {
                Index = domainId,
                LedgerIndex = pinnedIndex
            };
            LedgerEntryResponse domainResponse = (await client.LedgerEntry(domainRequest, cancellationToken)).Result;
            if (domainResponse.Node is not LOPermissionedDomain domain)
                throw new RippleException($"Ledger entry {domainId} is not a PermissionedDomain.");

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

                bool accepted = (credential.Flags & (uint)CredentialFlags.lsfAccepted) != 0;
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
                LedgerEntryResponse response = (await client.LedgerEntry(request, cancellationToken)).Result;
                return response.Node as LOCredential;
            }
            catch (RippledException ex) when (ex.Response?.Error == XrplErrorCodes.EntryNotFound)
            {
                return null;
            }
        }
    }
}
