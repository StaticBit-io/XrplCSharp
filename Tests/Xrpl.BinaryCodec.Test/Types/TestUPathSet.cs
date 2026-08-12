using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.BinaryCodec;

namespace XrplTests.BinaryCodecLib.Types;

/// <summary>
/// PathSet serialization tests.
/// Layout mirrors rippled STPathSet::add(): type byte, then account(20), MPTokenIssuanceID(24),
/// currency(20) and issuer(20) for whichever bits the type byte carries.
/// Type bits: 0x01 account, 0x10 currency, 0x20 issuer, 0x40 MPT (rippled 3.2.0+, MPTokensV2).
/// </summary>
[TestClass]
public class TestUPathSet
{
    private const string PathsFieldHeader = "0112";
    private const string PathSetEnd = "00";

    // rBitcoiNXev8VoVxV7pwoQx1sSfonVP9i3
    private const string IssuerAccountHex = "7720BA5CE66725906C2D74C7E8ADB1557556691A";
    private const string CurrencyHex = "4249547800000000000000000000000000000000";
    private const string MptIssuanceId = "00000001A407AF5856CCA3379B1EC94E1D2C5B99C1BE89C2";

    private static string PaymentWithPaths(string pathStepsJson) => @"{
        ""Account"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"",
        ""TransactionType"": ""Payment"",
        ""Destination"": ""rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn"",
        ""Amount"": { ""currency"": ""4249547800000000000000000000000000000000"", ""issuer"": ""rBitcoiNXev8VoVxV7pwoQx1sSfonVP9i3"", ""value"": ""1"" },
        ""SendMax"": ""100000000"",
        ""Fee"": ""12"",
        ""Sequence"": 1,
        ""Paths"": [[" + pathStepsJson + @"]]
    }";

    private static string Encode(string pathStepsJson) =>
        XrplBinaryCodec.Encode(JsonNode.Parse(PaymentWithPaths(pathStepsJson)));

    [TestMethod]
    [TestCategory("TestU")]
    public void TestUPathSetCurrencyIssuerHopMatchesRippledLayout()
    {
        // Same shape as mainnet tx 1D813B78FC55ABF9054AEBD2AF9DD7C90361F9985B7897E8E9A592D63BF0CC43
        string encoded = Encode(@"{ ""currency"": ""4249547800000000000000000000000000000000"", ""issuer"": ""rBitcoiNXev8VoVxV7pwoQx1sSfonVP9i3"", ""type"": 48 }");

        string expected = PathsFieldHeader + "30" + CurrencyHex + IssuerAccountHex + PathSetEnd;
        StringAssert.Contains(encoded, expected, $"PathSet bytes should match rippled layout. Got: {encoded}");
    }

    [TestMethod]
    [TestCategory("TestU")]
    public void TestUPathSetHopTypeIsSynthesizedNotReadFromJson()
    {
        // rippled derives the type byte from the fields present and ignores the JSON "type"/"type_hex"
        // keys on input, so neither a missing nor a wrong value may change the produced blob.
        string withCorrectType = Encode(@"{ ""currency"": ""4249547800000000000000000000000000000000"", ""issuer"": ""rBitcoiNXev8VoVxV7pwoQx1sSfonVP9i3"", ""type"": 48 }");
        string withoutType = Encode(@"{ ""currency"": ""4249547800000000000000000000000000000000"", ""issuer"": ""rBitcoiNXev8VoVxV7pwoQx1sSfonVP9i3"" }");
        string withWrongType = Encode(@"{ ""currency"": ""4249547800000000000000000000000000000000"", ""issuer"": ""rBitcoiNXev8VoVxV7pwoQx1sSfonVP9i3"", ""type"": 1, ""type_hex"": ""0000000000000001"" }");

        Assert.AreEqual(withCorrectType, withoutType, "Removing the type key must not change the blob");
        Assert.AreEqual(withCorrectType, withWrongType, "A wrong type/type_hex must not change the blob");
    }

    [TestMethod]
    [TestCategory("TestU")]
    public void TestUPathSetMptHopMatchesRippledLayout()
    {
        string encoded = Encode(@"{ ""mpt_issuance_id"": ""00000001A407AF5856CCA3379B1EC94E1D2C5B99C1BE89C2"" }");

        string expected = PathsFieldHeader + "40" + MptIssuanceId + PathSetEnd;
        StringAssert.Contains(encoded, expected, $"MPT hop should serialize as 0x40 + 24-byte MPTokenIssuanceID. Got: {encoded}");
    }

    [TestMethod]
    [TestCategory("TestU")]
    public void TestUPathSetMptWithIssuerHopMatchesRippledLayout()
    {
        string encoded = Encode(@"{ ""mpt_issuance_id"": ""00000001A407AF5856CCA3379B1EC94E1D2C5B99C1BE89C2"", ""issuer"": ""rBitcoiNXev8VoVxV7pwoQx1sSfonVP9i3"" }");

        // rippled STPathSet::add() writes MPT before issuer
        string expected = PathsFieldHeader + "60" + MptIssuanceId + IssuerAccountHex + PathSetEnd;
        StringAssert.Contains(encoded, expected, $"MPT+issuer hop should serialize as 0x60 + MPTID + issuer. Got: {encoded}");
    }

    [TestMethod]
    [TestCategory("TestU")]
    public void TestUPathSetMptHopRoundTrips()
    {
        string encoded = Encode(@"{ ""mpt_issuance_id"": ""00000001A407AF5856CCA3379B1EC94E1D2C5B99C1BE89C2"", ""issuer"": ""rBitcoiNXev8VoVxV7pwoQx1sSfonVP9i3"" }");

        JsonNode decoded = XrplBinaryCodec.Decode(encoded);
        JsonNode hop = decoded["Paths"][0][0];

        Assert.AreEqual(MptIssuanceId, hop["mpt_issuance_id"]?.ToString(), "MPTokenIssuanceID should round-trip");
        Assert.AreEqual("rBitcoiNXev8VoVxV7pwoQx1sSfonVP9i3", hop["issuer"]?.ToString(), "Issuer should round-trip");
        Assert.AreEqual(0x60, hop["type"]?.GetValue<int>(), "Decoded hop should report the synthesized type byte");
        Assert.IsNull(hop["currency"], "MPT hop must not carry a currency");
    }

    [TestMethod]
    [TestCategory("TestU")]
    public void TestUPathSetCurrencyAndMptTogetherThrows()
    {
        Assert.ThrowsExactly<InvalidJsonException>(
            () => Encode(@"{ ""currency"": ""4249547800000000000000000000000000000000"", ""mpt_issuance_id"": ""00000001A407AF5856CCA3379B1EC94E1D2C5B99C1BE89C2"" }"),
            "A path step holding both currency and mpt_issuance_id must be rejected, as rippled does");
    }

    [TestMethod]
    [TestCategory("TestU")]
    public void TestUPathSetNonStringMptIssuanceIdThrows()
    {
        Assert.ThrowsExactly<InvalidJsonException>(
            () => Encode(@"{ ""mpt_issuance_id"": 42 }"),
            "A non-string mpt_issuance_id must be reported as invalid JSON, like Amount and Issue do");
    }

    [TestMethod]
    [TestCategory("TestU")]
    public void TestUPathSetEmptyPathThrowsOnDecode()
    {
        string encoded = Encode(@"{ ""currency"": ""4249547800000000000000000000000000000000"", ""issuer"": ""rBitcoiNXev8VoVxV7pwoQx1sSfonVP9i3"" }");

        // insert a leading path separator, which makes the first path empty
        string corrupted = encoded.Replace(PathsFieldHeader + "30", PathsFieldHeader + "FF30");
        Assert.AreNotEqual(encoded, corrupted, "Test setup should have inserted the separator");

        Assert.ThrowsExactly<BinaryCodecException>(
            () => XrplBinaryCodec.Decode(corrupted),
            "An empty path must be rejected, as rippled does");
    }

    [TestMethod]
    [TestCategory("TestU")]
    public void TestUPathSetUnknownTypeBitsThrowOnDecode()
    {
        string encoded = Encode(@"{ ""currency"": ""4249547800000000000000000000000000000000"", ""issuer"": ""rBitcoiNXev8VoVxV7pwoQx1sSfonVP9i3"" }");

        // flip the hop type byte to a value carrying a bit outside TypeAll (0x71)
        string corrupted = encoded.Replace(PathsFieldHeader + "30" + CurrencyHex, PathsFieldHeader + "02" + CurrencyHex);
        Assert.AreNotEqual(encoded, corrupted, "Test setup should have patched the hop type byte");

        Assert.ThrowsExactly<BinaryCodecException>(
            () => XrplBinaryCodec.Decode(corrupted),
            "A hop type byte with unknown bits must be rejected, as rippled does");
    }
}
