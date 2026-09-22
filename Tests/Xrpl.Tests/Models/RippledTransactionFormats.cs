using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Xrpl.BinaryCodec.Enums;

using TxFormat = Xrpl.Models.Transaction.TxFormat;

namespace Xrpl.Tests.Models.Tests
{
    /// <summary>
    /// Reads the vendored rippled <c>transactions.macro</c> — the only place the protocol
    /// states which fields belong to which transaction type. <c>definitions.json</c> and the
    /// <c>server_definitions</c> RPC carry field codes but no per-transaction formats, so they
    /// cannot answer this question.
    /// </summary>
    /// <remarks>
    /// The source is C++ macro text, not a stability-guaranteed contract: the TRANSACTION
    /// signature has changed before and its own header invites callers to elide arguments.
    /// Every parse step therefore fails loudly rather than yielding a thin or empty table —
    /// a silently empty result would turn the conformance test green on nothing.
    /// </remarks>
    internal static class RippledTransactionFormats
    {
        /// <summary>
        /// TRANSACTION(ttTAG, code, Name, delegable, amendments, privileges, ({ {sfField, SoeX}, ... }))
        /// </summary>
        private static readonly Regex TransactionBlock = new Regex(
            @"TRANSACTION\(\s*tt\w+\s*,\s*\d+\s*,\s*(?<name>\w+)\s*,(?<body>.*?)\}\)\)",
            RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>{sfField, SoeRequired} / {sfField, SoeOptional, SoeMptSupported}</summary>
        private static readonly Regex FieldEntry = new Regex(
            @"\{\s*sf(?<field>\w+)\s*,\s*Soe(?<requirement>Required|Optional|Default)\b",
            RegexOptions.Compiled);

        /// <summary>Catches a requirement keyword the mapping below does not know yet.</summary>
        private static readonly Regex AnyFieldEntry = new Regex(
            @"\{\s*sf(?<field>\w+)\s*,\s*Soe(?<requirement>\w+)",
            RegexOptions.Compiled);

        /// <summary>
        /// Lower bound on how many formats a healthy parse yields. Exposed so the guard test
        /// asserts against the same number the parser enforces, instead of a second literal
        /// that would drift when the fixture is re-pinned.
        /// </summary>
        internal const int MinimumExpectedTransactions = 60;

        /// <summary>
        /// Fields shared by every transaction, declared once in the <see cref="TxFormat"/> constructor.
        /// rippled keeps them in a separate <c>commonFields</c> list, so both conformance surfaces
        /// exclude them — they read the set from here so the two cannot drift apart.
        /// </summary>
        internal static HashSet<Field> CommonFields() => new TxFormat().Keys.ToHashSet();

        internal static string FixturePath =>
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "transactions.macro");

        /// <summary>
        /// The transaction types rippled refuses to delegate, exactly as the macro declares them.
        /// </summary>
        /// <remarks>
        /// <c>TxSettings.delegable</c> defaults to <c>Delegation::NotDelegable</c>, so a type is
        /// delegable only where its settings tuple says so explicitly. The flag is static: an
        /// amendment can withhold delegability from a delegable type, never grant it to a
        /// forbidden one, so this set is correct for every network - unlike the delegable set,
        /// which <c>Permission::isDelegable</c> answers against the active amendments.
        /// </remarks>
        internal static HashSet<string> NonDelegableTransactions()
        {
            string macro = ReadFixture();

            HashSet<string> nonDelegable = new(StringComparer.Ordinal);
            int total = 0;

            foreach (Match block in TransactionBlock.Matches(macro))
            {
                string name = block.Groups["name"].Value;
                string body = block.Groups["body"].Value;
                total++;

                // The settings tuple is the first parenthesised group of the body; the fields
                // tuple follows it. Without the separator the layout has changed and any answer
                // here would be a guess.
                int settingsEnd = body.IndexOf("}),", StringComparison.Ordinal);
                if (settingsEnd < 0)
                {
                    throw new InvalidOperationException(
                        $"{name}: could not find the end of the settings tuple in transactions.macro — " +
                        "the macro format changed, update the parser before trusting this test");
                }

                string settings = body.Substring(0, settingsEnd);
                if (!settings.Contains("Delegation::Delegable", StringComparison.Ordinal))
                    nonDelegable.Add(name);
            }

            if (total < MinimumExpectedTransactions)
            {
                throw new InvalidOperationException(
                    $"Parsed only {total} transaction blocks from transactions.macro " +
                    $"(expected at least {MinimumExpectedTransactions}) — the macro layout changed");
            }

            // Every type delegable, or none of them, means the settings tuple stopped matching
            // rather than that the protocol changed that drastically.
            if (nonDelegable.Count == 0 || nonDelegable.Count == total)
            {
                throw new InvalidOperationException(
                    $"Parsed {nonDelegable.Count} non-delegable types out of {total} — the delegable " +
                    "flag stopped being read, so this test would pass on nothing");
            }

            return nonDelegable;
        }

        private static string ReadFixture()
        {
            if (!File.Exists(FixturePath))
                throw new InvalidOperationException($"Vendored transactions.macro not found at {FixturePath}");

            string macro = File.ReadAllText(FixturePath);
            if (string.IsNullOrWhiteSpace(macro))
                throw new InvalidOperationException("Vendored transactions.macro is empty");

            return macro;
        }

        /// <summary>
        /// Transaction name -> field name -> requirement, exactly as rippled declares it.
        /// </summary>
        internal static Dictionary<string, Dictionary<string, TxFormat.Requirement>> Parse()
        {
            string macro = ReadFixture();

            Dictionary<string, Dictionary<string, TxFormat.Requirement>> formats = new();

            foreach (Match block in TransactionBlock.Matches(macro))
            {
                string name = block.Groups["name"].Value;
                string body = block.Groups["body"].Value;

                // A requirement keyword we do not map would otherwise drop the field silently.
                foreach (Match raw in AnyFieldEntry.Matches(body))
                {
                    string keyword = raw.Groups["requirement"].Value;
                    if (keyword is not ("Required" or "Optional" or "Default" or "MptSupported"))
                    {
                        throw new InvalidOperationException(
                            $"{name}.{raw.Groups["field"].Value}: unknown requirement keyword 'Soe{keyword}' — " +
                            "the macro format changed, update the parser before trusting this test");
                    }
                }

                Dictionary<string, TxFormat.Requirement> fields = new();
                foreach (Match field in FieldEntry.Matches(body))
                {
                    fields[field.Groups["field"].Value] = field.Groups["requirement"].Value switch
                    {
                        "Required" => TxFormat.Requirement.Required,
                        "Optional" => TxFormat.Requirement.Optional,
                        "Default" => TxFormat.Requirement.Default,
                        _ => throw new InvalidOperationException("unreachable"),
                    };
                }

                formats[name] = fields;
            }

            if (formats.Count < MinimumExpectedTransactions)
            {
                throw new InvalidOperationException(
                    $"Parsed only {formats.Count} transaction formats from transactions.macro " +
                    $"(expected at least {MinimumExpectedTransactions}) — the macro layout changed " +
                    "and the parser silently stopped matching");
            }

            return formats;
        }
    }
}
