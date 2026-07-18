using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GenerateEnums;

/// <summary>A single field-level difference for one named member.</summary>
public sealed record Mismatch(string Name, string Field, string Local, string Server);

/// <summary>The three difference categories for one section.</summary>
public sealed record SectionDiff(
    string Section,
    IReadOnlyList<string> NodeOnly,
    IReadOnlyList<string> LocalOnly,
    IReadOnlyList<Mismatch> Mismatch);

/// <summary>The full comparison across all five sections.</summary>
public sealed record DiffResult(IReadOnlyList<SectionDiff> Sections)
{
    /// <summary>
    /// True when the node has entries the local file lacks, or values differ.
    /// Local-only entries are informational (SDK ahead of a lagging node).
    /// </summary>
    public bool HasDrift => Sections.Any(s => s.NodeOnly.Count > 0 || s.Mismatch.Count > 0);
}

/// <summary>
/// Pure comparison of a local definitions view against a node's. No I/O.
/// This is the engine a scheduled protocol monitor can reuse.
/// </summary>
public static class DefinitionsDiff
{
    public static DiffResult Compare(Definitions local, Definitions server)
    {
        List<SectionDiff> sections = new()
        {
            DiffFields(local.Fields, server.Fields),
            DiffCodes("TYPES", local.Types, server.Types),
            DiffCodes("LEDGER_ENTRY_TYPES", local.LedgerEntryTypes, server.LedgerEntryTypes),
            DiffCodes("TRANSACTION_RESULTS", local.TransactionResults, server.TransactionResults),
            DiffCodes("TRANSACTION_TYPES", local.TransactionTypes, server.TransactionTypes),
        };
        return new DiffResult(sections);
    }

    private static SectionDiff DiffCodes(
        string section,
        IReadOnlyDictionary<string, int> local,
        IReadOnlyDictionary<string, int> server)
    {
        List<string> nodeOnly = server.Keys.Where(k => !local.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        List<string> localOnly = local.Keys.Where(k => !server.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        List<Mismatch> mismatch = new();
        foreach (string name in local.Keys.Where(server.ContainsKey).OrderBy(k => k, StringComparer.Ordinal))
        {
            if (local[name] != server[name])
                mismatch.Add(new Mismatch(name, "code",
                    local[name].ToString(CultureInfo.InvariantCulture),
                    server[name].ToString(CultureInfo.InvariantCulture)));
        }
        return new SectionDiff(section, nodeOnly, localOnly, mismatch);
    }

    private static SectionDiff DiffFields(
        IReadOnlyDictionary<string, FieldDef> local,
        IReadOnlyDictionary<string, FieldDef> server)
    {
        List<string> nodeOnly = server.Keys.Where(k => !local.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        List<string> localOnly = local.Keys.Where(k => !server.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        List<Mismatch> mismatch = new();
        foreach (string name in local.Keys.Where(server.ContainsKey).OrderBy(k => k, StringComparer.Ordinal))
        {
            FieldDef a = local[name], b = server[name];
            if (a.Type != b.Type) mismatch.Add(new Mismatch(name, "type", a.Type, b.Type));
            if (a.Nth != b.Nth) mismatch.Add(new Mismatch(name, "nth", a.Nth.ToString(), b.Nth.ToString()));
            if (a.IsSigningField != b.IsSigningField) mismatch.Add(new Mismatch(name, "isSigningField", a.IsSigningField.ToString(), b.IsSigningField.ToString()));
            if (a.IsSerialized != b.IsSerialized) mismatch.Add(new Mismatch(name, "isSerialized", a.IsSerialized.ToString(), b.IsSerialized.ToString()));
            if (a.IsVLEncoded != b.IsVLEncoded) mismatch.Add(new Mismatch(name, "isVLEncoded", a.IsVLEncoded.ToString(), b.IsVLEncoded.ToString()));
        }
        return new SectionDiff("FIELDS", nodeOnly, localOnly, mismatch);
    }
}
