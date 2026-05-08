using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GenerateEnums;

/// <summary>
/// Reads definitions.json and generates partial C# source files for:
/// - EngineResult.Generated.cs (TRANSACTION_RESULTS)
/// - TransactionType.Generated.cs (TRANSACTION_TYPES)
/// - LedgerEntryType.Generated.cs (LEDGER_ENTRY_TYPES)
/// - Field.{TypeName}.Generated.cs (FIELDS, one per type group)
///
/// Before writing, performs semantic comparison between existing generated files
/// and new definitions, reporting additions, removals, and value changes.
///
/// Usage: dotnet run --project Tools/GenerateEnums [path-to-definitions.json]
/// If no path is given, defaults to Base/Xrpl.BinaryCodec/Enums/definitions.json
/// </summary>
internal static class Program
{
    private static readonly HashSet<string> SkippedFields = new(StringComparer.Ordinal)
    {
        "Transaction", "LedgerEntry", "Validation", "Metadata",
        "TransactionType", "LedgerEntryType", "TransactionResult",
        "Generic", "Invalid",
        "hash", "index",
        "taker_gets_funded", "taker_pays_funded",
        "ObjectEndMarker", "ArrayEndMarker"
    };

    private static readonly Dictionary<string, string> TypeToFieldClass = new(StringComparer.Ordinal)
    {
        ["Uint8"] = "Uint8Field",
        ["UInt8"] = "Uint8Field",
        ["Uint16"] = "Uint16Field",
        ["UInt16"] = "Uint16Field",
        ["Uint32"] = "Uint32Field",
        ["UInt32"] = "Uint32Field",
        ["Uint64"] = "Uint64Field",
        ["UInt64"] = "Uint64Field",
        ["Hash128"] = "Hash128Field",
        ["Hash160"] = "Hash160Field",
        ["Hash192"] = "Hash192Field",
        ["Hash256"] = "Hash256Field",
        ["Amount"] = "AmountField",
        ["Blob"] = "BlobField",
        ["AccountID"] = "AccountIdField",
        ["STObject"] = "StObjectField",
        ["STArray"] = "StArrayField",
        ["PathSet"] = "PathSetField",
        ["Vector256"] = "Vector256Field",
        ["Issue"] = "IssueField",
        ["Currency"] = "CurrencyField",
        ["Number"] = "NumberField",
        ["Int32"] = "Int32Field",
        ["Int64"] = "Int64Field",
        ["XChainBridge"] = "XChainBridgeField",
    };

    private static readonly Dictionary<string, string> TypeToFileName = new(StringComparer.Ordinal)
    {
        ["Uint8"] = "Uint8",
        ["UInt8"] = "Uint8",
        ["Uint16"] = "Uint16",
        ["UInt16"] = "Uint16",
        ["Uint32"] = "Uint32",
        ["UInt32"] = "Uint32",
        ["Uint64"] = "Uint64",
        ["UInt64"] = "Uint64",
        ["Hash128"] = "Hash128",
        ["Hash160"] = "Hash160",
        ["Hash192"] = "Hash192",
        ["Hash256"] = "Hash256",
        ["Amount"] = "Amount",
        ["Blob"] = "Blob",
        ["AccountID"] = "AccountId",
        ["STObject"] = "StObject",
        ["STArray"] = "StArray",
        ["PathSet"] = "PathSet",
        ["Vector256"] = "Vector256",
        ["Issue"] = "Issue",
        ["Currency"] = "Currency",
        ["Number"] = "Number",
        ["Int32"] = "Int32",
        ["Int64"] = "Int64",
        ["XChainBridge"] = "XChainBridge",
    };

    private static int _filesWritten;
    private static int _filesUnchanged;
    private static bool _forceRewrite;

    private static void Main(string[] args)
    {
        string repoRoot = FindRepoRoot();
        List<string> positionalArgs = new();

        foreach (string arg in args)
        {
            if (arg is "--force" or "-f")
                _forceRewrite = true;
            else
                positionalArgs.Add(arg);
        }

        string definitionsPath = positionalArgs.Count > 0
            ? positionalArgs[0]
            : Path.Combine(repoRoot, "Base", "Xrpl.BinaryCodec", "Enums", "definitions.json");

        if (!File.Exists(definitionsPath))
        {
            Console.Error.WriteLine($"ERROR: definitions.json not found at: {definitionsPath}");
            Environment.Exit(1);
        }

        string outputDir = Path.Combine(repoRoot, "Base", "Xrpl.BinaryCodec", "Enums");
        string json = File.ReadAllText(definitionsPath);
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        Console.WriteLine("=== Comparing definitions.json with existing generated files ===");
        if (_forceRewrite)
            Console.WriteLine("    (--force: all files will be rewritten)");
        Console.WriteLine();

        GenerateEngineResult(root.GetProperty("TRANSACTION_RESULTS"), outputDir);
        GenerateTransactionType(root.GetProperty("TRANSACTION_TYPES"), outputDir);
        GenerateLedgerEntryType(root.GetProperty("LEDGER_ENTRY_TYPES"), outputDir);
        GenerateFields(root.GetProperty("FIELDS"), outputDir);

        Console.WriteLine();
        Console.WriteLine($"Done. Written: {_filesWritten}, Unchanged: {_filesUnchanged}");
        Console.WriteLine($"Generated files in: {outputDir}");
    }

    #region Semantic model

    private record EnumEntry(string Name, int Code);

    private record FieldEntry(string Name, int Nth, bool IsSigningField, bool IsSerialized);

    #endregion

    #region Parsing existing .Generated.cs files

    private static readonly Regex EnumLineRegex = new(
        @"public static readonly \w+ (\w+)\s*=\s*Add\(nameof\(\w+\),\s*(-?\d+)\)",
        RegexOptions.Compiled);

    private static readonly Regex FieldLineRegex = new(
        @"public static readonly (\w+) (\w+)\s*=\s*new \w+\(nameof\(\w+\),\s*(\d+)(?:,\s*isSigningField:\s*(true|false))?(?:,\s*isSerialised:\s*(true|false))?\)",
        RegexOptions.Compiled);

    private static Dictionary<string, int> ParseExistingEnumFile(string path)
    {
        Dictionary<string, int> result = new();
        if (!File.Exists(path))
            return result;

        foreach (string line in File.ReadLines(path))
        {
            Match m = EnumLineRegex.Match(line);
            if (m.Success)
                result[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
        }
        return result;
    }

    private static Dictionary<string, (int Nth, bool IsSigningField, bool IsSerialized)> ParseExistingFieldFile(string path)
    {
        Dictionary<string, (int, bool, bool)> result = new();
        if (!File.Exists(path))
            return result;

        foreach (string line in File.ReadLines(path))
        {
            Match m = FieldLineRegex.Match(line);
            if (m.Success)
            {
                string name = m.Groups[2].Value;
                int nth = int.Parse(m.Groups[3].Value);
                bool isSigningField = m.Groups[4].Success ? bool.Parse(m.Groups[4].Value) : true;
                bool isSerialized = m.Groups[5].Success ? bool.Parse(m.Groups[5].Value) : true;
                result[name] = (nth, isSigningField, isSerialized);
            }
        }
        return result;
    }

    #endregion

    #region Semantic diff & reporting

    private static bool ReportEnumDiff(string label, Dictionary<string, int> existing, List<EnumEntry> newEntries)
    {
        Dictionary<string, int> newMap = newEntries.ToDictionary(e => e.Name, e => e.Code);

        List<string> added = new();
        List<string> removed = new();
        List<string> changed = new();

        foreach (EnumEntry entry in newEntries)
        {
            if (!existing.ContainsKey(entry.Name))
                added.Add($"    + {entry.Name} (code: {entry.Code})");
            else if (existing[entry.Name] != entry.Code)
                changed.Add($"    ~ {entry.Name}: code {existing[entry.Name]} -> {entry.Code}");
        }

        foreach (string name in existing.Keys)
        {
            if (!newMap.ContainsKey(name))
                removed.Add($"    - {name} (was code: {existing[name]})");
        }

        bool hasChanges = added.Count > 0 || removed.Count > 0 || changed.Count > 0;

        Console.WriteLine($"[{label}] {newEntries.Count} entries" +
            (hasChanges ? "" : " (no changes)"));

        foreach (string s in added) Console.WriteLine(s);
        foreach (string s in removed) Console.WriteLine(s);
        foreach (string s in changed) Console.WriteLine(s);

        return hasChanges;
    }

    private static bool ReportFieldDiff(
        string typeName,
        Dictionary<string, (int Nth, bool IsSigningField, bool IsSerialized)> existing,
        List<FieldEntry> newEntries)
    {
        Dictionary<string, FieldEntry> newMap = newEntries.ToDictionary(e => e.Name);

        List<string> added = new();
        List<string> removed = new();
        List<string> changed = new();

        foreach (FieldEntry entry in newEntries)
        {
            if (!existing.ContainsKey(entry.Name))
            {
                added.Add($"    + {entry.Name} (nth: {entry.Nth})");
            }
            else
            {
                var old = existing[entry.Name];
                List<string> diffs = new();
                if (old.Nth != entry.Nth)
                    diffs.Add($"nth {old.Nth}->{entry.Nth}");
                if (old.IsSigningField != entry.IsSigningField)
                    diffs.Add($"isSigningField {old.IsSigningField}->{entry.IsSigningField}");
                if (old.IsSerialized != entry.IsSerialized)
                    diffs.Add($"isSerialized {old.IsSerialized}->{entry.IsSerialized}");
                if (diffs.Count > 0)
                    changed.Add($"    ~ {entry.Name}: {string.Join(", ", diffs)}");
            }
        }

        foreach (string name in existing.Keys)
        {
            if (!newMap.ContainsKey(name))
                removed.Add($"    - {name} (was nth: {existing[name].Nth})");
        }

        bool hasChanges = added.Count > 0 || removed.Count > 0 || changed.Count > 0;

        if (hasChanges)
        {
            Console.WriteLine($"  [{typeName}] {newEntries.Count} fields:");
            foreach (string s in added) Console.WriteLine(s);
            foreach (string s in removed) Console.WriteLine(s);
            foreach (string s in changed) Console.WriteLine(s);
        }

        return hasChanges;
    }

    #endregion

    #region Validation

    private static void ValidateFields(Dictionary<string, List<FieldEntry>> grouped)
    {
        foreach (var (typeName, fields) in grouped)
        {
            var duplicateNths = fields
                .GroupBy(f => f.Nth)
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var dup in duplicateNths)
            {
                string names = string.Join(", ", dup.Select(f => f.Name));
                Console.Error.WriteLine($"  ! CONFLICT: Duplicate nth={dup.Key} in {typeName}: {names}");
            }
        }
    }

    #endregion

    #region Generation

    private static void GenerateEngineResult(JsonElement results, string outputDir)
    {
        List<EnumEntry> entries = new();
        foreach (JsonProperty prop in results.EnumerateObject())
            entries.Add(new EnumEntry(prop.Name, prop.Value.GetInt32()));

        entries.Sort((a, b) => a.Code.CompareTo(b.Code));

        string path = Path.Combine(outputDir, "EngineResult.Generated.cs");
        Dictionary<string, int> existing = ParseExistingEnumFile(path);
        bool hasChanges = ReportEnumDiff("EngineResult", existing, entries);

        if (!hasChanges && !_forceRewrite && File.Exists(path))
        {
            _filesUnchanged++;
            return;
        }

        StringBuilder sb = new();
        WriteAutoGeneratedHeader(sb, "Xrpl.BinaryCodec.Enums");
        sb.AppendLine("    public partial class EngineResult");
        sb.AppendLine("    {");

        string? currentPrefix = null;
        foreach (EnumEntry entry in entries)
        {
            string prefix = GetPrefix(entry.Name);
            if (prefix != currentPrefix)
            {
                if (currentPrefix != null) sb.AppendLine();
                sb.AppendLine($"        // ─── {prefix} ───");
                currentPrefix = prefix;
            }
            sb.AppendLine($"        public static readonly EngineResult {entry.Name} = Add(nameof({entry.Name}), {entry.Code});");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Console.WriteLine($"  -> Written: {Path.GetFileName(path)} ({entries.Count} entries)");
        _filesWritten++;
    }

    private static void GenerateTransactionType(JsonElement types, string outputDir)
    {
        List<EnumEntry> entries = new();
        foreach (JsonProperty prop in types.EnumerateObject())
            entries.Add(new EnumEntry(prop.Name, prop.Value.GetInt32()));

        entries.Sort((a, b) => a.Code.CompareTo(b.Code));

        string path = Path.Combine(outputDir, "TransactionType.Generated.cs");
        Dictionary<string, int> existing = ParseExistingEnumFile(path);
        bool hasChanges = ReportEnumDiff("TransactionType", existing, entries);

        if (!hasChanges && !_forceRewrite && File.Exists(path))
        {
            _filesUnchanged++;
            return;
        }

        StringBuilder sb = new();
        WriteAutoGeneratedHeader(sb, "Xrpl.BinaryCodec.Types");
        sb.AppendLine("    public partial class TransactionType");
        sb.AppendLine("    {");

        foreach (EnumEntry entry in entries)
            sb.AppendLine($"        public static readonly TransactionType {entry.Name} = Add(nameof({entry.Name}), {entry.Code});");

        sb.AppendLine("    }");
        sb.AppendLine("}");

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Console.WriteLine($"  -> Written: {Path.GetFileName(path)} ({entries.Count} entries)");
        _filesWritten++;
    }

    private static void GenerateLedgerEntryType(JsonElement types, string outputDir)
    {
        List<EnumEntry> entries = new();
        foreach (JsonProperty prop in types.EnumerateObject())
            entries.Add(new EnumEntry(prop.Name, prop.Value.GetInt32()));

        entries.Sort((a, b) => a.Code.CompareTo(b.Code));

        string path = Path.Combine(outputDir, "LedgerEntryType.Generated.cs");
        Dictionary<string, int> existing = ParseExistingEnumFile(path);
        bool hasChanges = ReportEnumDiff("LedgerEntryType", existing, entries);

        if (!hasChanges && !_forceRewrite && File.Exists(path))
        {
            _filesUnchanged++;
            return;
        }

        StringBuilder sb = new();
        WriteAutoGeneratedHeader(sb, "Xrpl.BinaryCodec.Enums");
        sb.AppendLine("    public partial class LedgerEntryType");
        sb.AppendLine("    {");

        foreach (EnumEntry entry in entries)
            sb.AppendLine($"        public static readonly LedgerEntryType {entry.Name} = Add(nameof({entry.Name}), {entry.Code});");

        sb.AppendLine("    }");
        sb.AppendLine("}");

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Console.WriteLine($"  -> Written: {Path.GetFileName(path)} ({entries.Count} entries)");
        _filesWritten++;
    }

    private static void GenerateFields(JsonElement fields, string outputDir)
    {
        var grouped = new Dictionary<string, List<FieldEntry>>();

        foreach (JsonElement entry in fields.EnumerateArray())
        {
            string name = entry[0].GetString()!;
            JsonElement props = entry[1];

            if (SkippedFields.Contains(name))
                continue;

            string typeName = props.GetProperty("type").GetString()!;

            if (!TypeToFieldClass.ContainsKey(typeName))
            {
                Console.Error.WriteLine($"  ! Unknown field type '{typeName}' for field '{name}', skipping.");
                continue;
            }

            int nth = props.GetProperty("nth").GetInt32();
            bool isSigningField = props.GetProperty("isSigningField").GetBoolean();
            bool isSerialized = props.GetProperty("isSerialized").GetBoolean();

            if (!grouped.ContainsKey(typeName))
                grouped[typeName] = new();

            grouped[typeName].Add(new FieldEntry(name, nth, isSigningField, isSerialized));
        }

        ValidateFields(grouped);

        int totalFields = 0;
        Console.WriteLine($"[Fields] {grouped.Sum(g => g.Value.Count)} fields across {grouped.Count} types:");

        foreach (var (typeName, fieldList) in grouped.OrderBy(kv => kv.Key))
        {
            string fieldClassName = TypeToFieldClass[typeName];
            string fileName = TypeToFileName[typeName];

            fieldList.Sort((a, b) => a.Nth.CompareTo(b.Nth));

            string path = Path.Combine(outputDir, $"Field.{fileName}.Generated.cs");
            var existing = ParseExistingFieldFile(path);
            bool hasChanges = ReportFieldDiff(fileName, existing, fieldList);

            if (!hasChanges && !_forceRewrite && File.Exists(path))
            {
                _filesUnchanged++;
                totalFields += fieldList.Count;
                continue;
            }

            StringBuilder sb = new();
            WriteAutoGeneratedHeader(sb, "Xrpl.BinaryCodec.Enums");
            sb.AppendLine("    public partial class Field");
            sb.AppendLine("    {");

            foreach (FieldEntry field in fieldList)
            {
                string ctorArgs = BuildCtorArgs(field.Name, field.Nth, field.IsSigningField, field.IsSerialized);
                sb.AppendLine($"        public static readonly {fieldClassName} {field.Name} = new {fieldClassName}({ctorArgs});");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            Console.WriteLine($"  -> Written: Field.{fileName}.Generated.cs ({fieldList.Count} fields)");
            _filesWritten++;
            totalFields += fieldList.Count;
        }

        Console.WriteLine($"  Total: {totalFields} fields");
    }

    #endregion

    #region Helpers

    private static void WriteAutoGeneratedHeader(StringBuilder sb, string ns)
    {
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// This file is auto-generated from definitions.json by Tools/GenerateEnums.");
        sb.AppendLine("// Do not edit manually. Run: dotnet run --project Tools/GenerateEnums");
        sb.AppendLine("#pragma warning disable CS1591");
        sb.AppendLine($"namespace {ns}");
        sb.AppendLine("{");
    }

    private static string BuildCtorArgs(string name, int nth, bool isSigningField, bool isSerialized)
    {
        string args = $"nameof({name}), {nth}";

        if (!isSigningField && !isSerialized)
            args += ", isSigningField: false, isSerialised: false";
        else if (!isSigningField)
            args += ", isSigningField: false";
        else if (!isSerialized)
            args += ", isSerialised: false";

        return args;
    }

    private static string GetPrefix(string name)
    {
        if (name.StartsWith("tel")) return "tel";
        if (name.StartsWith("tem")) return "tem";
        if (name.StartsWith("tef")) return "tef";
        if (name.StartsWith("ter")) return "ter";
        if (name.StartsWith("tes")) return "tes";
        if (name.StartsWith("tec")) return "tec";
        return "unknown";
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            if (File.Exists(Path.Combine(dir, "Base", "Xrpl.BinaryCodec", "Enums", "definitions.json")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        return Directory.GetCurrentDirectory();
    }

    #endregion
}
