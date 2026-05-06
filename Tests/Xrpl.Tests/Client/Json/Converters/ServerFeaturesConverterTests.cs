using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Methods;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class TestUServerFeaturesConverter
{
    [TestMethod]
    public void Read_Format1_WithFeaturesObject()
    {
        string json = @"{
            ""ledger_hash"": ""ABC123"",
            ""ledger_index"": 1000,
            ""validated"": true,
            ""features"": {
                ""42426C4D4F1009EE67080A9B7965B44656D7714D104A72F9B4369F97ABF044EE"": {
                    ""name"": ""Checks"",
                    ""enabled"": true,
                    ""supported"": true
                },
                ""DC9CA96AEA1DCF83E527D1AFC916EFAF5D27388ECA4060A88817C680FF3BC52F"": {
                    ""name"": ""ExpandedSignerList"",
                    ""enabled"": false,
                    ""supported"": true,
                    ""count"": 25,
                    ""threshold"": 28
                }
            }
        }";
        ServerFeatures result = JsonSerializer.Deserialize<ServerFeatures>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result);
        Assert.AreEqual("ABC123", result.LedgerHash);
        Assert.AreEqual(1000UL, result.LedgerIndex);
        Assert.IsTrue(result.Validated);
        Assert.AreEqual(2, result.Features.Count);
        Assert.IsTrue(result.Features.ContainsKey("42426C4D4F1009EE67080A9B7965B44656D7714D104A72F9B4369F97ABF044EE"));

        FeatureInfo checks = result.Features["42426C4D4F1009EE67080A9B7965B44656D7714D104A72F9B4369F97ABF044EE"];
        Assert.AreEqual("Checks", checks.Name);
        Assert.IsTrue(checks.Enabled);
        Assert.IsTrue(checks.Supported);
    }

    [TestMethod]
    public void Read_Format2_FlatHashProperties()
    {
        string json = @"{
            ""ledger_hash"": ""DEF456"",
            ""ledger_index"": 2000,
            ""validated"": false,
            ""42426C4D4F1009EE67080A9B7965B44656D7714D104A72F9B4369F97ABF044EE"": {
                ""name"": ""Checks"",
                ""enabled"": true,
                ""supported"": true
            }
        }";
        ServerFeatures result = JsonSerializer.Deserialize<ServerFeatures>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result);
        Assert.AreEqual("DEF456", result.LedgerHash);
        Assert.AreEqual(2000UL, result.LedgerIndex);
        Assert.IsFalse(result.Validated);
        Assert.AreEqual(1, result.Features.Count);
        Assert.IsTrue(result.Features.ContainsKey("42426C4D4F1009EE67080A9B7965B44656D7714D104A72F9B4369F97ABF044EE"));
    }

    [TestMethod]
    public void Read_EmptyFeatures_ReturnsEmptyDictionary()
    {
        string json = @"{
            ""ledger_hash"": ""GHI789"",
            ""ledger_index"": 3000,
            ""validated"": true,
            ""features"": {}
        }";
        ServerFeatures result = JsonSerializer.Deserialize<ServerFeatures>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Features.Count);
    }

    [TestMethod]
    public void Read_VetoedAsBoolean()
    {
        string json = @"{
            ""features"": {
                ""ABC123"": {
                    ""name"": ""TestFeature"",
                    ""enabled"": false,
                    ""supported"": true,
                    ""vetoed"": true
                }
            }
        }";
        ServerFeatures result = JsonSerializer.Deserialize<ServerFeatures>(json, XrplJsonOptions.Default);
        FeatureInfo feature = result.Features["ABC123"];
        Assert.IsTrue(feature.IsVetoed);
        Assert.IsNull(feature.VetoedReason);
    }

    [TestMethod]
    public void Read_VetoedAsString()
    {
        string json = @"{
            ""features"": {
                ""ABC123"": {
                    ""name"": ""TestFeature"",
                    ""enabled"": false,
                    ""supported"": true,
                    ""vetoed"": ""Obsolete""
                }
            }
        }";
        ServerFeatures result = JsonSerializer.Deserialize<ServerFeatures>(json, XrplJsonOptions.Default);
        FeatureInfo feature = result.Features["ABC123"];
        Assert.IsTrue(feature.IsVetoed);
        Assert.AreEqual("Obsolete", feature.VetoedReason);
    }
}
