using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class LedgerIndexConverterTests
{
    private class Model
    {
        [JsonConverter(typeof(LedgerIndexConverter))]
        public LedgerIndex Ledger { get; set; }
    }

    [TestMethod]
    public void Write_NumericIndex_WritesNumber()
    {
        Model model = new Model { Ledger = new LedgerIndex(12345) };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("12345"));
        Assert.IsFalse(json.Contains("\"12345\""));
    }

    [TestMethod]
    public void Write_Validated_WritesString()
    {
        Model model = new Model { Ledger = new LedgerIndex(LedgerIndexType.Validated) };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("\"validated\""));
    }

    [TestMethod]
    public void Write_Current_WritesString()
    {
        Model model = new Model { Ledger = new LedgerIndex(LedgerIndexType.Current) };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("\"current\""));
    }

    [TestMethod]
    public void Write_Closed_WritesString()
    {
        Model model = new Model { Ledger = new LedgerIndex(LedgerIndexType.Closed) };
        string json = JsonConvert.SerializeObject(model);
        Assert.IsTrue(json.Contains("\"closed\""));
    }

    [TestMethod]
    public void Read_ThrowsNotImplementedException()
    {
        string json = "{\"Ledger\": 12345}";
        bool threw = false;
        try { JsonConvert.DeserializeObject<Model>(json); }
        catch (Exception ex) when (ex is NotImplementedException || ex.InnerException is NotImplementedException) { threw = true; }
        Assert.IsTrue(threw, "Expected NotImplementedException from ReadJson");
    }
}
