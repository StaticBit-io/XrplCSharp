using System.Collections.Generic;
using System.Text.Json;

using GenerateEnums;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XrplTests.GenerateEnums;

[TestClass]
public class TestUDiffRenderer
{
    private static DiffResult Sample() => new(new List<SectionDiff>
    {
        new("FIELDS",
            NodeOnly: new[] { "NewField" },
            LocalOnly: new[] { "OldField" },
            Mismatch: new[] { new Mismatch("Sponsor", "nth", "27", "28") }),
        new("TYPES", new string[0], new string[0], new Mismatch[0]),
        new("LEDGER_ENTRY_TYPES", new string[0], new string[0], new Mismatch[0]),
        new("TRANSACTION_RESULTS", new string[0], new string[0], new Mismatch[0]),
        new("TRANSACTION_TYPES", new string[0], new string[0], new Mismatch[0]),
    });

    [TestMethod]
    public void TestURenderTable_ShowsCategoriesAndSummary()
    {
        string text = DiffRenderer.RenderTable(Sample());

        StringAssert.Contains(text, "FIELDS");
        StringAssert.Contains(text, "NewField");
        StringAssert.Contains(text, "OldField");
        StringAssert.Contains(text, "Sponsor");
        StringAssert.Contains(text, "27 -> 28");
        StringAssert.Contains(text, "Summary");
    }

    [TestMethod]
    public void TestURenderJson_IsParseableAndCarriesSections()
    {
        string json = DiffRenderer.RenderJson(Sample());
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement sections = doc.RootElement.GetProperty("Sections");
        Assert.AreEqual(5, sections.GetArrayLength());
    }
}
