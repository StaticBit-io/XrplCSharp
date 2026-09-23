// Mirrors rippled's GranularPermissionType, declared in
// include/xrpl/protocol/detail/permissions.macro

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;

using Xrpl.Client.Json.Converters;
using Xrpl.BinaryCodec.Enums;
using Xrpl.Tests.Models.Tests;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// The enum is the SDK's copy of a protocol table that lives only in a C++ macro, so what
    /// holds it true is the comparison with the vendored macro rather than review.
    /// </summary>
    [TestClass]
    public class TestUGranularPermissionConformance
    {
        [TestMethod]
        public void TestEnumMatchesRippledMacro()
        {
            Dictionary<string, uint> fromMacro = RippledGranularPermissions.Parse();

            Dictionary<string, uint> fromEnum = Enum.GetValues<GranularPermission>()
                .ToDictionary(permission => permission.ToString(), permission => (uint)permission);

            CollectionAssert.AreEquivalent(
                fromMacro.Keys.OrderBy(name => name).ToList(),
                fromEnum.Keys.OrderBy(name => name).ToList(),
                "GranularPermission no longer names the same permissions as rippled's permissions.macro");

            foreach (KeyValuePair<string, uint> expected in fromMacro)
            {
                Assert.AreEqual(
                    expected.Value,
                    fromEnum[expected.Key],
                    $"GranularPermission.{expected.Key} has the wrong value");
            }
        }

        [TestMethod]
        public void TestEveryPermissionExceedsTheTransactionTypeRange()
        {
            // rippled sets these above ushort.MaxValue on purpose, so that a granular value can
            // never collide with a transaction-type permission (type code + 1).
            foreach (GranularPermission permission in Enum.GetValues<GranularPermission>())
                Assert.IsGreaterThan(ushort.MaxValue, (uint)permission, $"{permission} is inside the transaction-type range");
        }

        [TestMethod]
        public void TestConverterResolvesEveryEnumMember()
        {
            // The converter's own table is built from the enum, so this fails if the two ever
            // stop agreeing - including when an enum member is added without a value.
            foreach (GranularPermission permission in Enum.GetValues<GranularPermission>())
            {
                Assert.IsTrue(
                    PermissionValueConverter.TryGetPermissionValue(permission.ToString(), out uint value),
                    $"the converter does not resolve {permission}");
                Assert.AreEqual((uint)permission, value);

                Assert.IsTrue(
                    PermissionValueConverter.TryGetPermissionName((uint)permission, out string name),
                    $"the converter does not name {(uint)permission}");
                Assert.AreEqual(permission.ToString(), name);
            }
        }

        [TestMethod]
        public void TestPermissionsApplyToKnownTransactionTypes()
        {
            // Each entry names the transaction it authorizes part of. The macro spells it as the
            // tt tag, so this checks the tag is one transactions.macro declares - a permission
            // pointing at a type that does not exist would mean the fixtures disagree.
            Dictionary<string, string> tags = RippledGranularPermissions.ParseTransactionTypeTags();
            Assert.IsNotEmpty(tags);

            string transactions = System.IO.File.ReadAllText(RippledTransactionFormats.FixturePath);

            foreach (KeyValuePair<string, string> entry in tags)
            {
                StringAssert.Contains(
                    transactions,
                    $"TRANSACTION({entry.Value},",
                    $"{entry.Key} names {entry.Value}, which transactions.macro does not declare");
            }
        }
    }
}
