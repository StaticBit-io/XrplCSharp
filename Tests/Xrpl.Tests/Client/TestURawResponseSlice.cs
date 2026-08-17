using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;

namespace Xrpl.Tests.ClientLib
{
    /// <summary>
    /// Pins that the response envelope records where <c>result</c> sits in the frame instead of
    /// materializing it. The slice has to be byte-exact: everything downstream — the typed
    /// deserialization and the raw JSON handed to consumers — is cut from it.
    /// </summary>
    [TestClass]
    public class TestURawResponseSlice
    {
        private sealed class SliceProbe
        {
            [JsonPropertyName("result")]
            [JsonConverter(typeof(JsonSliceConverter))]
            public JsonSlice Result { get; set; }
        }

        [TestMethod]
        public void TestUSliceMatchesResultSubtreeExactly()
        {
            // Deliberately irregular whitespace: the slice must reproduce the bytes as sent,
            // not a normalized rendering of them.
            string message = "{\"id\":\"7\", \"status\":\"success\", \"result\": {\"a\" : 1,\"b\":[2, 3]} , \"warning\":\"load\"}";
            byte[] frame = Encoding.UTF8.GetBytes(message);

            SliceProbe probe = JsonSerializer.Deserialize<SliceProbe>(frame, new JsonSerializerOptions());

            string expected = "{\"a\" : 1,\"b\":[2, 3]}";
            string actual = Encoding.UTF8.GetString(frame, probe.Result.Offset, probe.Result.Length);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void TestUSliceIsEmptyWhenResultAbsent()
        {
            byte[] frame = Encoding.UTF8.GetBytes("{\"id\":\"7\",\"status\":\"success\"}");

            SliceProbe probe = JsonSerializer.Deserialize<SliceProbe>(frame, new JsonSerializerOptions());

            Assert.IsTrue(probe.Result.IsEmpty);
        }

        [TestMethod]
        public void TestUSliceCoversExplicitNull()
        {
            byte[] frame = Encoding.UTF8.GetBytes("{\"id\":\"7\",\"result\":null}");

            SliceProbe probe = JsonSerializer.Deserialize<SliceProbe>(frame, new JsonSerializerOptions());

            Assert.AreEqual("null", Encoding.UTF8.GetString(frame, probe.Result.Offset, probe.Result.Length));
        }
    }
}
