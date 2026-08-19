using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Models.Ledger;

using LONFTokenOffer = Xrpl.Models.Methods.LONFTokenOffer;
using LONFTokenPage = Xrpl.Models.Methods.LONFTokenPage;

namespace Xrpl.Tests.Models.Tests
{
    /// <summary>
    /// Holds the ledger-object models to the field sets rippled declares in the vendored
    /// <c>ledger_entries.macro</c>.
    /// </summary>
    /// <remarks>
    /// The third conformance surface, next to <see cref="TestUTxFormatConformance"/> (transaction
    /// fields) and <see cref="TestULedgerFlagsConformance"/> (ledger flags). A field the protocol
    /// declares and the model lacks produces no symptom: reading the object still succeeds and the
    /// value is simply dropped, so it stays invisible until someone needs it. That is how
    /// LOAccountRoot went without WalletLocator/WalletSize until a manual completeness pass, and
    /// how sfLEVersion had to be spotted through a protocol-watch notification instead of a red test.
    /// </remarks>
    [TestClass]
    public class TestULedgerEntryFieldsConformance
    {
        /// <summary>
        /// rippled LEDGER_ENTRY name -> the model that carries its fields. Every entry in the
        /// fixture must appear here; a newly added ledger object fails the test rather than
        /// being skipped silently.
        /// </summary>
        private static readonly Dictionary<string, Type> Models = new(StringComparer.Ordinal)
        {
            ["AccountRoot"] = typeof(LOAccountRoot),
            ["AMM"] = typeof(LOAmm),
            ["Amendments"] = typeof(LOAmendments),
            ["Bridge"] = typeof(LOBridge),
            ["Check"] = typeof(LOCheck),
            ["Credential"] = typeof(LOCredential),
            ["Delegate"] = typeof(LODelegate),
            ["DepositPreauth"] = typeof(LODepositPreauth),
            ["DID"] = typeof(LODID),
            ["DirectoryNode"] = typeof(LODirectoryNode),
            ["Escrow"] = typeof(LOEscrow),
            ["FeeSettings"] = typeof(LOFeeSettings),
            ["LedgerHashes"] = typeof(LOLedgerHashes),
            ["Loan"] = typeof(LOLoan),
            ["LoanBroker"] = typeof(LOLoanBroker),
            ["MPToken"] = typeof(LOMPToken),
            ["MPTokenIssuance"] = typeof(LOMPTokenIssuance),
            ["NegativeUNL"] = typeof(LONegativeUNL),
            ["NFTokenOffer"] = typeof(LONFTokenOffer),
            ["NFTokenPage"] = typeof(LONFTokenPage),
            ["Offer"] = typeof(LOOffer),
            ["Oracle"] = typeof(LOOracle),
            ["PayChannel"] = typeof(LOPayChannel),
            ["PermissionedDomain"] = typeof(LOPermissionedDomain),
            ["RippleState"] = typeof(LORippleState),
            ["SignerList"] = typeof(LOSignerList),
            ["Sponsorship"] = typeof(LOSponsorship),
            ["Ticket"] = typeof(LOTicket),
            ["Vault"] = typeof(LOVault),
            ["XChainOwnedClaimID"] = typeof(LOXChainOwnedClaimID),
            ["XChainOwnedCreateAccountClaimID"] = typeof(LOXChainOwnedCreateAccountClaimID),
        };

        /// <summary>
        /// Names that appear on a model but are not fields of that ledger object, with the
        /// reason each is legitimate. Anything else the reverse check reports is a real finding.
        /// Common fields are handled separately, via
        /// <see cref="RippledLedgerEntryFormats.CommonFields"/>.
        /// </summary>
        private static readonly Dictionary<string, string> KnownExtras = new(StringComparer.Ordinal)
        {
            // BaseLedgerEntry.Index, serialized as "index" — the object's own key. rippled
            // returns it alongside the object (account_objects, ledger_entry) and it is not
            // part of any object's template
            ["index"] = "the entry's key, returned beside the object rather than inside it",
        };

        /// <summary>
        /// The JSON name a property maps to: <see cref="JsonPropertyNameAttribute"/> when
        /// present, the property name otherwise. Properties marked <see cref="JsonIgnoreAttribute"/>
        /// never reach the wire and are excluded — that is where the computed helpers live
        /// (DataParsed, MPTokenMetadataRow, Metadata, …). Properties marked
        /// <see cref="JsonExtensionDataAttribute"/> (BaseLedgerEntry.UnknownFields) are excluded
        /// too: that property is not itself a field of any ledger object — it is the catch-all
        /// System.Text.Json pours anything undeclared into — so it never belongs in either side of
        /// this diff.
        /// </summary>
        private static Dictionary<string, PropertyInfo> WireProperties(Type model)
        {
            Dictionary<string, PropertyInfo> map = new(StringComparer.Ordinal);

            foreach (PropertyInfo property in model.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null)
                    continue;

                if (property.GetCustomAttribute<JsonExtensionDataAttribute>() != null)
                    continue;

                string name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
                map[name] = property;
            }

            return map;
        }

        [TestMethod]
        public void TestULedgerEntryModels_MatchRippledLedgerEntriesMacro()
        {
            Dictionary<string, Dictionary<string, RippledLedgerEntryFormats.Requirement>> upstream =
                RippledLedgerEntryFormats.Parse();
            HashSet<string> common = RippledLedgerEntryFormats.CommonFields();
            StringBuilder report = new StringBuilder();

            foreach (KeyValuePair<string, Dictionary<string, RippledLedgerEntryFormats.Requirement>> entry
                     in upstream.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                if (!Models.TryGetValue(entry.Key, out Type model))
                {
                    report.AppendLine(
                        $"{entry.Key}: declared in ledger_entries.macro but no model is registered for it — " +
                        "add the LO type and register it in Models");
                    continue;
                }

                Dictionary<string, PropertyInfo> mine = WireProperties(model);

                foreach (string field in entry.Value.Keys.OrderBy(f => f, StringComparer.Ordinal))
                {
                    if (!mine.ContainsKey(field))
                    {
                        report.AppendLine(
                            $"{entry.Key}.{field} ({entry.Value[field]}): declared by rippled, " +
                            $"missing from {model.Name}");
                    }
                }

                foreach (string name in mine.Keys.OrderBy(n => n, StringComparer.Ordinal))
                {
                    if (entry.Value.ContainsKey(name) || common.Contains(name) || KnownExtras.ContainsKey(name))
                        continue;

                    report.AppendLine(
                        $"{model.Name}.{name}: on the model, not a field of {entry.Key} in rippled");
                }
            }

            Assert.AreEqual(
                string.Empty,
                report.ToString(),
                $"Ledger-object models diverge from rippled ledger_entries.macro ({RippledLedgerEntryFormats.FixturePath}):\n" + report);
        }

        [TestMethod]
        public void TestULedgerEntryFixture_ParsesFully()
        {
            Dictionary<string, Dictionary<string, RippledLedgerEntryFormats.Requirement>> upstream =
                RippledLedgerEntryFormats.Parse();

            Assert.IsTrue(
                upstream.Count >= RippledLedgerEntryFormats.MinimumExpectedEntries,
                $"Parsed {upstream.Count} ledger entries, expected at least {RippledLedgerEntryFormats.MinimumExpectedEntries}");

            int fields = upstream.Sum(e => e.Value.Count);
            Assert.IsTrue(
                fields >= RippledLedgerEntryFormats.MinimumExpectedFields,
                $"Parsed {fields} fields, expected at least {RippledLedgerEntryFormats.MinimumExpectedFields}");

            // Counts alone would still pass on a parse that dropped requirements
            Assert.AreEqual(
                RippledLedgerEntryFormats.Requirement.Default,
                upstream["Vault"]["LEVersion"],
                "Vault.LEVersion should parse as SoeDefault");
            Assert.AreEqual(
                RippledLedgerEntryFormats.Requirement.Required,
                upstream["Vault"]["Owner"],
                "Vault.Owner should parse as SoeRequired");
        }
    }
}
