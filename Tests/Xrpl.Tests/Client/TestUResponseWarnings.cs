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
        public void Details_RoundTrip_WritesStrings()
        {
            BaseResponse response = JsonSerializer.Deserialize<BaseResponse>(Response, XrplJsonOptions.Default);

            string written = JsonSerializer.Serialize(response.Warnings[0], XrplJsonOptions.Default);
            RippleResponseWarning read = JsonSerializer.Deserialize<RippleResponseWarning>(written, XrplJsonOptions.Default);

            Assert.AreEqual("782870400", read.Details["expected_date"]);
        }
    }
}
