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
            envelope.AttachFrame(frame);
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
            envelope.AttachFrame(frame);

            Assert.IsFalse(envelope.HasNextPage());
        }

        /// <summary>An envelope built by hand carries no frame, so there is nothing to read.</summary>
        [TestMethod]
        public void TestUEnvelopeWithoutFrameHasNoNextPage()
        {
            Assert.IsFalse(new ErrorResponse().HasNextPage());
        }

        /// <summary>The scanner must bail on a non-object result rather than misread it.</summary>
        [TestMethod]
        public void TestUNonObjectResultHasNoNextPage()
        {
            Assert.IsFalse(Envelope("[1,2]").HasNextPage());
            Assert.IsFalse(Envelope("\"marker\"").HasNextPage());
            Assert.IsFalse(Envelope("42").HasNextPage());
            Assert.IsFalse(Envelope("null").HasNextPage());
        }

        [TestMethod]
        public void TestUEmptyResultObjectHasNoNextPage()
        {
            Assert.IsFalse(Envelope("{}").HasNextPage());
        }

        /// <summary>
        /// Works only because the scan goes through ValueTextEquals, which unescapes. Swapping it
        /// for a raw byte comparison would pass every other test here and break this one silently.
        /// </summary>
        [TestMethod]
        public void TestUEscapedMarkerKeyIsRecognized()
        {
            Assert.IsTrue(Envelope("{\"\\u006darker\":\"AABB\"}").HasNextPage());
            Assert.IsFalse(Envelope("{\"state\":[{\"\\u006darker\":\"AABB\"}]}").HasNextPage());
        }

        /// <summary>Prefix and near-miss keys must not count.</summary>
        [TestMethod]
        public void TestUNearMissKeysAreNotTheMarker()
        {
            Assert.IsFalse(Envelope("{\"markerX\":1}").HasNextPage());
            Assert.IsFalse(Envelope("{\"marke\":1}").HasNextPage());
        }
    }
}
