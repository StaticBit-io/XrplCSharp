using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System.Collections.Generic;

using Xrpl.Client.Json.Converters;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class StringOrArrayConverterTests
{
    private class Model
    {
        [JsonConverter(typeof(StringOrArrayConverter))]
        public object Value { get; set; }
    }

    [TestMethod]
    public void Read_String_ReturnsString()
    {
        string json = "{\"Value\": \"hello\"}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.AreEqual("hello", result.Value);
    }

    [TestMethod]
    public void Read_Null_ReturnsNull()
    {
        string json = "{\"Value\": null}";
        Model result = JsonConvert.DeserializeObject<Model>(json);
        Assert.IsNull(result.Value);
    }

    [TestMethod]
    public void Write_String_WritesStringValue()
    {
        Model model = new Model { Value = "test" };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("\"test\""));
    }

    [TestMethod]
    public void Write_List_WritesArray()
    {
        Model model = new Model { Value = new List<string> { "a", "b", "c" } };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("["));
        Assert.IsTrue(json.Contains("\"a\""));
        Assert.IsTrue(json.Contains("\"b\""));
        Assert.IsTrue(json.Contains("\"c\""));
    }

    [TestMethod]
    public void Write_StringArray_WritesArray()
    {
        Model model = new Model { Value = new string[] { "x", "y" } };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("["));
        Assert.IsTrue(json.Contains("\"x\""));
    }

    [TestMethod]
    public void Write_Null_WritesNull()
    {
        Model model = new Model { Value = null };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("null"));
    }
}
