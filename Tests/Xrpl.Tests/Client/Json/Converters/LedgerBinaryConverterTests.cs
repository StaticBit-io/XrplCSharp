using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Ledger;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class TestULedgerBinaryConverter
{
    private class Model
    {
        [JsonConverter(typeof(LedgerBinaryConverter))]
        public IBaseLedgerEntity Ledger { get; set; }
    }

    [TestMethod]
    public void Read_WithLedgerData_ReturnsLedgerBinaryEntity()
    {
        string json = @"{""Ledger"": {
            ""closed"": true,
            ""ledger_data"": ""ABCD1234""
        }}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result.Ledger);
        Assert.IsInstanceOfType(result.Ledger, typeof(LedgerBinaryEntity));
        LedgerBinaryEntity binary = (LedgerBinaryEntity)result.Ledger;
        Assert.AreEqual("ABCD1234", binary.LedgerData);
        Assert.IsTrue(binary.Closed);
    }

    [TestMethod]
    public void Read_WithoutLedgerData_ReturnsLedgerEntity()
    {
        string json = @"{""Ledger"": {
            ""closed"": false
        }}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result.Ledger);
        Assert.IsInstanceOfType(result.Ledger, typeof(LedgerEntity));
        Assert.IsFalse(result.Ledger.Closed);
    }
}
