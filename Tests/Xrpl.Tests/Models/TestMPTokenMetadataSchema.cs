using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Models.Transactions;
using Xrpl.Models.Utils;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// Unit tests for MPTokenMetadataSchema class.
    /// Tests serialization, deserialization, and roundtrip conversions for XLS-89 metadata.
    /// </summary>
    [TestClass]
    public class TestMPTokenMetadataSchema
    {
        [TestMethod]
        public void TestToHexAndFromHexRoundtrip()
        {
            var schema = new MPTokenMetadataSchema
            {
                Ticker = "TBILL",
                Name = "T-Bill Yield Token",
                Description = "A yield-bearing stablecoin backed by short-term U.S. Treasuries and money market instruments.",
                Icon = "example.org/tbill-icon.png",
                AssetClass = MPTokenAssetClass.Rwa,
                AssetSubclass = MPTokenAssetSubclass.Treasury,
                IssuerName = "Example Yield Co."
            };

            string hex = schema.ToHex();
            Assert.IsNotNull(hex);

            var deserialized = MPTokenMetadataSchema.FromHex(hex);
            Assert.IsNotNull(deserialized);
            Assert.AreEqual("TBILL", deserialized.Ticker);
            Assert.AreEqual("T-Bill Yield Token", deserialized.Name);
            Assert.AreEqual("A yield-bearing stablecoin backed by short-term U.S. Treasuries and money market instruments.", deserialized.Description);
            Assert.AreEqual("example.org/tbill-icon.png", deserialized.Icon);
            Assert.AreEqual(MPTokenAssetClass.Rwa, deserialized.AssetClass);
            Assert.AreEqual(MPTokenAssetSubclass.Treasury, deserialized.AssetSubclass);
            Assert.AreEqual("Example Yield Co.", deserialized.IssuerName);
        }

        [TestMethod]
        public void TestFromJsonShortKeys()
        {
            string json = "{\"t\":\"TBILL\",\"n\":\"T-Bill Yield Token\",\"d\":\"A yield-bearing stablecoin\",\"i\":\"example.org/tbill-icon.png\",\"ac\":\"rwa\",\"as\":\"treasury\",\"in\":\"Example Yield Co.\"}";

            var schema = MPTokenMetadataSchema.FromJson(json);
            Assert.IsNotNull(schema);
            Assert.AreEqual("TBILL", schema.Ticker);
            Assert.AreEqual("T-Bill Yield Token", schema.Name);
            Assert.AreEqual("A yield-bearing stablecoin", schema.Description);
            Assert.AreEqual("example.org/tbill-icon.png", schema.Icon);
            Assert.AreEqual("rwa", schema.AssetClass);
            Assert.AreEqual("treasury", schema.AssetSubclass);
            Assert.AreEqual("Example Yield Co.", schema.IssuerName);
        }

        [TestMethod]
        public void TestFromJsonLongKeys()
        {
            string json = "{\"ticker\":\"TBILL\",\"name\":\"T-Bill Yield Token\",\"desc\":\"A yield-bearing stablecoin\",\"icon\":\"example.org/tbill-icon.png\",\"asset_class\":\"rwa\",\"asset_subclass\":\"treasury\",\"issuer_name\":\"Example Yield Co.\"}";

            var schema = MPTokenMetadataSchema.FromJson(json);
            Assert.IsNotNull(schema);
            Assert.AreEqual("TBILL", schema.Ticker);
            Assert.AreEqual("T-Bill Yield Token", schema.Name);
            Assert.AreEqual("A yield-bearing stablecoin", schema.Description);
            Assert.AreEqual("example.org/tbill-icon.png", schema.Icon);
            Assert.AreEqual("rwa", schema.AssetClass);
            Assert.AreEqual("treasury", schema.AssetSubclass);
            Assert.AreEqual("Example Yield Co.", schema.IssuerName);
        }

        [TestMethod]
        public void TestUrisRoundtrip()
        {
            var schema = new MPTokenMetadataSchema
            {
                Ticker = "TST",
                Name = "Test Token",
                AssetClass = "other",
                Uris = new List<MPTokenMetadataUri>
                {
                    new MPTokenMetadataUri
                    {
                        Uri = "https://example.org/token",
                        Category = "website",
                        Title = "Token Website"
                    },
                    new MPTokenMetadataUri
                    {
                        Uri = "https://example.org/docs",
                        Category = "docs",
                        Title = "Documentation"
                    }
                }
            };

            string hex = schema.ToHex();
            var deserialized = MPTokenMetadataSchema.FromHex(hex);

            Assert.IsNotNull(deserialized.Uris);
            Assert.AreEqual(2, deserialized.Uris.Count);
            Assert.AreEqual("https://example.org/token", deserialized.Uris[0].Uri);
            Assert.AreEqual("website", deserialized.Uris[0].Category);
            Assert.AreEqual("Token Website", deserialized.Uris[0].Title);
            Assert.AreEqual("https://example.org/docs", deserialized.Uris[1].Uri);
            Assert.AreEqual("docs", deserialized.Uris[1].Category);
            Assert.AreEqual("Documentation", deserialized.Uris[1].Title);
        }

        [TestMethod]
        public void TestAdditionalInfoRoundtrip()
        {
            var schema = new MPTokenMetadataSchema
            {
                Ticker = "BOND",
                Name = "Bond Token",
                AssetClass = "rwa",
                AdditionalInfo = new Dictionary<string, object>
                {
                    { "interest_rate", "5.00%" },
                    { "maturity_date", "2045-06-30" },
                    { "cusip", "912796RX0" }
                }
            };

            string hex = schema.ToHex();
            var deserialized = MPTokenMetadataSchema.FromHex(hex);

            Assert.IsNotNull(deserialized.AdditionalInfo);
            Assert.AreEqual(3, deserialized.AdditionalInfo.Count);
            Assert.AreEqual("5.00%", deserialized.AdditionalInfo["interest_rate"]);
            Assert.AreEqual("2045-06-30", deserialized.AdditionalInfo["maturity_date"]);
            Assert.AreEqual("912796RX0", deserialized.AdditionalInfo["cusip"]);
        }

        [TestMethod]
        public void TestExceeds1024ByteLimit()
        {
            string longDescription = new string('A', 1500);
            var schema = new MPTokenMetadataSchema
            {
                Ticker = "BIG",
                Name = "Big Token",
                Description = longDescription,
                AssetClass = "other"
            };

            Helper.ThrowsException<InvalidOperationException>(() => schema.ToHex());
        }

        [TestMethod]
        public void TestExceeds1024ByteLimitMessageContent()
        {
            string longDescription = new string('A', 1500);
            var schema = new MPTokenMetadataSchema
            {
                Ticker = "BIG",
                Name = "Big Token",
                Description = longDescription,
                AssetClass = "other"
            };

            try
            {
                schema.ToHex();
                Assert.Fail("Expected InvalidOperationException");
            }
            catch (InvalidOperationException ex)
            {
                Assert.IsTrue(ex.Message.Contains("1024-byte limit"), "Exception message should contain '1024-byte limit'");
            }
        }

        [TestMethod]
        public void TestGetByteSize()
        {
            var schema = new MPTokenMetadataSchema
            {
                Ticker = "TST",
                Name = "Test",
                Icon = "t.com/i.png",
                AssetClass = "other",
                IssuerName = "Me"
            };

            int size = schema.GetByteSize();
            Assert.IsTrue(size > 0);
            Assert.IsTrue(size < 1024);
        }

        [TestMethod]
        public void TestFromHexNull()
        {
            var result = MPTokenMetadataSchema.FromHex(null);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void TestFromHexEmptyString()
        {
            var result = MPTokenMetadataSchema.FromHex(string.Empty);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void TestToJsonShortKeys()
        {
            var schema = new MPTokenMetadataSchema
            {
                Ticker = "TEST",
                Name = "Test Token"
            };

            string json = schema.ToJson(true);
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Contains("\"t\":"));
            Assert.IsTrue(json.Contains("\"n\":"));
        }

        [TestMethod]
        public void TestToJsonLongKeys()
        {
            var schema = new MPTokenMetadataSchema
            {
                Ticker = "TEST",
                Name = "Test Token"
            };

            string json = schema.ToJson(false);
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Contains("\"ticker\":"));
            Assert.IsTrue(json.Contains("\"name\":"));
        }

        [TestMethod]
        public void TestEnumConstants()
        {
            Assert.AreEqual("rwa", MPTokenAssetClass.Rwa);
            Assert.AreEqual("memes", MPTokenAssetClass.Memes);
            Assert.AreEqual("wrapped", MPTokenAssetClass.Wrapped);
            Assert.AreEqual("gaming", MPTokenAssetClass.Gaming);
            Assert.AreEqual("defi", MPTokenAssetClass.Defi);
            Assert.AreEqual("other", MPTokenAssetClass.Other);

            Assert.AreEqual("stablecoin", MPTokenAssetSubclass.Stablecoin);
            Assert.AreEqual("commodity", MPTokenAssetSubclass.Commodity);
            Assert.AreEqual("real_estate", MPTokenAssetSubclass.RealEstate);
            Assert.AreEqual("private_credit", MPTokenAssetSubclass.PrivateCredit);
            Assert.AreEqual("equity", MPTokenAssetSubclass.Equity);
            Assert.AreEqual("treasury", MPTokenAssetSubclass.Treasury);
            Assert.AreEqual("other", MPTokenAssetSubclass.Other);

            Assert.AreEqual("website", MPTokenUriCategory.Website);
            Assert.AreEqual("social", MPTokenUriCategory.Social);
            Assert.AreEqual("docs", MPTokenUriCategory.Docs);
            Assert.AreEqual("other", MPTokenUriCategory.Other);
        }

        [TestMethod]
        public void TestFullXls89Example()
        {
            string json = "{\"t\":\"TBILL\",\"n\":\"T-Bill Yield Token\",\"d\":\"A yield-bearing stablecoin backed by short-term U.S. Treasuries and money market instruments.\",\"i\":\"example.org/tbill-icon.png\",\"ac\":\"rwa\",\"as\":\"treasury\",\"in\":\"Example Yield Co.\",\"us\":[{\"u\":\"exampleyield.co/tbill\",\"c\":\"website\",\"t\":\"Product Page\"},{\"u\":\"exampleyield.co/docs\",\"c\":\"docs\",\"t\":\"Yield Token Docs\"}],\"ai\":{\"interest_rate\":\"5.00%\",\"interest_type\":\"variable\",\"yield_source\":\"U.S. Treasury Bills\",\"maturity_date\":\"2045-06-30\",\"cusip\":\"912796RX0\"}}";

            var schema = MPTokenMetadataSchema.FromJson(json);
            Assert.IsNotNull(schema);
            Assert.AreEqual("TBILL", schema.Ticker);
            Assert.AreEqual("T-Bill Yield Token", schema.Name);
            Assert.AreEqual("A yield-bearing stablecoin backed by short-term U.S. Treasuries and money market instruments.", schema.Description);
            Assert.AreEqual("example.org/tbill-icon.png", schema.Icon);
            Assert.AreEqual("rwa", schema.AssetClass);
            Assert.AreEqual("treasury", schema.AssetSubclass);
            Assert.AreEqual("Example Yield Co.", schema.IssuerName);

            Assert.IsNotNull(schema.Uris);
            Assert.AreEqual(2, schema.Uris.Count);
            Assert.AreEqual("exampleyield.co/tbill", schema.Uris[0].Uri);
            Assert.AreEqual("website", schema.Uris[0].Category);
            Assert.AreEqual("Product Page", schema.Uris[0].Title);
            Assert.AreEqual("exampleyield.co/docs", schema.Uris[1].Uri);
            Assert.AreEqual("docs", schema.Uris[1].Category);
            Assert.AreEqual("Yield Token Docs", schema.Uris[1].Title);

            Assert.IsNotNull(schema.AdditionalInfo);
            Assert.AreEqual(5, schema.AdditionalInfo.Count);
            Assert.AreEqual("5.00%", schema.AdditionalInfo["interest_rate"]);
            Assert.AreEqual("variable", schema.AdditionalInfo["interest_type"]);
            Assert.AreEqual("U.S. Treasury Bills", schema.AdditionalInfo["yield_source"]);
            Assert.AreEqual("2045-06-30", schema.AdditionalInfo["maturity_date"]);
            Assert.AreEqual("912796RX0", schema.AdditionalInfo["cusip"]);

            string hex = schema.ToHex();
            var deserialized = MPTokenMetadataSchema.FromHex(hex);
            Assert.IsNotNull(deserialized);
            Assert.AreEqual("TBILL", deserialized.Ticker);
            Assert.AreEqual("T-Bill Yield Token", deserialized.Name);
            Assert.AreEqual(2, deserialized.Uris.Count);
            Assert.AreEqual(5, deserialized.AdditionalInfo.Count);
        }
        [TestMethod]
        public void TestFromHexInvalidHexReturnsNull()
        {
            var result = MPTokenMetadataSchema.FromHex("ZZZZZZ");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void TestFromHexNonJsonReturnsNull()
        {
            var hex = Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes("this is not json"));
            var result = MPTokenMetadataSchema.FromHex(hex);
            Assert.IsNull(result);
        }

        [TestMethod]
        public void TestFromHexBinaryDataReturnsNull()
        {
            var result = MPTokenMetadataSchema.FromHex("DEADBEEF");
            Assert.IsNull(result);
        }

        [TestMethod]
        public void TestTryFromHexSuccess()
        {
            var schema = new MPTokenMetadataSchema { Ticker = "TST", Name = "Test" };
            var hex = schema.ToHex();

            bool success = MPTokenMetadataSchema.TryFromHex(hex, out var parsed);
            Assert.IsTrue(success);
            Assert.IsNotNull(parsed);
            Assert.AreEqual("TST", parsed.Ticker);
        }

        [TestMethod]
        public void TestTryFromHexFailure()
        {
            bool success = MPTokenMetadataSchema.TryFromHex("not_hex!", out var parsed);
            Assert.IsFalse(success);
            Assert.IsNull(parsed);
        }

        [TestMethod]
        public void TestTryFromHexNullReturnsFalse()
        {
            bool success = MPTokenMetadataSchema.TryFromHex(null, out var parsed);
            Assert.IsFalse(success);
            Assert.IsNull(parsed);
        }

        [TestMethod]
        public void TestCacheInvalidationOnMPTokenMetadataChange()
        {
            var tx = new MPTokenIssuanceCreate();

            var schema1 = new MPTokenMetadataSchema { Ticker = "AAA", Name = "First" };
            tx.Metadata = schema1;
            Assert.AreEqual("AAA", tx.Metadata.Ticker);

            var schema2 = new MPTokenMetadataSchema { Ticker = "BBB", Name = "Second" };
            tx.MPTokenMetadata = schema2.ToHex();

            Assert.AreEqual("BBB", tx.Metadata.Ticker);
        }

        [TestMethod]
        public void TestMetadataSetterSyncsHex()
        {
            var tx = new MPTokenIssuanceCreate();
            var schema = new MPTokenMetadataSchema { Ticker = "XYZ", Name = "Sync Test" };

            tx.Metadata = schema;
            Assert.IsNotNull(tx.MPTokenMetadata);

            var parsed = MPTokenMetadataSchema.FromHex(tx.MPTokenMetadata);
            Assert.IsNotNull(parsed);
            Assert.AreEqual("XYZ", parsed.Ticker);
        }

        [TestMethod]
        public void TestInvalidHexInMPTokenMetadataReturnsNullMetadata()
        {
            var tx = new MPTokenIssuanceCreate();
            tx.MPTokenMetadata = "DEADBEEF";

            Assert.IsNull(tx.Metadata);
        }
    }
}
