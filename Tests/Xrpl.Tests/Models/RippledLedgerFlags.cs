using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Xrpl.Tests.Models.Tests
{
    /// <summary>
    /// Reads the vendored rippled <c>LedgerFormats.h</c> — the only place the protocol states
    /// which <c>lsf</c> flags belong to which ledger object. <c>definitions.json</c> and the
    /// <c>server_definitions</c> RPC carry field codes and ledger entry types but no flag
    /// values, so they cannot answer this question.
    /// </summary>
    /// <remarks>
    /// The source is C++ macro text, not a stability-guaranteed contract. Every parse step
    /// fails loudly rather than yielding a thin or empty table — a silently empty result
    /// would turn the conformance test green on nothing.
    /// </remarks>
    internal static class RippledLedgerFlags
    {
        /// <summary>
        /// LEDGER_ENTRY(ltNAME, 0x00, Name, ...) blocks are irrelevant here; the flags live in
        /// LEDGER_OBJECT(Name, LSF_FLAG(lsfX, 0x…) …) blocks of the LEDGER_OBJECT_FLAGS list.
        /// </summary>
        private static readonly Regex ObjectBlock = new Regex(
            @"LEDGER_OBJECT\(\s*(?<name>\w+)\s*,(?<body>(?:[^()]|\((?:[^()])*\))*)\)",
            RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>LSF_FLAG(lsfX, 0x00010000) / LSF_FLAG2(lsfX, 0x00000001)</summary>
        private static readonly Regex FlagEntry = new Regex(
            @"LSF_FLAG2?\(\s*(?<flag>ls[fm]\w+)\s*,\s*(?<value>0x[0-9a-fA-F]+)\s*\)",
            RegexOptions.Compiled);

        /// <summary>
        /// Catches an LSF_FLAG variant the parser does not know yet — a new macro name would
        /// otherwise drop its flags silently and leave the conformance test passing.
        /// </summary>
        private static readonly Regex AnyFlagMacro = new Regex(
            @"(?<macro>LSF_FLAG\w*)\(", RegexOptions.Compiled);

        /// <summary>
        /// Lower bounds on a healthy parse. Exposed so the guard test asserts against the same
        /// numbers the parser enforces, instead of literals that would drift on re-pinning.
        /// </summary>
        internal const int MinimumExpectedObjects = 10;

        /// <inheritdoc cref="MinimumExpectedObjects"/>
        internal const int MinimumExpectedFlags = 50;

        internal static string FixturePath =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "LedgerFormats.h");

        /// <summary>
        /// Ledger object name -> flag name -> value, exactly as rippled declares it.
        /// </summary>
        internal static Dictionary<string, Dictionary<string, uint>> Parse()
        {
            if (!File.Exists(FixturePath))
                throw new InvalidOperationException($"Vendored LedgerFormats.h not found at {FixturePath}");

            string header = File.ReadAllText(FixturePath);
            if (string.IsNullOrWhiteSpace(header))
                throw new InvalidOperationException("Vendored LedgerFormats.h is empty");

            foreach (Match macro in AnyFlagMacro.Matches(header))
            {
                string name = macro.Groups["macro"].Value;
                if (name is not ("LSF_FLAG" or "LSF_FLAG2"))
                {
                    throw new InvalidOperationException(
                        $"Unknown flag macro '{name}' in LedgerFormats.h — the header layout changed, " +
                        "update the parser before trusting this test");
                }
            }

            Dictionary<string, Dictionary<string, uint>> objects = new();
            int flagCount = 0;

            // Tracked separately from `objects`, which only holds flagged entries: a name declared
            // twice must be caught even when one of the two declarations parses to no flags at all,
            // otherwise the flagless-skip below would let the duplicate through unnoticed.
            HashSet<string> seenNames = new(StringComparer.Ordinal);

            foreach (Match block in ObjectBlock.Matches(header))
            {
                string name = block.Groups["name"].Value;

                // Same rule as RippledLedgerEntryFormats.Parse, so the two parsers stay consistent
                if (!seenNames.Add(name))
                {
                    throw new InvalidOperationException(
                        $"{name}: declared twice in LedgerFormats.h — the parser would drop one " +
                        "definition, update it before trusting this test");
                }

                Dictionary<string, uint> flags = new();

                foreach (Match flag in FlagEntry.Matches(block.Groups["body"].Value))
                {
                    flags[flag.Groups["flag"].Value] =
                        Convert.ToUInt32(flag.Groups["value"].Value.Substring(2), 16);
                }

                // LEDGER_OBJECT is also used for objects that declare no flags at all;
                // those carry nothing to conform to.
                if (flags.Count == 0)
                    continue;

                objects.Add(name, flags);
                flagCount += flags.Count;
            }

            if (objects.Count < MinimumExpectedObjects || flagCount < MinimumExpectedFlags)
            {
                throw new InvalidOperationException(
                    $"Parsed only {objects.Count} flagged ledger objects / {flagCount} flags from " +
                    $"LedgerFormats.h (expected at least {MinimumExpectedObjects} / {MinimumExpectedFlags}) — " +
                    "the header layout changed and the parser silently stopped matching");
            }

            return objects;
        }
    }
}
