using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class GenericStringConverterTests
{
    private class SimpleDto
    {
        public string Name { get; set; }
        public int Age { get; set; }

        public override string ToString() => JsonSerializer.Serialize(this, XrplJsonOptions.Default);
    }

    private class Model
    {
        [JsonConverter(typeof(GenericStringConverter<SimpleDto>))]
        public SimpleDto Data { get; set; }
    }

    [TestMethod]
    public void Read_JsonObject_DeserializesDirectly()
    {
        string json = "{\"Data\": {\"Name\": \"Alice\", \"Age\": 30}}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result.Data);
        Assert.AreEqual("Alice", result.Data.Name);
        Assert.AreEqual(30, result.Data.Age);
    }

    [TestMethod]
    public void Read_JsonString_DeserializesFromString()
    {
        string innerJson = JsonSerializer.Serialize(new SimpleDto { Name = "Bob", Age = 25 }, XrplJsonOptions.Default);
        string json = $"{{\"Data\": {JsonSerializer.Serialize(innerJson, XrplJsonOptions.Default)}}}";
        Model result = JsonSerializer.Deserialize<Model>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result.Data);
        Assert.AreEqual("Bob", result.Data.Name);
        Assert.AreEqual(25, result.Data.Age);
    }

    [TestMethod]
    public void Write_Object_WritesToString()
    {
        Model model = new Model
        {
            Data = new SimpleDto { Name = "Charlie", Age = 35 }
        };
        string json = JsonSerializer.Serialize(model, XrplJsonOptions.Default);
        Assert.IsNotNull(json);
        Assert.IsTrue(json.Contains("Charlie"));
    }
}
