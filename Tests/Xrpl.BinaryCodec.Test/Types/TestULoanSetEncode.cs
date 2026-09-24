using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.BinaryCodec;

namespace XrplTests.BinaryCodecLib.Types;

[TestClass]
public class TestULoanSetEncode
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
        // The codec writes a Number back the way rippled does: 10000000000000 reads as 1e13.
        string principalDecoded = decodedNode["PrincipalRequested"]?.ToString();
        Assert.AreEqual("1e13", principalDecoded, "PrincipalRequested round-trip mismatch");

        string forSigning = XrplBinaryCodec.EncodeForSigning(node);
        Assert.IsFalse(string.IsNullOrWhiteSpace(forSigning), "Signing payload should not be empty");
        Assert.AreNotEqual(encoded, forSigning, "Signing payload should differ from regular encoding");
    }
}
