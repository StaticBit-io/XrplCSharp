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
        Console.WriteLine("Encoded hex: " + encoded);
        Console.WriteLine("Encoded length: " + encoded.Length / 2 + " bytes");

        JsonNode decodedNode = XrplBinaryCodec.Decode(encoded);
        string decoded = decodedNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine("Decoded JSON: " + decoded);

        string principalDecoded = decodedNode["PrincipalRequested"]?.ToString();
        Console.WriteLine("PrincipalRequested decoded: " + principalDecoded);

        // Also encode for signing
        string forSigning = XrplBinaryCodec.EncodeForSigning(node);
        Console.WriteLine("ForSigning hex: " + forSigning);
    }
}
