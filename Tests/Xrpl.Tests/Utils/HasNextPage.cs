// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/utils/hasNextPage.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text;
using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Models.Subscriptions;
using Xrpl.Utils;

namespace XrplTests.Xrpl.Utils
{
    /// <summary>
    /// Port of xrpl.js `hasNextPage.ts`. The class name carries the TestU prefix because the CI
    /// filter matches on the fully qualified name — as `HasNextPage` it would never have run.
    /// </summary>
    [TestClass]
    public class TestUHasNextPage
    {
        private static BaseResponse Envelope(string result)
        {
            byte[] frame = Encoding.UTF8.GetBytes($"{{\"id\":\"7\",\"status\":\"success\",\"result\":{result}}}");
            ErrorResponse envelope = JsonSerializer.Deserialize<ErrorResponse>(frame, XrplJsonOptions.Default);
            envelope.Frame = frame;
            return envelope;
        }

        [TestMethod]
        public void TestUMarkerPresentMeansMorePages()
        {
            Assert.IsTrue(Envelope("{\"marker\":\"AABB\",\"state\":[]}").HasNextPage());
        }

        [TestMethod]
        public void TestUMarkerAbsentMeansLastPage()
        {
            Assert.IsFalse(Envelope("{\"state\":[]}").HasNextPage());
        }

        /// <summary>The marker need not be first, and skipping over earlier members must not eat it.</summary>
        [TestMethod]
        public void TestUMarkerFoundAfterNestedMembers()
        {
            Assert.IsTrue(Envelope(
                "{\"state\":[{\"a\":{\"b\":[1,2]}}],\"ledger_index\":9,\"marker\":{\"ledger\":9,\"seq\":1}}")
                .HasNextPage());
        }

        /// <summary>A `marker` nested inside another member is not the paging marker.</summary>
        [TestMethod]
        public void TestUNestedMarkerIsNotThePagingMarker()
        {
            Assert.IsFalse(Envelope("{\"state\":[{\"marker\":\"AABB\"}]}").HasNextPage());
        }

        [TestMethod]
        public void TestUEnvelopeWithoutResultHasNoNextPage()
        {
            byte[] frame = Encoding.UTF8.GetBytes("{\"id\":\"7\",\"status\":\"success\"}");
            ErrorResponse envelope = JsonSerializer.Deserialize<ErrorResponse>(frame, XrplJsonOptions.Default);
            envelope.Frame = frame;

            Assert.IsFalse(envelope.HasNextPage());
        }

        /// <summary>An envelope built by hand carries no frame, so there is nothing to read.</summary>
        [TestMethod]
        public void TestUEnvelopeWithoutFrameHasNoNextPage()
        {
            Assert.IsFalse(new ErrorResponse().HasNextPage());
        }
    }
}
