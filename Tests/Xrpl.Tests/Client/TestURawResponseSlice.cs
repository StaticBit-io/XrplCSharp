using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Text;

using Xrpl.Client.Json;

namespace Xrpl.Tests.ClientLib
{
    /// <summary>
    /// Covers the window a consumer is handed onto the bytes a node actually sent. The converter
    /// and the slice type have their own tests next to the other converters; this is about what
    /// comes out the other end.
    /// </summary>
    [TestClass]
    public class TestURawResponseSlice
    {
        [TestMethod]
        public void TestURawJsonRendersTheOriginalBytes()
        {
            // `{"result": {"a" : 1} }` — the inner object starts at byte 11 and is 9 bytes long.
            byte[] frame = Encoding.UTF8.GetBytes("{\"result\": {\"a\" : 1} }");
            RawJson raw = new RawJson(frame, 11, 9);

            Assert.AreEqual("{\"a\" : 1}", raw.ToString());
            Assert.AreEqual(9, raw.Length);
            Assert.IsFalse(raw.IsEmpty);
        }

        [TestMethod]
        public void TestURawJsonDefaultIsEmpty()
        {
            RawJson raw = default;

            Assert.IsTrue(raw.IsEmpty);
            Assert.AreEqual(string.Empty, raw.ToString());
            Assert.AreEqual(0, raw.Span.Length);
            Assert.AreEqual(0, raw.Length);
        }

        [TestMethod]
        public void TestURawJsonToArrayCopiesTheSlice()
        {
            // `{"result":{"a":1}}` — the inner object starts at byte 10 and is 7 bytes long.
            byte[] frame = Encoding.UTF8.GetBytes("{\"result\":{\"a\":1}}");
            RawJson raw = new RawJson(frame, 10, 7);

            byte[] copy = raw.ToArray();

            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("{\"a\":1}"), copy);
            Assert.AreEqual(0, default(RawJson).ToArray().Length);
        }

        [TestMethod]
        public void TestURawJsonSpanDoesNotCopy()
        {
            byte[] frame = Encoding.UTF8.GetBytes("{\"result\":{\"a\":1}}");
            RawJson raw = new RawJson(frame, 10, 7);

            Assert.IsTrue(raw.Span.SequenceEqual(Encoding.UTF8.GetBytes("{\"a\":1}")));
        }
    }
}
