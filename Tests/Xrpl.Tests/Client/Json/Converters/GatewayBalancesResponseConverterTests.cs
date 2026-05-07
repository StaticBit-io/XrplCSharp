using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class TestUGatewayBalancesResponseConverter
{
    [TestMethod]
    public void Read_FullResponse_ParsesAllFields()
    {
        string json = @"{
            ""account"": ""rMwjYedjc7qqtKYVLiAccJSmCwih4LnE2q"",
            ""ledger_hash"": ""ABCDEF"",
            ""ledger_index"": 12345,
            ""validated"": true,
            ""obligations"": {
                ""USD"": ""100.50"",
                ""EUR"": ""200.25""
            },
            ""balances"": {
                ""rKm4uWpg9tfwbVSeATv4KxDe6mpE9yPkgJ"": [
                    {""currency"": ""USD"", ""value"": ""50""},
                    {""currency"": ""EUR"", ""value"": ""75""}
                ]
            },
            ""assets"": {},
            ""frozen_balances"": {}
        }";
        GatewayBalancesResponse result = JsonSerializer.Deserialize<GatewayBalancesResponse>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result);
        Assert.AreEqual("rMwjYedjc7qqtKYVLiAccJSmCwih4LnE2q", result.Account);
        Assert.AreEqual("ABCDEF", result.LedgerHash);
        Assert.AreEqual(12345U, result.LedgerIndex);
        Assert.IsTrue(result.Validated.Value);

        Assert.AreEqual(2, result.Obligations.Count);
        Assert.AreEqual("USD", result.Obligations[0].CurrencyCode);
        Assert.AreEqual("100.50", result.Obligations[0].Value);

        Assert.AreEqual(2, result.Balances.Count);
        Assert.IsTrue(result.Balances.All(b => b.Issuer == "rKm4uWpg9tfwbVSeATv4KxDe6mpE9yPkgJ"));
    }

    [TestMethod]
    public void Read_EmptyResponse_ReturnsEmptyLists()
    {
        string json = @"{
            ""account"": ""rTest""
        }";
        GatewayBalancesResponse result = JsonSerializer.Deserialize<GatewayBalancesResponse>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result);
        Assert.AreEqual("rTest", result.Account);
        Assert.AreEqual(0, result.Assets.Count);
        Assert.AreEqual(0, result.Balances.Count);
        Assert.AreEqual(0, result.Obligations.Count);
    }

    [TestMethod]
    public void Write_FullResponse_SerializesCorrectly()
    {
        GatewayBalancesResponse response = new GatewayBalancesResponse
        {
            Account = "rTest",
            LedgerHash = "HASH123",
            LedgerIndex = 999,
            Validated = true,
            Assets = new List<Currency>(),
            Balances = new List<Currency>(),
            FrozenBalances = new List<Currency>(),
            Obligations = new List<Currency>
            {
                new Currency { CurrencyCode = "USD", Value = "100", Issuer = "rTest" }
            }
        };
        string json = JsonSerializer.Serialize(response, XrplJsonOptions.Default);
        Assert.IsTrue(json.Contains("\"account\""));
        Assert.IsTrue(json.Contains("rTest"));
        Assert.IsTrue(json.Contains("obligations"));
        Assert.IsTrue(json.Contains("USD"));
    }

    [TestMethod]
    public void RoundTrip_PreservesValues()
    {
        string json = @"{
            ""account"": ""rIssuer"",
            ""obligations"": {
                ""USD"": ""500""
            },
            ""balances"": {
                ""rHot1"": [
                    {""currency"": ""USD"", ""value"": ""250""}
                ]
            },
            ""ledger_hash"": ""HASH"",
            ""ledger_index"": 100,
            ""validated"": true
        }";
        GatewayBalancesResponse parsed = JsonSerializer.Deserialize<GatewayBalancesResponse>(json, XrplJsonOptions.Default);
        string reserialized = JsonSerializer.Serialize(parsed, XrplJsonOptions.Default);
        GatewayBalancesResponse reparsed = JsonSerializer.Deserialize<GatewayBalancesResponse>(reserialized, XrplJsonOptions.Default);

        Assert.AreEqual(parsed.Account, reparsed.Account);
        Assert.AreEqual(parsed.Obligations.Count, reparsed.Obligations.Count);
        Assert.AreEqual(parsed.Balances.Count, reparsed.Balances.Count);
    }
}
