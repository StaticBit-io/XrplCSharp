using System;
using System.Collections.Concurrent;

using Xrpl.Utils;

using static Xrpl.Models.Common.Common;

namespace Xrpl.Amounts
{
    /// <summary>
    /// What the engine reads off an asset regardless of its kind: its issuer (<c>Asset::getIssuer</c>)
    /// - nobody for XRP, the currency's issuer, or the account an MPT issuance ID names - and the
    /// key an MPT is indexed by.
    /// </summary>
    internal static class DexAssets
    {
        private static readonly ConcurrentDictionary<string, string> MptIssuers = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        /// <summary><c>Asset::getIssuer</c>: the empty account for XRP.</summary>
        internal static string Issuer(IssuedCurrency asset) =>
            XrplAmount.KindOf(asset) switch
            {
                AmountKind.Xrp => string.Empty,
                AmountKind.Mpt => MptIssuer(asset.MptIssuanceId),
                _ => asset.Issuer,
            };

        /// <summary>The issuer an MPT issuance ID names: its last 20 bytes.</summary>
        internal static string MptIssuer(string mptIssuanceId) =>
            MptIssuers.GetOrAdd(MptKey(mptIssuanceId), id => id.ParseMPTokenIssuanceID().Issuer);

        /// <summary>An MPT issuance ID as the snapshot's indexes compare it: upper-case hex.</summary>
        internal static string MptKey(string mptIssuanceId) => mptIssuanceId.ToUpperInvariant();

        internal static bool IsMpt(IssuedCurrency asset) => XrplAmount.KindOf(asset) == AmountKind.Mpt;

        internal static IssuedCurrency Mpt(string mptIssuanceId) => new IssuedCurrency { MptIssuanceId = MptKey(mptIssuanceId) };
    }
}
