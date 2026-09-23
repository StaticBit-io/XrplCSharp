using System.Linq;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec;
using Xrpl.BinaryCodec.Enums;

namespace XrplTests.BinaryCodecLib
{
    /// <summary>
    /// rippled reports PermissionValue as a name and accepts either form on input
    /// (<c>STUInt32::getJson</c> and <c>parseUInt32</c>), so a document that went through this
    /// codec has to read like one that came from a node.
    /// </summary>
    [TestClass]
    public class TestUPermissionValueRoundTrip
    {
        private const string Owner = "rH438jEAzTs5PYtV6CHZqpDpwCKQmPW9Cg";
        private const string Delegate = "rLNaPoKeeBjZe2qs6x52yVPZpZ8td4dc6w";

        /// <summary>
        /// Built by parsing JSON text rather than composing JsonObject by hand: a JsonValue made
        /// from a C# int does not read back as a uint, which is a property of JsonNode and not of
        /// anything under test.
        /// </summary>
        private static JsonObject DelegateSet(params string[] permissionValueTokens)
        {
            string permissions = string.Join(",",
                permissionValueTokens.Select(token =>
                    $@"{{ ""Permission"": {{ ""PermissionValue"": {token} }} }}"));

            string json = $@"{{
                ""TransactionType"": ""DelegateSet"",
                ""Account"": ""{Owner}"",
                ""Authorize"": ""{Delegate}"",
                ""Fee"": ""12"",
                ""Sequence"": 42,
                ""Permissions"": [{permissions}]
            }}";

            return JsonNode.Parse(json).AsObject();
        }

        private static string Name(string permissionName) => $@"""{permissionName}""";

        private static JsonNode FirstPermissionValue(JsonNode decoded) =>
            decoded["Permissions"][0]["Permission"]["PermissionValue"];

        [TestMethod]
        public void TestEncodeAcceptsAName()
        {
            // 2034 00010001 is PermissionValue 65537.
            string fromName = XrplBinaryCodec.Encode(DelegateSet(Name("TrustlineAuthorize")));
            string fromNumber = XrplBinaryCodec.Encode(DelegateSet("65537"));

            Assert.AreEqual(fromNumber, fromName);
            StringAssert.Contains(fromName, "203400010001");
        }

        [TestMethod]
        public void TestEncodeAcceptsATransactionTypeName()
        {
            // Payment is transaction type 0, so its permission value is 1.
            string fromName = XrplBinaryCodec.Encode(DelegateSet(Name("Payment")));
            string fromNumber = XrplBinaryCodec.Encode(DelegateSet("1"));

            Assert.AreEqual(fromNumber, fromName);
        }

        [TestMethod]
        public void TestDecodeReportsTheName()
        {
            string encoded = XrplBinaryCodec.Encode(DelegateSet("65537"));

            JsonNode decoded = XrplBinaryCodec.Decode(encoded);

            Assert.AreEqual("TrustlineAuthorize", FirstPermissionValue(decoded).GetValue<string>());
        }

        [TestMethod]
        public void TestNameSurvivesTheRoundTrip()
        {
            JsonObject original = DelegateSet(Name("AccountDomainSet"));

            JsonNode decoded = XrplBinaryCodec.Decode(XrplBinaryCodec.Encode(original));

            Assert.AreEqual("AccountDomainSet", FirstPermissionValue(decoded).GetValue<string>());
        }

        [TestMethod]
        public void TestDecodeKeepsTheNumberWhenItHasNoName()
        {
            // A value from an amendment this build predates has no name to report, and inventing
            // one would be worse than showing the number the node sent.
            string encoded = XrplBinaryCodec.Encode(DelegateSet("70000"));

            JsonNode decoded = XrplBinaryCodec.Decode(encoded);

            Assert.AreEqual(70000u, FirstPermissionValue(decoded).GetValue<uint>());
        }

        [TestMethod]
        public void TestEncodeRefusesAnUnknownName()
        {
            // Signing a permission this build cannot name would put a value on the ledger that
            // nobody asked for, so the codec fails rather than guessing.
            InvalidJsonException exception = Assert.ThrowsExactly<InvalidJsonException>(
                () => XrplBinaryCodec.Encode(DelegateSet(Name("BrandNewPermissionV2"))));

            StringAssert.Contains(exception.Message, "BrandNewPermissionV2");
        }

        [TestMethod]
        public void TestOtherUint32FieldsStillDecodeAsNumbers()
        {
            // The name mapping belongs to PermissionValue alone: Sequence is a UInt32 too, and a
            // permission name must not leak onto every field of that type.
            string encoded = XrplBinaryCodec.Encode(DelegateSet("65537"));

            JsonNode decoded = XrplBinaryCodec.Decode(encoded);

            Assert.AreEqual(42u, decoded["Sequence"].GetValue<uint>());
        }

        [TestMethod]
        public void TestMappingCoversBothKinds()
        {
            Assert.IsTrue(DelegatablePermissions.TryGetValue("TrustlineAuthorize", out uint granular));
            Assert.AreEqual(65537u, granular);

            Assert.IsTrue(DelegatablePermissions.TryGetValue("Payment", out uint transactionType));
            Assert.AreEqual(1u, transactionType);

            Assert.IsTrue(DelegatablePermissions.TryGetName(65537, out string granularName));
            Assert.AreEqual("TrustlineAuthorize", granularName);

            Assert.IsFalse(DelegatablePermissions.TryGetName(0, out _));
            Assert.IsFalse(DelegatablePermissions.TryGetValue("BrandNewPermissionV2", out _));
        }
    }
}
