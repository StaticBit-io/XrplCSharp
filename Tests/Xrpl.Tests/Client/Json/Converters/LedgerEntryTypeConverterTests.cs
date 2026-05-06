using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;
using Xrpl.Models;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class LedgerEntryTypeConverterTests
{
    private static readonly JsonSerializerOptions Options = XrplJsonOptions.Default;

    [TestMethod]
    public void Read_KnownType_AccountRoot()
    {
        string json = "\"AccountRoot\"";
        LedgerEntryType result = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(LedgerEntryType.AccountRoot, result);
    }

    [TestMethod]
    public void Read_KnownType_Offer()
    {
        string json = "\"Offer\"";
        LedgerEntryType result = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(LedgerEntryType.Offer, result);
    }

    [TestMethod]
    public void Read_KnownType_RippleState()
    {
        string json = "\"RippleState\"";
        LedgerEntryType result = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(LedgerEntryType.RippleState, result);
    }

    [TestMethod]
    public void Read_KnownType_DirectoryNode()
    {
        string json = "\"DirectoryNode\"";
        LedgerEntryType result = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(LedgerEntryType.DirectoryNode, result);
    }

    [TestMethod]
    public void Read_KnownType_Escrow()
    {
        string json = "\"Escrow\"";
        LedgerEntryType result = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(LedgerEntryType.Escrow, result);
    }

    [TestMethod]
    public void Read_KnownType_Check()
    {
        string json = "\"Check\"";
        LedgerEntryType result = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(LedgerEntryType.Check, result);
    }

    [TestMethod]
    public void Read_KnownType_AMM()
    {
        string json = "\"AMM\"";
        LedgerEntryType result = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(LedgerEntryType.AMM, result);
    }

    [TestMethod]
    public void Read_KnownType_DID()
    {
        string json = "\"DID\"";
        LedgerEntryType result = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(LedgerEntryType.DID, result);
    }

    [TestMethod]
    public void Read_KnownType_Oracle()
    {
        string json = "\"Oracle\"";
        LedgerEntryType result = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(LedgerEntryType.Oracle, result);
    }

    [TestMethod]
    public void Read_KnownType_Credential()
    {
        string json = "\"Credential\"";
        LedgerEntryType result = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(LedgerEntryType.Credential, result);
    }

    [TestMethod]
    public void Read_KnownType_NFTokenPage()
    {
        string json = "\"NFTokenPage\"";
        LedgerEntryType result = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(LedgerEntryType.NFTokenPage, result);
    }

    [TestMethod]
    public void Read_KnownType_MPTokenIssuance()
    {
        string json = "\"MPTokenIssuance\"";
        LedgerEntryType result = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(LedgerEntryType.MPTokenIssuance, result);
    }

    [TestMethod]
    public void Read_UnknownString_ReturnsUnknown()
    {
        string json = "\"FutureLedgerType\"";
        LedgerEntryType result = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(LedgerEntryType.Unknown, result);
    }

    [TestMethod]
    public void Read_AnotherUnknownString_ReturnsUnknown()
    {
        string json = "\"XChainSomethingNew\"";
        LedgerEntryType result = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(LedgerEntryType.Unknown, result);
    }

    [TestMethod]
    public void Read_EmptyString_ReturnsUnknown()
    {
        string json = "\"\"";
        LedgerEntryType result = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(LedgerEntryType.Unknown, result);
    }

    [TestMethod]
    public void Read_CaseInsensitive_ReturnsCorrectType()
    {
        string json = "\"accountroot\"";
        LedgerEntryType result = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(LedgerEntryType.AccountRoot, result);
    }

    [TestMethod]
    public void Read_CaseInsensitive_UpperCase()
    {
        string json = "\"OFFER\"";
        LedgerEntryType result = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(LedgerEntryType.Offer, result);
    }

    [TestMethod]
    public void Read_InvalidTokenType_Throws()
    {
        string json = "true";
        bool threw = false;
        try { JsonSerializer.Deserialize<LedgerEntryType>(json, Options); }
        catch (JsonException) { threw = true; }
        Assert.IsTrue(threw, "Boolean token should throw JsonException");
    }

    [TestMethod]
    public void Write_KnownType_WritesString()
    {
        string json = JsonSerializer.Serialize(LedgerEntryType.AccountRoot, Options);
        Assert.AreEqual("\"AccountRoot\"", json);
    }

    [TestMethod]
    public void Write_Unknown_WritesString()
    {
        string json = JsonSerializer.Serialize(LedgerEntryType.Unknown, Options);
        Assert.AreEqual("\"Unknown\"", json);
    }

    [TestMethod]
    public void Write_Offer_WritesString()
    {
        string json = JsonSerializer.Serialize(LedgerEntryType.Offer, Options);
        Assert.AreEqual("\"Offer\"", json);
    }

    [TestMethod]
    public void RoundTrip_KnownType()
    {
        LedgerEntryType original = LedgerEntryType.RippleState;
        string json = JsonSerializer.Serialize(original, Options);
        LedgerEntryType deserialized = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(original, deserialized);
    }

    [TestMethod]
    public void RoundTrip_Unknown()
    {
        LedgerEntryType original = LedgerEntryType.Unknown;
        string json = JsonSerializer.Serialize(original, Options);
        LedgerEntryType deserialized = JsonSerializer.Deserialize<LedgerEntryType>(json, Options);
        Assert.AreEqual(original, deserialized);
    }

    [TestMethod]
    public void Read_InObject_UnknownType_ReturnsUnknown()
    {
        string json = @"{ ""LedgerEntryType"": ""FutureType"", ""Flags"": 0 }";
        TestEntry result = JsonSerializer.Deserialize<TestEntry>(json, Options);
        Assert.IsNotNull(result);
        Assert.AreEqual(LedgerEntryType.Unknown, result.LedgerEntryType);
    }

    [TestMethod]
    public void Read_InObject_KnownType_ReturnsCorrect()
    {
        string json = @"{ ""LedgerEntryType"": ""Offer"", ""Flags"": 0 }";
        TestEntry result = JsonSerializer.Deserialize<TestEntry>(json, Options);
        Assert.IsNotNull(result);
        Assert.AreEqual(LedgerEntryType.Offer, result.LedgerEntryType);
    }

    private class TestEntry
    {
        [System.Text.Json.Serialization.JsonConverter(typeof(LedgerEntryTypeConverter))]
        public LedgerEntryType LedgerEntryType { get; set; }
        public uint Flags { get; set; }
    }
}
