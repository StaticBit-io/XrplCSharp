using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;

using XrplTests;

namespace Xrpl.Tests.ClientLib
{
    /// <summary>
    /// The <c>get_aggregate_price</c> request and response, against rippled's
    /// <c>GetAggregatePrice.cpp</c> and the xrpl.org reference.
    /// </summary>
    [TestClass]
    public class TestUGetAggregatePrice
    {
        private const string OracleOwner = "rNZ9m6AP9K7z3EVg6GhPMx36V4QmZKeWds";

        private static string Serialize(GetAggregatePriceRequest request) =>
            JsonSerializer.Serialize(request, XrplJsonOptions.Default);

        [TestMethod]
        public void TestURequestAsksForWhatRippledReads()
        {
            GetAggregatePriceRequest request = new GetAggregatePriceRequest(
                "XRP",
                "USD",
                new[] { new OracleIdentifier(OracleOwner, 34), new OracleIdentifier("rMVKq8zrVsJZQFEiTARyC6WfZznhhLMcNi", 100) })
            {
                Trim = 20,
                TimeThreshold = 60,
                LedgerIndex = new LedgerIndex(LedgerIndexType.Current),
            };

            string json = Serialize(request);

            StringAssert.Contains(json, "\"command\":\"get_aggregate_price\"");
            StringAssert.Contains(json, "\"base_asset\":\"XRP\"");
            StringAssert.Contains(json, "\"quote_asset\":\"USD\"");
            StringAssert.Contains(json, "\"oracles\":[{\"account\":\"" + OracleOwner + "\",\"oracle_document_id\":34},");
            StringAssert.Contains(json, "\"oracle_document_id\":100");
            StringAssert.Contains(json, "\"trim\":20");
            StringAssert.Contains(json, "\"ledger_index\":\"current\"");
        }

        /// <summary>
        /// rippled reads <c>time_threshold</c>; the xrpl.org reference and xrpl.js send <c>trim_threshold</c>,
        /// which the node ignores, so the filter silently does nothing.
        /// </summary>
        [TestMethod]
        public void TestUTimeThresholdUsesTheNameRippledReads()
        {
            GetAggregatePriceRequest request = new GetAggregatePriceRequest("XRP", "USD", new[] { new OracleIdentifier(OracleOwner, 1) })
            {
                TimeThreshold = 60,
            };

            string json = Serialize(request);

            StringAssert.Contains(json, "\"time_threshold\":60");
            Assert.DoesNotContain("trim_threshold", json);
        }

        /// <summary>
        /// rippled rejects a <c>trim</c> of zero with <c>invalidParams</c>, so an unset trim must not be sent.
        /// </summary>
        [TestMethod]
        public void TestUOptionalParametersAreLeftOutWhenUnset()
        {
            string json = Serialize(new GetAggregatePriceRequest("XRP", "USD", new[] { new OracleIdentifier(OracleOwner, 1) }));

            Assert.DoesNotContain("\"trim\"", json);
            Assert.DoesNotContain("\"time_threshold\"", json);
        }

        /// <summary>
        /// rippled matches the pair by comparing the request string with <c>to_string(Currency)</c> of the
        /// stored asset, so every spelling of a currency must reach the node in that display form.
        /// </summary>
        [TestMethod]
        [DataRow("XRP", "XRP", DisplayName = "the system currency")]
        [DataRow("USD", "USD", DisplayName = "an ISO-style code")]
        [DataRow("RLUSD", "524C555344000000000000000000000000000000", DisplayName = "a longer name, encoded as OracleSet encodes it")]
        [DataRow("524c555344000000000000000000000000000000", "524C555344000000000000000000000000000000", DisplayName = "lowercase hex")]
        [DataRow("0000000000000000000000005553440000000000", "USD", DisplayName = "hex holding an ISO-style code")]
        [DataRow("0000000000000000000000000000000000000000", "XRP", DisplayName = "the zero currency in hex")]
        [DataRow("0000000000000000000000005852500000000000", "0000000000000000000000005852500000000000", DisplayName = "XRP in ISO position is not displayed as XRP")]
        [DataRow("0000000000000000000000000000000000000001", "1", DisplayName = "noCurrency")]
        [DataRow("0158415500000000C1F76FF6ECB0BAC600000000", "0158415500000000C1F76FF6ECB0BAC600000000", DisplayName = "a nonstandard code")]
        public void TestUAssetIsWrittenInTheNodesDisplayForm(string given, string expected)
        {
            Assert.AreEqual(expected, AggregatePriceAssetConverter.ToDisplayForm(given));

            string json = Serialize(new GetAggregatePriceRequest(given, given, new[] { new OracleIdentifier(OracleOwner, 1) }));
            StringAssert.Contains(json, "\"base_asset\":\"" + expected + "\"");
            StringAssert.Contains(json, "\"quote_asset\":\"" + expected + "\"");
        }

        [TestMethod]
        [DataRow("€UR")]
        [DataRow("ThisNameIsLongerThanTwentyBytes")]
        public void TestUAssetTheLedgerCannotHoldIsRefused(string asset)
        {
            GetAggregatePriceRequest request = new GetAggregatePriceRequest(asset, "USD", new[] { new OracleIdentifier(OracleOwner, 1) });

            Helper.ThrowsException<JsonException>(() => Serialize(request));
        }

        /// <summary>
        /// The answer to a request on the open ledger with a trim, as xrpl.org documents it.
        /// </summary>
        [TestMethod]
        public void TestUResponseReadsEveryFieldTheNodeSends()
        {
            const string body = """
                {
                  "entire_set": { "mean": "0.78", "size": 3, "standard_deviation": "0.03464101615137754" },
                  "ledger_current_index": 3677185,
                  "median": "0.8",
                  "time": 1724877762,
                  "trimmed_set": { "mean": "0.79", "size": 2, "standard_deviation": "0.01414213562373095" },
                  "validated": false
                }
                """;

            GetAggregatePriceResponse response = JsonSerializer.Deserialize<GetAggregatePriceResponse>(body, XrplJsonOptions.Default);

            Assert.AreEqual("0.78", response.EntireSet.Mean);
            Assert.AreEqual(3u, response.EntireSet.Size);
            Assert.AreEqual("0.03464101615137754", response.EntireSet.StandardDeviation);
            Assert.AreEqual("0.79", response.TrimmedSet.Mean);
            Assert.AreEqual(2u, response.TrimmedSet.Size);
            Assert.AreEqual("0.01414213562373095", response.TrimmedSet.StandardDeviation);
            Assert.AreEqual("0.8", response.Median);
            Assert.AreEqual(new DateTime(2024, 8, 28, 20, 42, 42, DateTimeKind.Utc), response.Time);
            Assert.AreEqual(3677185u, response.LedgerCurrentIndex);
            Assert.IsFalse(response.Validated.Value);
        }

        /// <summary>
        /// Without a trim the node sends no <c>trimmed_set</c>; on a closed ledger it names the ledger by index and hash.
        /// </summary>
        [TestMethod]
        public void TestUResponseFromAClosedLedgerWithoutTrim()
        {
            const string body = """
                {
                  "entire_set": { "mean": "74", "size": 1, "standard_deviation": "0" },
                  "ledger_hash": "4BC50C9B0D8515D3EAAE1E74B29A95804346C491EE1A95BF25E4AAB854A6A652",
                  "ledger_index": 270,
                  "median": "74",
                  "time": 1724877762,
                  "validated": true
                }
                """;

            GetAggregatePriceResponse response = JsonSerializer.Deserialize<GetAggregatePriceResponse>(body, XrplJsonOptions.Default);

            Assert.IsNull(response.TrimmedSet);
            Assert.AreEqual(1u, response.EntireSet.Size);
            Assert.AreEqual(270u, response.LedgerIndex);
            Assert.AreEqual("4BC50C9B0D8515D3EAAE1E74B29A95804346C491EE1A95BF25E4AAB854A6A652", response.LedgerHash);
            Assert.IsNull(response.LedgerCurrentIndex);
            Assert.IsTrue(response.Validated.Value);
        }
    }
}
