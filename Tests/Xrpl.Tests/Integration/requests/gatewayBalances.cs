// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/integration/requests/gatewayBalances.ts

using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System.Linq;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;

namespace XrplTests.Xrpl.ClientLib.Integration;

[TestClass]
public class TestIGatewayBalances
{
    // private static int Timeout = 20;
    public TestContext TestContext { get; set; }

    static IXrplClient client;

    [ClassInitialize]
    public static async Task MyClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(TestNodeType.MainNet);

    }

    [ClassCleanup]
    public static void AfterAllTests()
    {
        client.Dispose();
    }

    [TestMethod]
    public async Task TestRequestMethod()
    {
        var index = new LedgerIndex(LedgerIndexType.Validated);
        var request = new GatewayBalancesRequest("rXPMxBeefHGxx2K7g5qmmWq3gFsgawkoa")
        {
            LedgerIndex = index,
            Strict = true,
        };
        var response = await client.GatewayBalances(request);
        Assert.IsNotNull(response);
    }

    // Прямые тесты десериализатора/модели на основе примеров ответов.
    [TestMethod]
    public void Deserialize_GatewayBalances_Example1()
    {
        const string json = @"{
  ""balances"": {
    ""rXPMxBeefHGxx2K7g5qmmWq3gFsgawkoa"": [
      {
        ""currency"": ""XPM"",
        ""value"": ""-3350974.464285175""
      }
    ]
  },
  ""assets"": {
    ""rrzQdKukvET4tE7ZmUSxJrAmXAquQnMFG"": [
      {
        ""currency"": ""LOW"",
        ""value"": ""523.1778853927886""
      }
    ]
  },
  ""account"": ""rLiooJRSKeiNfRJcDBUhu4rcjQjGLWqa4p"",
  ""ledger_index"": 103001160,
  ""ledger_hash"": ""0FD2A81794A0CEF35D71BA4A23DB8D8FB78F412D1FD9413A697EAA1F6E54CF8B""
}";

        var settings = new JsonSerializerSettings();
        settings.Converters.Add(new GatewayBalancesResponseConverter());

        var result = JsonConvert.DeserializeObject<GatewayBalancesResponse>(json, settings);

        Assert.IsNotNull(result);
        Assert.AreEqual(expected: "rLiooJRSKeiNfRJcDBUhu4rcjQjGLWqa4p", result.Account);
        Assert.AreEqual(expected: (uint)103001160, result.LedgerIndex);
        Assert.AreEqual(
            expected: "0FD2A81794A0CEF35D71BA4A23DB8D8FB78F412D1FD9413A697EAA1F6E54CF8B",
            result.LedgerHash);

        // Проверяем, что balances разобраны и каждому Currency присвоен Issuer
        var balance = result.Balances.FirstOrDefault(c =>
            c.Issuer == "rXPMxBeefHGxx2K7g5qmmWq3gFsgawkoa" && c.CurrencyCode == "XPM");
        Assert.IsNotNull(balance);
        Assert.AreEqual(expected: "-3350974.464285175", balance.Value);

        // Проверяем наличие хотя бы одной записи в assets и что Issuer установлен
        var asset = result.Assets.FirstOrDefault();
        Assert.IsNotNull(asset);
        Assert.IsFalse(string.IsNullOrWhiteSpace(asset.Issuer));
        Assert.IsFalse(string.IsNullOrWhiteSpace(asset.CurrencyCode));
    }

    [TestMethod]
    public void Deserialize_GatewayBalances_Example2_WithObligationsAndFrozen()
    {
        const string json = @"{
  ""obligations"": {
    ""XPM"": ""475773804.063732""
  },
  ""balances"": {
    ""rLiooJRSKeiNfRJcDBUhu4rcjQjGLWqa4p"": [
      {
        ""currency"": ""XPM"",
        ""value"": ""3350974.464285175""
      }
    ]
  },
  ""frozen_balances"": {
    ""rEYomQhJtaiVcREfRsHBfFHFistTVpabMz"": [
      {
        ""currency"": ""XPM"",
        ""value"": ""1077.4493969246""
      }
    ]
  },
  ""account"": ""rXPMxBeefHGxx2K7g5qmmWq3gFsgawkoa"",
  ""ledger_index"": 103001177,
  ""ledger_hash"": ""20ED11316FDC245F8E43F1D31FEF322B2AC483F6F3F41FD95B414C4ABA938AE2""
}";

        var settings = new JsonSerializerSettings();
        settings.Converters.Add(new GatewayBalancesResponseConverter());

        var result = JsonConvert.DeserializeObject<GatewayBalancesResponse>(json, settings);

        Assert.IsNotNull(result);
        Assert.AreEqual(expected: "rXPMxBeefHGxx2K7g5qmmWq3gFsgawkoa", result.Account);
        Assert.AreEqual(expected: (uint)103001177, result.LedgerIndex);
        Assert.AreEqual(
            expected: "20ED11316FDC245F8E43F1D31FEF322B2AC483F6F3F41FD95B414C4ABA938AE2",
            result.LedgerHash);

        // Obligations проверяем как список Currency с CurrencyCode == XPM и Issuer == account
        var obligation = result.Obligations.FirstOrDefault(c => c.CurrencyCode == "XPM" && c.Issuer == result.Account);
        Assert.IsNotNull(obligation);
        Assert.AreEqual(expected: "475773804.063732", obligation.Value);

        // Балансы
        var balance = result.Balances.FirstOrDefault(c =>
            c.Issuer == "rLiooJRSKeiNfRJcDBUhu4rcjQjGLWqa4p" && c.CurrencyCode == "XPM");
        Assert.IsNotNull(balance);
        Assert.AreEqual(expected: "3350974.464285175", balance.Value);

        // Замороженные балансы
        var frozen = result.FrozenBalances.FirstOrDefault(c =>
            c.Issuer == "rEYomQhJtaiVcREfRsHBfFHFistTVpabMz" && c.CurrencyCode == "XPM");
        Assert.IsNotNull(frozen);
        Assert.AreEqual(expected: "1077.4493969246", frozen.Value);
    }
}