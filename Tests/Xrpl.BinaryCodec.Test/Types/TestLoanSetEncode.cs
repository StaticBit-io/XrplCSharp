using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.BinaryCodec;

namespace XrplTests.BinaryCodecLib.Types;

[TestClass]
public class TestLoanSetEncode
{
    [TestMethod]
    public void TestEncodeDecode_LoanSet()
    {
        string json = @"{
            ""TransactionType"": ""LoanSet"",
            ""Account"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"",
            ""LoanBrokerID"": ""0000000000000000000000000000000000000000000000000000000000000001"",
            ""Counterparty"": ""rnUy2SHTrB9DubsPmkJZUXTf5FcNDGrYEA"",
            ""PrincipalRequested"": ""10000000000000"",
            ""Fee"": ""12"",
            ""Sequence"": 1,
            ""SigningPubKey"": """"
        }";

        JsonNode node = JsonNode.Parse(json);

        string encoded = XrplBinaryCodec.Encode(node);
        Assert.IsFalse(string.IsNullOrWhiteSpace(encoded), "Encoded hex should not be empty");

        JsonNode decodedNode = XrplBinaryCodec.Decode(encoded);
        Assert.IsNotNull(decodedNode, "Decoded node should not be null");
        Assert.AreEqual("LoanSet", decodedNode["TransactionType"]?.ToString());
        Assert.AreEqual("10000000000000", decodedNode["PrincipalRequested"]?.ToString());

        string principalDecoded = decodedNode["PrincipalRequested"]?.ToString();
        Assert.AreEqual("10000000000000", principalDecoded, "PrincipalRequested round-trip mismatch");

        string forSigning = XrplBinaryCodec.EncodeForSigning(node);
        Assert.IsFalse(string.IsNullOrWhiteSpace(forSigning), "Signing payload should not be empty");
        Assert.AreNotEqual(encoded, forSigning, "Signing payload should differ from regular encoding");
    }
}
