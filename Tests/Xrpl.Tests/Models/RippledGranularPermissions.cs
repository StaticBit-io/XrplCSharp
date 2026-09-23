using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Xrpl.Tests.Models.Tests
{
    /// <summary>
    /// Reads the vendored rippled <c>permissions.macro</c>, the only place the protocol declares
    /// the granular permissions. <c>definitions.json</c> does not carry them, which is why
    /// <c>GenerateEnums</c> cannot produce them the way it produces the other protocol enums.
    /// </summary>
    /// <remarks>
    /// The source is C++ macro text rather than a stability-guaranteed contract, so every parse
    /// step fails loudly instead of yielding a thin table: a silently short result would turn the
    /// conformance test green on a fraction of the protocol.
    /// </remarks>
    internal static class RippledGranularPermissions
    {
        /// <summary>
        /// GRANULAR_PERMISSION(name, txType, value, allowedFlags, allowedFields)
        /// </summary>
        private static readonly Regex Entry = new Regex(
            @"^[ \t]*GRANULAR_PERMISSION\(\s*(?<name>\w+)\s*,\s*(?<txType>tt\w+)\s*,\s*(?<value>\d+)\s*,",
            RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>
        /// Every invocation, matched on its opening alone, to catch one the parser above missed.
        /// Both patterns tolerate leading indentation, and they have to do so together: an
        /// indented entry that only one of them saw would make the counts disagree, but one that
        /// neither saw would leave them agreeing on an incomplete table.
        /// </summary>
        private static readonly Regex Invocation = new Regex(
            @"^[ \t]*GRANULAR_PERMISSION\(",
            RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>
        /// Lower bound on a healthy parse. The macro has held twelve entries since
        /// PermissionDelegationV1_1; fewer than that means the layout stopped matching.
        /// </summary>
        internal const int MinimumExpectedPermissions = 12;

        internal static string FixturePath =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "permissions.macro");

        /// <summary>
        /// Granular permission name -> numeric value, exactly as rippled declares it.
        /// </summary>
        internal static Dictionary<string, uint> Parse()
        {
            if (!File.Exists(FixturePath))
                throw new InvalidOperationException($"Vendored permissions.macro not found at {FixturePath}");

            string macro = File.ReadAllText(FixturePath);
            if (string.IsNullOrWhiteSpace(macro))
                throw new InvalidOperationException("Vendored permissions.macro is empty");

            Dictionary<string, uint> permissions = new(StringComparer.Ordinal);

            foreach (Match entry in Entry.Matches(macro))
            {
                string name = entry.Groups["name"].Value;
                if (!uint.TryParse(entry.Groups["value"].Value, out uint value))
                {
                    throw new InvalidOperationException(
                        $"{name}: could not read the permission value from permissions.macro");
                }

                permissions[name] = value;
            }

            if (permissions.Count < MinimumExpectedPermissions)
            {
                throw new InvalidOperationException(
                    $"Parsed only {permissions.Count} granular permissions from permissions.macro " +
                    $"(expected at least {MinimumExpectedPermissions}) — the macro layout changed " +
                    "and the parser silently stopped matching");
            }

            int declared = Invocation.Matches(macro).Count;
            if (permissions.Count != declared)
            {
                throw new InvalidOperationException(
                    $"permissions.macro declares {declared} granular permissions but only " +
                    $"{permissions.Count} parsed — an entry the parser does not match would be " +
                    "dropped silently");
            }

            return permissions;
        }

        /// <summary>
        /// Granular permission name -> the transaction type it applies to, as the macro states it
        /// (<c>ttTRUST_SET</c> and so on, not the transaction's own name).
        /// </summary>
        internal static Dictionary<string, string> ParseTransactionTypeTags()
        {
            string macro = File.ReadAllText(FixturePath);

            Dictionary<string, string> tags = new(StringComparer.Ordinal);
            foreach (Match entry in Entry.Matches(macro))
                tags[entry.Groups["name"].Value] = entry.Groups["txType"].Value;

            return tags;
        }
    }
}
