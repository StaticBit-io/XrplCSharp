using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System;

using Xrpl.Client.Json.Converters;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class GenericStringConverterTests
{
    private class SimpleDto
    {
        public string Name { get; set; }
        public int Age { get; set; }

        public override string ToString() => JsonConvert.SerializeObject(this);
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
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.IsNotNull(result.Data);
        Assert.AreEqual("Alice", result.Data.Name);
        Assert.AreEqual(30, result.Data.Age);
    }

    [TestMethod]
    public void Read_JsonString_DeserializesFromString()
    {
        string innerJson = JsonConvert.SerializeObject(new SimpleDto { Name = "Bob", Age = 25 });
        string json = $"{{\"Data\": {JsonConvert.SerializeObject(innerJson)}}}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
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
        string json = JsonConvert.SerializeObject(model);
        Assert.IsNotNull(json);
        Assert.IsTrue(json.Contains("Charlie"));
    }
}
