using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Models.Subscriptions;

namespace XrplTests.Xrpl.Client
{
    /// <summary>
    /// The <c>warnings</c> array of a response, including warning 1001, whose details are not all strings.
    /// </summary>
    [TestClass]
    public class TestUResponseWarnings
    {
        // Warning 1001 as rippled writes it (RPCHelpers.cpp injectWarnings / amendment blocked soon):
        // expected_date is Ripple-epoch seconds, a number.
        private const string Response = """
            {
              "id": 1,
              "status": "success",
              "type": "response",
              "result": { "ledger_index": 5 },
              "warnings": [
                {
                  "id": 1001,
                  "message": "One or more unsupported amendments have reached majority. Upgrade to the latest version before they are activated to avoid being amendment blocked.",
                  "details": {
                    "expected_date": 782870400,
                    "expected_date_UTC": "2024-Oct-23 00:00:00.000000000 UTC",
                    "nested": { "a": [1, 2] },
                    "missing": null
                  }
                },
                { "id": 1004, "message": "This is a reporting server." }
              ]
            }
            """;

        [TestMethod]
        public void Warning1001_WithNumericDetails_IsRead()
        {
            BaseResponse response = JsonSerializer.Deserialize<BaseResponse>(Response, XrplJsonOptions.Default);

            Assert.AreEqual(2, response.Warnings.Count);
            RippleResponseWarning warning = response.Warnings[0];
            Assert.AreEqual(1001u, warning.Id);
            Assert.AreEqual("782870400", warning.Details["expected_date"]);
            Assert.AreEqual("2024-Oct-23 00:00:00.000000000 UTC", warning.Details["expected_date_UTC"]);
            Assert.AreEqual("{\"a\":[1,2]}", JsonDocument.Parse(warning.Details["nested"]).RootElement.GetRawText().Replace(" ", ""));
            Assert.IsNull(warning.Details["missing"]);
            Assert.IsNull(response.Warnings[1].Details);
        }

        [TestMethod]
        public void Details_RoundTrip_KeepsTheJsonTokenKinds()
        {
            BaseResponse response = JsonSerializer.Deserialize<BaseResponse>(Response, XrplJsonOptions.Default);

            string written = JsonSerializer.Serialize(response.Warnings[0], XrplJsonOptions.Default);
            JsonElement details = JsonDocument.Parse(written).RootElement.GetProperty("details");

            Assert.AreEqual(JsonValueKind.Number, details.GetProperty("expected_date").ValueKind);
            Assert.AreEqual(782870400L, details.GetProperty("expected_date").GetInt64());
            Assert.AreEqual(JsonValueKind.String, details.GetProperty("expected_date_UTC").ValueKind);
            Assert.AreEqual(JsonValueKind.Object, details.GetProperty("nested").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, details.GetProperty("missing").ValueKind);

            RippleResponseWarning read = JsonSerializer.Deserialize<RippleResponseWarning>(written, XrplJsonOptions.Default);
            Assert.AreEqual("782870400", read.Details["expected_date"]);
        }

        /// <summary>
        /// A value the caller replaced is written as a JSON string, never as a raw token, even when
        /// the key was read as a number.
        /// </summary>
        [TestMethod]
        public void Details_ReplacedValue_IsWrittenAsAString()
        {
            BaseResponse response = JsonSerializer.Deserialize<BaseResponse>(Response, XrplJsonOptions.Default);
            RippleResponseWarning warning = response.Warnings[0];
            warning.Details["expected_date"] = "not json";

            JsonElement details = JsonDocument.Parse(JsonSerializer.Serialize(warning, XrplJsonOptions.Default)).RootElement.GetProperty("details");

            Assert.AreEqual("not json", details.GetProperty("expected_date").GetString());
        }

        [TestMethod]
        public void Details_BuiltByHand_IsWrittenAsStrings()
        {
            RippleResponseWarning warning = new RippleResponseWarning
            {
                Id = 1001,
                Details = new System.Collections.Generic.Dictionary<string, string> { ["expected_date"] = "782870400" },
            };

            JsonElement details = JsonDocument.Parse(JsonSerializer.Serialize(warning, XrplJsonOptions.Default)).RootElement.GetProperty("details");

            Assert.AreEqual(JsonValueKind.String, details.GetProperty("expected_date").ValueKind);
        }
    }
}
