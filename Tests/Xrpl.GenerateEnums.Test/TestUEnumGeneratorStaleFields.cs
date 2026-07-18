using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using GenerateEnums;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XrplTests.GenerateEnums;

/// <summary>
/// A field-type group that loses ALL its fields in a new definitions.json must
/// have its stale Field.&lt;Type&gt;.Generated.cs removed. These cover the pure
/// decision helpers behind that behavior (CodeRabbit finding on PR #57).
/// </summary>
[TestClass]
public class TestUEnumGeneratorStaleFields
{
    [TestMethod]
    public void TestUFieldFileStem_ExtractsTypeStem()
    {
        Assert.AreEqual("Uint16", EnumGenerator.FieldFileStem("Field.Uint16.Generated.cs"));
        Assert.AreEqual("AccountId", EnumGenerator.FieldFileStem("Field.AccountId.Generated.cs"));
        // Non field-generated files are not field-type files
        Assert.IsNull(EnumGenerator.FieldFileStem("EngineResult.Generated.cs"));
        Assert.IsNull(EnumGenerator.FieldFileStem("Field.Uint16.cs"));
        Assert.IsNull(EnumGenerator.FieldFileStem("TransactionType.Generated.cs"));
    }

    [TestMethod]
    public void TestUFindStaleFieldFileStems_FlagsTypeThatLostAllFields()
    {
        // The new definitions still produce Uint8 + AccountId field files; the
        // on-disk Uint16 file has no corresponding fields anymore -> stale.
        string[] expected = { "Uint8", "AccountId" };
        string[] onDisk = { "Uint8", "AccountId", "Uint16" };

        List<string> stale = EnumGenerator.FindStaleFieldFileStems(expected, onDisk);

        CollectionAssert.AreEqual(new[] { "Uint16" }, stale.ToArray());
    }

    [TestMethod]
    public void TestUFindStaleFieldFileStems_NoneWhenAllPresent()
    {
        string[] expected = { "Uint8", "AccountId", "Uint16" };
        string[] onDisk = { "Uint8", "AccountId", "Uint16" };

        Assert.AreEqual(0, EnumGenerator.FindStaleFieldFileStems(expected, onDisk).Count);
    }

    [TestMethod]
    public void TestUFindStaleFieldFileStems_SortedAndMultiple()
    {
        string[] expected = { "AccountId" };
        string[] onDisk = { "Uint16", "AccountId", "Blob" };

        List<string> stale = EnumGenerator.FindStaleFieldFileStems(expected, onDisk);

        // both Blob and Uint16 are stale, returned Ordinal-sorted
        CollectionAssert.AreEqual(new[] { "Blob", "Uint16" }, stale.ToArray());
    }

    private const string OneKnownField =
        "[[\"TradingFee\",{\"nth\":5,\"isVLEncoded\":false,\"isSerialized\":true,\"isSigningField\":true,\"type\":\"UInt16\"}]]";

    // one known field + one field with an UNKNOWN type
    private const string KnownPlusUnknownField =
        "[[\"TradingFee\",{\"nth\":5,\"isVLEncoded\":false,\"isSerialized\":true,\"isSigningField\":true,\"type\":\"UInt16\"}]," +
        "[\"Weird\",{\"nth\":9,\"isVLEncoded\":false,\"isSerialized\":true,\"isSigningField\":true,\"type\":\"BogusType\"}]]";

    [TestMethod]
    public void TestUGenerateFields_RemovesStaleFileWhenTypeGone()
    {
        string dir = Path.Combine(Path.GetTempPath(), "genenums_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string stale = Path.Combine(dir, "Field.Blob.Generated.cs");
            File.WriteAllText(stale, "// stale placeholder\n");

            using JsonDocument doc = JsonDocument.Parse(OneKnownField);
            EnumGenerator.GenerateFields(doc.RootElement, dir);

            Assert.IsFalse(File.Exists(stale), "a field-type file whose type has no fields left must be removed");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void TestUGenerateFields_PreservesFilesWhenUnknownTypePresent()
    {
        string dir = Path.Combine(Path.GetTempPath(), "genenums_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string stale = Path.Combine(dir, "Field.Blob.Generated.cs");
            File.WriteAllText(stale, "// stale placeholder\n");

            // an unknown field type is skipped and never enters `grouped`; the
            // stale-removal must be skipped entirely so a merely-skipped type's
            // file is not deleted by mistake
            using JsonDocument doc = JsonDocument.Parse(KnownPlusUnknownField);
            EnumGenerator.GenerateFields(doc.RootElement, dir);

            Assert.IsTrue(File.Exists(stale), "stale removal must be skipped when an unknown field type was encountered");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
