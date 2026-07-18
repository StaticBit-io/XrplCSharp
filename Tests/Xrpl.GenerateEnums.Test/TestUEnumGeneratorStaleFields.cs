using System.Collections.Generic;
using System.Linq;

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
}
