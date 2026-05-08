using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.BinaryCodec.Enums;
using Xrpl.BinaryCodec.Types;

// https://github.com/XRPLF/xrpl-py/blob/master/tests/unit/core/binarycodec/test_field_id_codec.py

namespace Xrpl.BinaryCodec.Tests
{
    [TestClass]
    public class TestUFieldIDCodec
    {
        public class TestData
        {
            public string type_name { get; set; }
            public string name { get; set; }
            public int nth_of_type { get; set; }
            public int type { get; set; }
            public string expected_hex { get; set; }
        }

        private static JsonNode GetTestsJson()
        {
            string jsonString = "{\"fields_tests\":[{\"type_name\":\"UInt16\",\"name\":\"LedgerEntryType\",\"nth_of_type\":1,\"type\":1,\"expected_hex\":\"11\"},{\"type_name\":\"UInt16\",\"name\":\"TransactionType\",\"nth_of_type\":2,\"type\":1,\"expected_hex\":\"12\"},{\"type_name\":\"UInt32\",\"name\":\"Flags\",\"nth_of_type\":2,\"type\":2,\"expected_hex\":\"22\"},{\"type_name\":\"UInt32\",\"name\":\"SourceTag\",\"nth_of_type\":3,\"type\":2,\"expected_hex\":\"23\"},{\"type_name\":\"UInt32\",\"name\":\"Sequence\",\"nth_of_type\":4,\"type\":2,\"expected_hex\":\"24\"},{\"type_name\":\"UInt32\",\"name\":\"DestinationTag\",\"nth_of_type\":14,\"type\":2,\"expected_hex\":\"2E\"},{\"type_name\":\"UInt32\",\"name\":\"HighQualityIn\",\"nth_of_type\":16,\"type\":2,\"expected_hex\":\"2010\"},{\"type_name\":\"UInt32\",\"name\":\"HighQualityOut\",\"nth_of_type\":17,\"type\":2,\"expected_hex\":\"2011\"},{\"type_name\":\"UInt64\",\"name\":\"IndexNext\",\"nth_of_type\":1,\"type\":3,\"expected_hex\":\"31\"},{\"type_name\":\"Hash256\",\"name\":\"PreviousTxnID\",\"nth_of_type\":5,\"type\":5,\"expected_hex\":\"55\"},{\"type_name\":\"Hash256\",\"name\":\"BookDirectory\",\"nth_of_type\":16,\"type\":5,\"expected_hex\":\"5010\"},{\"type_name\":\"Amount\",\"name\":\"Amount\",\"nth_of_type\":1,\"type\":6,\"expected_hex\":\"61\"},{\"type_name\":\"Amount\",\"name\":\"Fee\",\"nth_of_type\":8,\"type\":6,\"expected_hex\":\"68\"},{\"type_name\":\"Blob\",\"name\":\"SigningPubKey\",\"nth_of_type\":3,\"type\":7,\"expected_hex\":\"73\"},{\"type_name\":\"AccountID\",\"name\":\"Account\",\"nth_of_type\":1,\"type\":8,\"expected_hex\":\"81\"},{\"type_name\":\"STObject\",\"name\":\"ObjectEndMarker\",\"nth_of_type\":1,\"type\":14,\"expected_hex\":\"E1\"},{\"type_name\":\"STArray\",\"name\":\"Memos\",\"nth_of_type\":9,\"type\":15,\"expected_hex\":\"F9\"},{\"type_name\":\"UInt8\",\"name\":\"CloseResolution\",\"nth_of_type\":1,\"type\":16,\"expected_hex\":\"0110\"},{\"type_name\":\"UInt8\",\"name\":\"TickSize\",\"nth_of_type\":16,\"type\":16,\"expected_hex\":\"001010\"}]}";
            return JsonNode.Parse(jsonString);
        }

        [ClassInitialize]
        public static void Init(TestContext _)
        {
            StObject.FromJson(new JsonObject());
        }

        [TestMethod]
        public void TestFieldHeaderEncode()
        {
            JsonNode obj = GetTestsJson();
            TestData[] tests = JsonSerializer.Deserialize<TestData[]>(obj["fields_tests"]);

            foreach (TestData test in tests)
            {
                if (!Field.Values.Has(test.name))
                    continue;

                Field field = Field.Values[test.name];
                string actualHex = BitConverter.ToString(field.Header).Replace("-", "");
                Assert.AreEqual(
                    test.expected_hex.ToUpperInvariant(),
                    actualHex.ToUpperInvariant(),
                    $"Field '{test.name}' header mismatch: expected {test.expected_hex}, got {actualHex}");
            }
        }

        [TestMethod]
        public void TestFieldHeaderDecode()
        {
            JsonNode obj = GetTestsJson();
            TestData[] tests = JsonSerializer.Deserialize<TestData[]>(obj["fields_tests"]);

            foreach (TestData test in tests)
            {
                if (!Field.Values.Has(test.name))
                    continue;

                Field field = Field.Values[test.name];
                // Verify that looking up by ordinal gives back the same field
                int ordinal = (field.Type.Ordinal << 16) | field.NthOfType;
                Field resolved = Field.Values[ordinal];
                Assert.AreEqual(test.name, resolved.Name,
                    $"Field lookup by ordinal failed for '{test.name}'");
            }
        }

        [TestMethod]
        public void TestFieldTypeCode()
        {
            JsonNode obj = GetTestsJson();
            TestData[] tests = JsonSerializer.Deserialize<TestData[]>(obj["fields_tests"]);

            foreach (TestData test in tests)
            {
                if (!Field.Values.Has(test.name))
                    continue;

                Field field = Field.Values[test.name];
                Assert.AreEqual(test.type, field.Type.Ordinal,
                    $"Field '{test.name}' type code mismatch");
                Assert.AreEqual(test.nth_of_type, field.NthOfType,
                    $"Field '{test.name}' nth_of_type mismatch");
            }
        }
    }
}
