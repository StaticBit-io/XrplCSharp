using System.Collections.Generic;
using System.Linq;

using GenerateEnums;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XrplTests.GenerateEnums;

[TestClass]
public class TestUDefinitionsDiff
{
    private static Definitions Make(
        Dictionary<string, FieldDef>? fields = null,
        Dictionary<string, int>? txTypes = null) =>
        new(
            fields ?? new(),
            new Dictionary<string, int>(),
            new Dictionary<string, int>(),
            new Dictionary<string, int>(),
            txTypes ?? new());

    private static SectionDiff Section(DiffResult r, string name) =>
        r.Sections.Single(s => s.Section == name);

    [TestMethod]
    public void TestUDiff_NodeOnly_IsDriftAndListed()
    {
        Definitions local = Make(txTypes: new() { ["Payment"] = 0 });
        Definitions server = Make(txTypes: new() { ["Payment"] = 0, ["Batch"] = 40 });

        DiffResult r = DefinitionsDiff.Compare(local, server);

        SectionDiff s = Section(r, "TRANSACTION_TYPES");
        CollectionAssert.AreEquivalent(new[] { "Batch" }, s.NodeOnly.ToArray());
        Assert.AreEqual(0, s.LocalOnly.Count);
        Assert.AreEqual(0, s.Mismatch.Count);
        Assert.IsTrue(r.HasDrift);
    }

    [TestMethod]
    public void TestUDiff_LocalOnly_IsInformationalNotDrift()
    {
        Definitions local = Make(txTypes: new() { ["Payment"] = 0, ["FutureTx"] = 99 });
        Definitions server = Make(txTypes: new() { ["Payment"] = 0 });

        DiffResult r = DefinitionsDiff.Compare(local, server);

        SectionDiff s = Section(r, "TRANSACTION_TYPES");
        CollectionAssert.AreEquivalent(new[] { "FutureTx" }, s.LocalOnly.ToArray());
        Assert.AreEqual(0, s.NodeOnly.Count);
        Assert.IsFalse(r.HasDrift, "local-only entries must not count as drift");
    }

    [TestMethod]
    public void TestUDiff_CodeMismatch_IsDrift()
    {
        Definitions local = Make(txTypes: new() { ["Payment"] = 0 });
        Definitions server = Make(txTypes: new() { ["Payment"] = 1 });

        DiffResult r = DefinitionsDiff.Compare(local, server);

        SectionDiff s = Section(r, "TRANSACTION_TYPES");
        Assert.AreEqual(1, s.Mismatch.Count);
        Mismatch m = s.Mismatch[0];
        Assert.AreEqual("Payment", m.Name);
        Assert.AreEqual("code", m.Field);
        Assert.AreEqual("0", m.Local);
        Assert.AreEqual("1", m.Server);
        Assert.IsTrue(r.HasDrift);
    }

    [TestMethod]
    public void TestUDiff_FieldPropertyMismatch_OneRowPerProperty()
    {
        var local = new Dictionary<string, FieldDef>
        {
            ["Sponsor"] = new("AccountID", 27, IsSigningField: true, IsSerialized: true, IsVLEncoded: true),
        };
        var server = new Dictionary<string, FieldDef>
        {
            ["Sponsor"] = new("AccountID", 28, IsSigningField: false, IsSerialized: true, IsVLEncoded: true),
        };

        DiffResult r = DefinitionsDiff.Compare(Make(fields: local), Make(fields: server));

        SectionDiff s = Section(r, "FIELDS");
        // nth 27->28 and isSigningField true->false => two mismatch rows
        Assert.AreEqual(2, s.Mismatch.Count);
        CollectionAssert.AreEquivalent(
            new[] { "nth", "isSigningField" },
            s.Mismatch.Select(m => m.Field).ToArray());
        Assert.IsTrue(r.HasDrift);
    }

    [TestMethod]
    public void TestUDiff_Identical_NoDrift()
    {
        Definitions local = Make(txTypes: new() { ["Payment"] = 0 });
        Definitions server = Make(txTypes: new() { ["Payment"] = 0 });

        DiffResult r = DefinitionsDiff.Compare(local, server);

        Assert.IsFalse(r.HasDrift);
        Assert.IsTrue(r.Sections.All(s =>
            s.NodeOnly.Count == 0 && s.LocalOnly.Count == 0 && s.Mismatch.Count == 0));
    }

    [TestMethod]
    public void TestUDiff_HasAllFiveSections()
    {
        DiffResult r = DefinitionsDiff.Compare(Make(), Make());
        CollectionAssert.AreEquivalent(
            new[] { "FIELDS", "TYPES", "LEDGER_ENTRY_TYPES", "TRANSACTION_RESULTS", "TRANSACTION_TYPES" },
            r.Sections.Select(s => s.Section).ToArray());
    }
}
