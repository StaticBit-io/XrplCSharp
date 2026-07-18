using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// Drift guard for the hand-written Models enums against the protocol
    /// definitions (#42 part a). A transaction/ledger-entry type added to
    /// definitions.json but forgotten in the Models enum (or vice versa) fails
    /// here. Intentional divergences are recorded in the per-enum allow-lists:
    /// definitions.json carries "Invalid" (the Models enums omit it), and the
    /// Models enums add a synthetic "Unknown" sentinel the converters fall back
    /// to for unrecognized values.
    /// </summary>
    [TestClass]
    public class TestUModelEnumDrift
    {
        // definitions.json has these; the Models enums intentionally omit them.
        private static readonly HashSet<string> DefinitionsOnly =
            new(StringComparer.Ordinal) { "Invalid" };

        // The Models enums add these synthetic members (not protocol types).
        private static readonly HashSet<string> ModelsOnly =
            new(StringComparer.Ordinal) { "Unknown" };

        [TestMethod]
        public void TestUTransactionTypeEnum_MatchesDefinitions()
        {
            AssertEnumMatchesDefinitions(typeof(global::Xrpl.Models.TransactionType), "TRANSACTION_TYPES", DefinitionsOnly, ModelsOnly);
        }

        [TestMethod]
        public void TestULedgerEntryTypeEnum_MatchesDefinitions()
        {
            AssertEnumMatchesDefinitions(typeof(global::Xrpl.Models.LedgerEntryType), "LEDGER_ENTRY_TYPES", DefinitionsOnly, ModelsOnly);
        }

        private static void AssertEnumMatchesDefinitions(
            Type enumType, string section, ISet<string> definitionsOnly, ISet<string> modelsOnly)
        {
            HashSet<string> modelNames = new(Enum.GetNames(enumType), StringComparer.Ordinal);
            HashSet<string> definitionKeys = LoadSectionKeys(section);

            // The EXACT member set the Models enum must contain: every protocol
            // type minus the intentionally-omitted ones, plus the synthetic
            // sentinels. Comparing against this exact set — rather than only
            // subtracting the allow-lists from each diff — also catches an
            // omitted name being ADDED (e.g. Invalid) or a sentinel being
            // REMOVED (e.g. Unknown), not just names on either raw side.
            HashSet<string> expected = new(definitionKeys, StringComparer.Ordinal);
            expected.ExceptWith(definitionsOnly);
            expected.UnionWith(modelsOnly);

            List<string> missingInModels = expected
                .Where(n => !modelNames.Contains(n))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            List<string> extraInModels = modelNames
                .Where(n => !expected.Contains(n))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            if (missingInModels.Count == 0 && extraInModels.Count == 0)
                return;

            string message =
                $"{enumType.Name} drift vs definitions.json ({section}):" + Environment.NewLine +
                $"  missing in Models (add them):      {Format(missingInModels)}" + Environment.NewLine +
                $"  extra in Models (remove/justify):  {Format(extraInModels)}" + Environment.NewLine +
                "If a divergence is intentional, adjust the DefinitionsOnly/ModelsOnly allow-list with a reason.";
            Assert.Fail(message);
        }

        private static string Format(List<string> names) =>
            names.Count == 0 ? "(none)" : string.Join(", ", names);

        private static HashSet<string> LoadSectionKeys(string section)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "definitions.json");
            Assert.IsTrue(File.Exists(path),
                $"definitions.json was not copied next to the test assembly (expected at {path}).");

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            Assert.IsTrue(doc.RootElement.TryGetProperty(section, out JsonElement sectionElement),
                $"definitions.json is missing the '{section}' section.");

            HashSet<string> keys = new(StringComparer.Ordinal);
            foreach (JsonProperty prop in sectionElement.EnumerateObject())
                keys.Add(prop.Name);
            return keys;
        }
    }
}
