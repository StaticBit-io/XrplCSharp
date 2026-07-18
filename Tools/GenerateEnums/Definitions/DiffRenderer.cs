using System.Text;
using System.Text.Json;

namespace GenerateEnums;

/// <summary>Formats a DiffResult as a human table or machine JSON.</summary>
public static class DiffRenderer
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public static string RenderTable(DiffResult result)
    {
        StringBuilder sb = new();
        int driftSections = 0, nodeOnly = 0, mismatch = 0, localOnly = 0;

        foreach (SectionDiff s in result.Sections)
        {
            bool any = s.NodeOnly.Count > 0 || s.LocalOnly.Count > 0 || s.Mismatch.Count > 0;
            if (!any)
            {
                sb.AppendLine($"{s.Section} … (no differences)");
                continue;
            }
            if (s.NodeOnly.Count > 0 || s.Mismatch.Count > 0) driftSections++;
            sb.AppendLine(s.Section);
            foreach (string n in s.NodeOnly) { sb.AppendLine($"  node-only (SDK behind):   + {n}"); nodeOnly++; }
            foreach (Mismatch m in s.Mismatch) { sb.AppendLine($"  mismatch:                 ~ {m.Name}: {m.Field} {m.Local} -> {m.Server}"); mismatch++; }
            foreach (string n in s.LocalOnly) { sb.AppendLine($"  local-only (info):        - {n}"); localOnly++; }
        }

        sb.AppendLine(
            $"Summary: drift in {driftSections}/{result.Sections.Count} sections — " +
            $"{nodeOnly} node-only, {mismatch} mismatch, {localOnly} local-only.");
        return sb.ToString();
    }

    public static string RenderJson(DiffResult result) => JsonSerializer.Serialize(result, IndentedJson);
}
