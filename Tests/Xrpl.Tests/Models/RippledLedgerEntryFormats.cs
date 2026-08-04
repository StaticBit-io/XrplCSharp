using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Xrpl.Tests.Models.Tests
{
    /// <summary>
    /// Reads the vendored rippled <c>ledger_entries.macro</c> — the only place the protocol
    /// states which fields belong to which ledger object. <c>definitions.json</c> carries field
    /// codes and object types, but not the per-object field lists, so it cannot answer this.
    /// </summary>
    /// <remarks>
    /// The counterpart of <see cref="RippledTransactionFormats"/> for ledger objects. Same
    /// contract: the source is C++ macro text, so every parse step fails loudly rather than
    /// yielding a thin or empty table — a silently empty result would turn the conformance
    /// test green on nothing.
    /// </remarks>
    internal static class RippledLedgerEntryFormats
    {
        /// <summary>
        /// LEDGER_ENTRY(ltTAG, 0x00NN, Name, rpcName, ({ {sfField, SoeX}, ... }))
        /// LEDGER_ENTRY_DUPLICATE(...) has the same shape and declares an object that shares
        /// a type code with another one, so it is parsed identically.
        /// </summary>
        private static readonly Regex EntryBlock = new Regex(
            @"LEDGER_ENTRY(?:_DUPLICATE)?\(\s*lt\w+\s*,\s*0x[0-9a-fA-F]+\s*,\s*(?<name>\w+)\s*,(?<body>.*?)\}\)\)",
            RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>{sfField, SoeRequired} / {sfField, SoeOptional} / {sfField, SoeDefault}</summary>
        private static readonly Regex FieldEntry = new Regex(
            @"\{\s*sf(?<field>\w+)\s*,\s*Soe(?<requirement>Required|Optional|Default)\b",
            RegexOptions.Compiled);

        /// <summary>Catches a requirement keyword the mapping below does not know yet.</summary>
        private static readonly Regex AnyFieldEntry = new Regex(
            @"\{\s*sf(?<field>\w+)\s*,\s*Soe(?<requirement>\w+)",
            RegexOptions.Compiled);

        /// <summary>
        /// Lower bounds on a healthy parse, asserted by the guard test as well so the two
        /// cannot disagree about what "parsed enough" means.
        /// </summary>
        internal const int MinimumExpectedEntries = 25;

        /// <inheritdoc cref="MinimumExpectedEntries"/>
        internal const int MinimumExpectedFields = 250;

        internal static string FixturePath =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "ledger_entries.macro");

        /// <summary>
        /// Fields every ledger object carries, declared once in rippled's
        /// <c>LedgerFormats::getCommonFields()</c> (src/libxrpl/protocol/LedgerFormats.cpp)
        /// rather than per object in the macro — the ledger-side counterpart of
        /// <c>TxFormats</c>' <c>commonFields</c>. Both directions of the conformance diff
        /// exclude them: the macro never lists them, so requiring them of a model would be
        /// wrong, and a model that does expose them is not inventing anything.
        /// </summary>
        /// <remarks>
        /// Hand-maintained: the list lives in a .cpp, which protocol-watch does not track
        /// (it watches the headers and macros). It has held these four for releases —
        /// <c>sfSponsor</c> was the last addition, with XLS-68 — so drift here is slow and
        /// visible: a new common field would surface as the same name reported missing from
        /// every single model at once.
        /// </remarks>
        internal static HashSet<string> CommonFields() => new(StringComparer.Ordinal)
        {
            "LedgerIndex",
            "LedgerEntryType",
            "Flags",
            "Sponsor",
        };

        /// <summary>How rippled declares a field of a ledger object.</summary>
        internal enum Requirement
        {
            /// <summary>Always present.</summary>
            Required,

            /// <summary>May be absent.</summary>
            Optional,

            /// <summary>Absent means the type's default value, not missing data.</summary>
            Default,
        }

        /// <summary>
        /// Ledger object name -> field name -> requirement, exactly as rippled declares it.
        /// </summary>
        internal static Dictionary<string, Dictionary<string, Requirement>> Parse()
        {
            if (!File.Exists(FixturePath))
                throw new InvalidOperationException($"Vendored ledger_entries.macro not found at {FixturePath}");

            string macro = File.ReadAllText(FixturePath);
            if (string.IsNullOrWhiteSpace(macro))
                throw new InvalidOperationException("Vendored ledger_entries.macro is empty");

            Dictionary<string, Dictionary<string, Requirement>> entries = new();
            int fieldCount = 0;

            foreach (Match block in EntryBlock.Matches(macro))
            {
                string name = block.Groups["name"].Value;
                string body = block.Groups["body"].Value;

                foreach (Match raw in AnyFieldEntry.Matches(body))
                {
                    string keyword = raw.Groups["requirement"].Value;
                    if (keyword is not ("Required" or "Optional" or "Default"))
                    {
                        throw new InvalidOperationException(
                            $"{name}.{raw.Groups["field"].Value}: unknown requirement keyword 'Soe{keyword}' — " +
                            "the macro format changed, update the parser before trusting this test");
                    }
                }

                Dictionary<string, Requirement> fields = new();
                foreach (Match field in FieldEntry.Matches(body))
                {
                    fields[field.Groups["field"].Value] = field.Groups["requirement"].Value switch
                    {
                        "Required" => Requirement.Required,
                        "Optional" => Requirement.Optional,
                        "Default" => Requirement.Default,
                        _ => throw new InvalidOperationException("unreachable"),
                    };
                }

                entries[name] = fields;
                fieldCount += fields.Count;
            }

            if (entries.Count < MinimumExpectedEntries || fieldCount < MinimumExpectedFields)
            {
                throw new InvalidOperationException(
                    $"Parsed only {entries.Count} ledger entries / {fieldCount} fields from " +
                    $"ledger_entries.macro (expected at least {MinimumExpectedEntries} / " +
                    $"{MinimumExpectedFields}) — the macro layout changed and the parser " +
                    "silently stopped matching");
            }

            return entries;
        }
    }
}
