using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Transactions;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class TestUMetaBinaryConverter
{
    private class Model
    {
        [JsonConverter(typeof(MetaBinaryConverter))]
        public Meta MetaData { get; set; }
    }

    [TestMethod]
    public void Read_Null_ReturnsNull()
    {
        string json = "{\"MetaData\": null}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNull(result.MetaData);
    }

    [TestMethod]
    public void Read_JsonObject_ReturnsMeta()
    {
        string json = @"{""MetaData"": {
            ""TransactionIndex"": 5,
            ""AffectedNodes"": []
        }}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result.MetaData);
        Assert.AreEqual(5, result.MetaData.TransactionIndex);
    }

    [TestMethod]
    public void CanConvert_ReturnsTrue()
    {
        MetaBinaryConverter converter = new MetaBinaryConverter();
        Assert.IsTrue(converter.CanConvert(typeof(Meta)));
        Assert.IsFalse(converter.CanConvert(typeof(string)));
    }
}
