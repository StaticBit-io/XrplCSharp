using System;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec;
using Xrpl.BinaryCodec.Binary;
using Xrpl.BinaryCodec.Types;

namespace XrplTests.BinaryCodecLib.Types;

[TestClass]
public class TestXChainBridgeType
{
    private const string LockingDoorAddress = "r9LqNeG6qHxjeUocjvVki2XR35weJ9mZgQ";
    private const string IssuingDoorAddress = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";

    private static JsonObject CreateXrpBridgeJson()
    {
        return new JsonObject
        {
            ["LockingChainDoor"] = LockingDoorAddress,
            ["LockingChainIssue"] = new JsonObject { ["currency"] = "XRP" },
            ["IssuingChainDoor"] = IssuingDoorAddress,
            ["IssuingChainIssue"] = new JsonObject { ["currency"] = "XRP" }
        };
    }

    private static JsonObject CreateIouBridgeJson()
    {
        const string lockIssuer = "rGFpans8aW7XZNEcNky6RHKyEdLvXPMnUn";
        const string issuIssuer = "rPk2dXr27rMw9G5Ej9ad2Tt7RJzGy8ycBp";
        return new JsonObject
        {
            ["LockingChainDoor"] = LockingDoorAddress,
            ["LockingChainIssue"] = new JsonObject
            {
                ["currency"] = "USD",
                ["issuer"] = lockIssuer
            },
            ["IssuingChainDoor"] = IssuingDoorAddress,
            ["IssuingChainIssue"] = new JsonObject
            {
                ["currency"] = "USD",
                ["issuer"] = issuIssuer
            }
        };
    }

    [TestMethod]
    public void TestFromJson_XrpBridge()
    {
        JsonObject json = CreateXrpBridgeJson();
        XChainBridgeType bridge = XChainBridgeType.FromJson(json);

        Assert.AreEqual(LockingDoorAddress, bridge.LockingChainDoor.ToString());
        Assert.AreEqual(IssuingDoorAddress, bridge.IssuingChainDoor.ToString());
        Assert.IsTrue(bridge.LockingChainIssue.Currency.IsNative);
        Assert.IsTrue(bridge.IssuingChainIssue.Currency.IsNative);
    }

    [TestMethod]
    public void TestFromJson_IouBridge()
    {
        JsonObject json = CreateIouBridgeJson();
        XChainBridgeType bridge = XChainBridgeType.FromJson(json);

        Assert.AreEqual(LockingDoorAddress, bridge.LockingChainDoor.ToString());
        Assert.AreEqual(IssuingDoorAddress, bridge.IssuingChainDoor.ToString());
        Assert.IsFalse(bridge.LockingChainIssue.Currency.IsNative);
        Assert.IsFalse(bridge.IssuingChainIssue.Currency.IsNative);
    }

    [TestMethod]
    public void TestRoundtrip_XrpBridge()
    {
        JsonObject json = CreateXrpBridgeJson();
        XChainBridgeType original = XChainBridgeType.FromJson(json);

        BytesList sink = new BytesList();
        original.ToBytes(sink);
        byte[] bytes = sink.ToBytes();
        string hex = BitConverter.ToString(bytes).Replace("-", "");

        BufferParser parser = new BufferParser(hex);
        XChainBridgeType deserialized = XChainBridgeType.FromParser(parser);

        Assert.AreEqual(LockingDoorAddress, deserialized.LockingChainDoor.ToString());
        Assert.AreEqual(IssuingDoorAddress, deserialized.IssuingChainDoor.ToString());
        Assert.IsTrue(deserialized.LockingChainIssue.Currency.IsNative);
        Assert.IsTrue(deserialized.IssuingChainIssue.Currency.IsNative);
    }

    [TestMethod]
    public void TestRoundtrip_IouBridge()
    {
        JsonObject json = CreateIouBridgeJson();
        XChainBridgeType original = XChainBridgeType.FromJson(json);

        BytesList sink = new BytesList();
        original.ToBytes(sink);
        byte[] bytes = sink.ToBytes();
        string hex = BitConverter.ToString(bytes).Replace("-", "");

        BufferParser parser = new BufferParser(hex);
        XChainBridgeType deserialized = XChainBridgeType.FromParser(parser);

        Assert.AreEqual(LockingDoorAddress, deserialized.LockingChainDoor.ToString());
        Assert.AreEqual(IssuingDoorAddress, deserialized.IssuingChainDoor.ToString());
        Assert.IsFalse(deserialized.LockingChainIssue.Currency.IsNative);
        Assert.IsFalse(deserialized.IssuingChainIssue.Currency.IsNative);
    }

    [TestMethod]
    public void TestToJson_Structure()
    {
        JsonObject json = CreateXrpBridgeJson();
        XChainBridgeType bridge = XChainBridgeType.FromJson(json);
        JsonNode result = bridge.ToJson();

        Assert.IsInstanceOfType(result, typeof(JsonObject));
        JsonObject obj = result.AsObject();
        Assert.AreEqual(LockingDoorAddress, obj["LockingChainDoor"].GetValue<string>());
        Assert.AreEqual(IssuingDoorAddress, obj["IssuingChainDoor"].GetValue<string>());
        Assert.AreEqual("XRP", obj["LockingChainIssue"]["currency"].GetValue<string>());
        Assert.AreEqual("XRP", obj["IssuingChainIssue"]["currency"].GetValue<string>());
    }

    [TestMethod]
    public void TestBinarySize_XrpBridge()
    {
        JsonObject json = CreateXrpBridgeJson();
        XChainBridgeType bridge = XChainBridgeType.FromJson(json);
        BytesList sink = new BytesList();
        bridge.ToBytes(sink);

        // XRP bridge: AccountID(20) + Issue_XRP(20) + AccountID(20) + Issue_XRP(20) = 80 bytes
        Assert.AreEqual(80, sink.BytesLength());
    }

    [TestMethod]
    public void TestBinarySize_IouBridge()
    {
        JsonObject json = CreateIouBridgeJson();
        XChainBridgeType bridge = XChainBridgeType.FromJson(json);
        BytesList sink = new BytesList();
        bridge.ToBytes(sink);

        // IOU bridge: AccountID(20) + Issue_IOU(20+20) + AccountID(20) + Issue_IOU(20+20) = 120 bytes
        Assert.AreEqual(120, sink.BytesLength());
    }

    [TestMethod]
    public void TestFromJson_NotObject_Throws()
    {
        JsonNode json = JsonValue.Create("invalid");
        bool threw = false;
        try { XChainBridgeType.FromJson(json); }
        catch (InvalidJsonException) { threw = true; }
        Assert.IsTrue(threw, "Expected InvalidJsonException was not thrown.");
    }
}