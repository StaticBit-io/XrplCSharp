using System.Linq;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Json;
using Xrpl.Models.Methods;

namespace Xrpl.Tests.Models.Tests
{
    /// <summary>
    /// Deserialization tests for gateway_balances responses, based on captured
    /// mainnet payloads. Moved out of the integration class: they exercise only
    /// JsonSerializer + XrplJsonOptions and need no node.
    /// </summary>
    [TestClass]
    public class TestUGatewayBalances
    {
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

            JsonSerializerOptions options = XrplJsonOptions.Default;

            var result = JsonSerializer.Deserialize<GatewayBalancesResponse>(json, options);

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

            JsonSerializerOptions options = XrplJsonOptions.Default;

            var result = JsonSerializer.Deserialize<GatewayBalancesResponse>(json, options);

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
}
