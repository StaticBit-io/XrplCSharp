using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Ledger;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class TransactionOrHashConverterTests
{
    [TestMethod]
    public void Read_StringHash_SetsTransactionHash()
    {
        string json = "\"E7A4A2D3E3B3D2D9E0C0A1B3C4D5E6F7E8F9A0B1C2D3E4F5A6B7C8D9E0F1A2\"";
        HashOrTransaction result = JsonConvert.DeserializeObject<HashOrTransaction>(json);
        Assert.IsNotNull(result);
        Assert.AreEqual("E7A4A2D3E3B3D2D9E0C0A1B3C4D5E6F7E8F9A0B1C2D3E4F5A6B7C8D9E0F1A2", result.TransactionHash);
        Assert.IsNull(result.Transaction);
    }

    [TestMethod]
    public void Read_Object_SetsTransaction()
    {
        string json = @"{
            ""hash"": ""ABC123"",
            ""validated"": true,
            ""tx_json"": {
                ""TransactionType"": ""Payment"",
                ""Account"": ""rTest"",
                ""Destination"": ""rDest"",
                ""Amount"": ""1000000""
            }
        }";
        HashOrTransaction result = JsonConvert.DeserializeObject<HashOrTransaction>(json);
        Assert.IsNotNull(result);
        Assert.IsNull(result.TransactionHash);
        Assert.IsNotNull(result.Transaction);
        Assert.AreEqual("ABC123", result.Transaction.Hash);
    }

    [TestMethod]
    public void Write_Hash_WritesString()
    {
        HashOrTransaction value = new HashOrTransaction
        {
            TransactionHash = "DEADBEEF"
        };
        string json = JsonConvert.SerializeObject(value);
        Assert.AreEqual("\"DEADBEEF\"", json);
    }

    [TestMethod]
    public void Write_Null_WritesNull()
    {
        HashOrTransaction value = null;
        string json = JsonConvert.SerializeObject(value);
        Assert.AreEqual("null", json);
    }
}
