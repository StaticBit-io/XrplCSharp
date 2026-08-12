using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

namespace Xrpl.Tests.Models.Tests
{
    /// <summary>
    /// Holds the ledger-object flag enums to the values rippled declares in the vendored
    /// <c>LedgerFormats.h</c>.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="TestUTxFormatConformance"/> for the other half of the
    /// protocol surface. Nothing else in the suite notices a missing flag: an unnamed bit still
    /// arrives in the model as a number, so reading the object keeps working and only the
    /// consumer's ability to test it by name is lost. That is how lsfMPTAMM — present since at
    /// least 3.2.1 — went unnoticed until a manual diff found it.
    ///
    /// Pinned copy, not the live develop branch, for the same reason as the TxFormat guard:
    /// tracking upstream drift is protocol-watch's job, and a network-backed test would go red
    /// on Ripple's release schedule instead of ours.
    /// </remarks>
    [TestClass]
    public class TestULedgerFlagsConformance
    {
        /// <summary>
        /// rippled LEDGER_OBJECT name -> the enum that names its flags in the models.
        /// Every flagged object in the fixture must appear here; a new one fails the test
        /// rather than being skipped silently.
        /// </summary>
        private static readonly Dictionary<string, Type> FlagEnums = new(StringComparer.Ordinal)
        {
            ["AccountRoot"] = typeof(AccountRootFlags),
            ["Offer"] = typeof(OfferFlags),
            ["RippleState"] = typeof(RippleStateFlags),
            ["SignerList"] = typeof(SignerListFlags),
            ["DirNode"] = typeof(DirectoryNodeFlags),
            ["NFTokenOffer"] = typeof(NFTokenOffer),
            ["MPTokenIssuance"] = typeof(MPTokenIssuanceFlags),
            // rippled declares these as lsif* constants rather than a LEDGER_OBJECT block;
            // TxFlags.h then aliases tifX = lsifX, and the SDK shares one enum between
            // MPTokenIssuanceCreate.ImmutableFlags and MPTokenIssuanceSet.ImmutableFlags
            [RippledLedgerFlags.ImmutableFlagsObject] = typeof(MPTokenIssuanceImmutableFlags),
            ["MPToken"] = typeof(MPTokenFlags),
            ["Credential"] = typeof(CredentialFlags),
            ["Vault"] = typeof(VaultLedgerFlags),
            ["Loan"] = typeof(LoanFlags),
            ["Sponsorship"] = typeof(SponsorshipFlags),
        };

        /// <summary>
        /// Strips the prefix rippled and the models use for the same bit, so
        /// <c>lsfMPTLocked</c>, <c>MPTLocked</c> and <c>tifMPTCanLock</c> compare
        /// against their upstream counterparts.
        /// </summary>
        private static string Normalize(string name) =>
            Regex.Replace(name, "^(lsmf|lsif|lsf|tmf|tif)", string.Empty);

        [TestMethod]
        public void TestULedgerFlags_MatchRippledLedgerFormats()
        {
            Dictionary<string, Dictionary<string, uint>> upstream = RippledLedgerFlags.Parse();
            StringBuilder report = new StringBuilder();

            foreach (KeyValuePair<string, Dictionary<string, uint>> entry in upstream.OrderBy(o => o.Key, StringComparer.Ordinal))
            {
                if (!FlagEnums.TryGetValue(entry.Key, out Type flagEnum))
                {
                    report.AppendLine(
                        $"{entry.Key}: declares {entry.Value.Count} flag(s) in LedgerFormats.h but no model enum " +
                        "is registered for it — add the enum and register it in FlagEnums");
                    continue;
                }

                Dictionary<string, uint> mine = Enum.GetNames(flagEnum)
                    .ToDictionary(
                        name => Normalize(name),
                        name => Convert.ToUInt32(Enum.Parse(flagEnum, name)),
                        StringComparer.Ordinal);

                foreach (KeyValuePair<string, uint> flag in entry.Value.OrderBy(f => f.Key, StringComparer.Ordinal))
                {
                    string key = Normalize(flag.Key);
                    if (!mine.TryGetValue(key, out uint value))
                    {
                        report.AppendLine(
                            $"{entry.Key}.{flag.Key} (0x{flag.Value:X8}): declared by rippled, " +
                            $"missing from {flagEnum.Name}");
                    }
                    else if (value != flag.Value)
                    {
                        report.AppendLine(
                            $"{entry.Key}.{flag.Key}: rippled 0x{flag.Value:X8}, {flagEnum.Name} 0x{value:X8}");
                    }
                }

                // The other direction: a bit the models claim the protocol does not have.
                // Zero members (None) carry no bit, and tf* members are transaction flags
                // that share an enum with the ledger ones (OfferFlags.tfInnerBatchTxn).
                HashSet<string> declared = entry.Value.Keys.Select(Normalize).ToHashSet(StringComparer.Ordinal);
                foreach (string name in Enum.GetNames(flagEnum).OrderBy(n => n, StringComparer.Ordinal))
                {
                    uint value = Convert.ToUInt32(Enum.Parse(flagEnum, name));
                    if (value == 0 || name.StartsWith("tf", StringComparison.Ordinal) && !name.StartsWith("tmf", StringComparison.Ordinal))
                        continue;

                    if (!declared.Contains(Normalize(name)))
                    {
                        report.AppendLine(
                            $"{flagEnum.Name}.{name} (0x{value:X8}): in the models, " +
                            $"not a flag of {entry.Key} in rippled");
                    }
                }
            }

            Assert.AreEqual(
                string.Empty,
                report.ToString(),
                $"Ledger flag enums diverge from rippled LedgerFormats.h ({RippledLedgerFlags.FixturePath}):\n" + report);
        }

        [TestMethod]
        public void TestULedgerFlags_FixtureParsesFully()
        {
            Dictionary<string, Dictionary<string, uint>> upstream = RippledLedgerFlags.Parse();

            Assert.IsTrue(
                upstream.Count >= RippledLedgerFlags.MinimumExpectedObjects,
                $"Parsed {upstream.Count} flagged ledger objects, expected at least {RippledLedgerFlags.MinimumExpectedObjects}");

            int flags = upstream.Sum(o => o.Value.Count);
            Assert.IsTrue(
                flags >= RippledLedgerFlags.MinimumExpectedFlags,
                $"Parsed {flags} flags, expected at least {RippledLedgerFlags.MinimumExpectedFlags}");

            // A parse that yields objects but drops their values would still satisfy the counts
            Assert.AreEqual(
                0x00000080u,
                upstream["MPTokenIssuance"]["lsfMPTCanHoldConfidentialBalance"],
                "lsfMPTCanHoldConfidentialBalance should parse to 0x80");
        }
    }
}
