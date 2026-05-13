using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.BinaryCodec;

namespace XrplTests.BinaryCodecLib.Types;

[TestClass]
public class TestIssueMpt
{
    [TestMethod]
    [TestCategory("TestU")]
    public void TestEncodeDecode_MptIssue_VaultCreate()
    {
        string json = @"{
            ""Account"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"",
            ""TransactionType"": ""VaultCreate"",
            ""Asset"": { ""mpt_issuance_id"": ""00000001A407AF5856CCA3379B1EC94E1D2C5B99C1BE89C2"" },
            ""Fee"": ""12"",
            ""Sequence"": 1
        }";

        JsonNode node = JsonNode.Parse(json);
        string encoded = XrplBinaryCodec.Encode(node);
        Assert.IsFalse(string.IsNullOrWhiteSpace(encoded), "Encoded hex should not be empty");

        JsonNode decoded = XrplBinaryCodec.Decode(encoded);
        Assert.IsNotNull(decoded, "Decoded node should not be null");
        Assert.AreEqual("VaultCreate", decoded["TransactionType"]?.ToString());

        // Verify MPT Issue round-trips correctly
        var asset = decoded["Asset"];
        Assert.IsNotNull(asset, "Asset should be present");
        string mptId = asset["mpt_issuance_id"]?.ToString();
        Assert.AreEqual("00000001A407AF5856CCA3379B1EC94E1D2C5B99C1BE89C2", mptId,
            "MPT issuance ID should round-trip correctly");
    }

    [TestMethod]
    [TestCategory("TestU")]
    public void TestMptIssue_BinaryMatchesRippled()
    {
        // Verify our binary output matches what rippled produces for MPT Issue
        // Reference: rippled 3.1.3 tx_blob for VaultCreate with MPTID seq=1
        string json = @"{
            ""Account"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"",
            ""TransactionType"": ""VaultCreate"",
            ""Asset"": { ""mpt_issuance_id"": ""00000001A407AF5856CCA3379B1EC94E1D2C5B99C1BE89C2"" },
            ""Fee"": ""12"",
            ""Sequence"": 1
        }";

        JsonNode node = JsonNode.Parse(json);
        string encoded = XrplBinaryCodec.Encode(node);

        // Asset field should contain: account(20) + noAccount marker(20) + seq LE(4) = 44 bytes
        // Field header 0318 followed by:
        //   A407AF5856CCA3379B1EC94E1D2C5B99C1BE89C2 (account from MPTID)
        //   0000000000000000000000000000000000000001 (noAccount = uint160{1})
        //   01000000 (sequence=1 reversed per rippled memcpy+add32 pattern)
        string expectedAssetHex =
            "A407AF5856CCA3379B1EC94E1D2C5B99C1BE89C2" +
            "0000000000000000000000000000000000000001" +
            "01000000";

        Assert.IsTrue(encoded.Contains("0318" + expectedAssetHex),
            $"Binary should contain the MPT Asset field matching rippled format. Got: {encoded}");
    }

    [TestMethod]
    [TestCategory("TestU")]
    public void TestEncodeDecode_StandardIssue_VaultCreate()
    {
        // Verify standard IOU Issue still works
        string json = @"{
            ""Account"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"",
            ""TransactionType"": ""VaultCreate"",
            ""Asset"": { ""currency"": ""USD"", ""issuer"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"" },
            ""Fee"": ""12"",
            ""Sequence"": 1
        }";

        JsonNode node = JsonNode.Parse(json);
        string encoded = XrplBinaryCodec.Encode(node);
        Assert.IsFalse(string.IsNullOrWhiteSpace(encoded));

        JsonNode decoded = XrplBinaryCodec.Decode(encoded);
        Assert.AreEqual("VaultCreate", decoded["TransactionType"]?.ToString());
        Assert.AreEqual("USD", decoded["Asset"]?["currency"]?.ToString());
    }
}
