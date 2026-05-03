using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.BinaryCodec.Types;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

using XrplTests;

using BinaryCodecCurrency = Xrpl.BinaryCodec.Types.Currency;
using ModelCurrency = Xrpl.Models.Common.Currency;
using ModelPriceData = Xrpl.Models.Common.PriceData;
using ModelPriceDataWrapper = Xrpl.Models.Common.PriceDataWrapper;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// Unit tests for OracleSet transaction validation.
    /// </summary>
    [TestClass]
    public class TestUOracleSet
    {
        /// <summary>
        /// Tests that a valid OracleSet transaction passes validation.
        /// </summary>
        [TestMethod]
        public async Task TestVerify_Valid_OracleSet()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "OracleSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "OracleDocumentID", 1u },
                { "LastUpdateTime", 1715097600u },
                { "Provider", "chainlink" },
                { "AssetClass", "currency" },
                { "PriceDataSeries", new List<object>
                    {
                        new Dictionary<string, dynamic>
                        {
                            { "PriceData", new Dictionary<string, dynamic>
                                {
                                    { "BaseAsset", "XRP" },
                                    { "QuoteAsset", "USD" },
                                    { "AssetPrice", 740u },
                                    { "Scale", 3u }
                                }
                            }
                        }
                    }
                }
            };
            await Validation.ValidateOracleSet(tx);
            await Validation.Validate(tx);
        }

        /// <summary>
        /// Tests that a valid OracleSet with multiple PriceData objects passes validation.
        /// </summary>
        [TestMethod]
        public async Task TestVerify_Valid_OracleSet_MultiplePriceData()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "OracleSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "OracleDocumentID", 1u },
                { "LastUpdateTime", 1715097600u },
                { "Provider", "chainlink" },
                { "AssetClass", "currency" },
                { "PriceDataSeries", new List<object>
                    {
                        new Dictionary<string, dynamic>
                        {
                            { "PriceData", new Dictionary<string, dynamic>
                                {
                                    { "BaseAsset", "XRP" },
                                    { "QuoteAsset", "USD" },
                                    { "AssetPrice", 740u },
                                    { "Scale", 3u }
                                }
                            }
                        },
                        new Dictionary<string, dynamic>
                        {
                            { "PriceData", new Dictionary<string, dynamic>
                                {
                                    { "BaseAsset", "BTC" },
                                    { "QuoteAsset", "USD" },
                                    { "AssetPrice", 65000u },
                                    { "Scale", 0u }
                                }
                            }
                        }
                    }
                }
            };
            await Validation.ValidateOracleSet(tx);
        }

        /// <summary>
        /// Tests that OracleSet without OracleDocumentID fails validation.
        /// </summary>
        [TestMethod]
        public async Task TestVerify_Invalid_MissingOracleDocumentID()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "OracleSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "LastUpdateTime", 1715097600u },
                { "Provider", "chainlink" },
                { "AssetClass", "currency" },
                { "PriceDataSeries", new List<object>
                    {
                        new Dictionary<string, dynamic>
                        {
                            { "PriceData", new Dictionary<string, dynamic>
                                {
                                    { "BaseAsset", "XRP" },
                                    { "QuoteAsset", "USD" }
                                }
                            }
                        }
                    }
                }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateOracleSet(tx),
                "OracleSet: missing field OracleDocumentID");
        }

        /// <summary>
        /// Tests that OracleSet without LastUpdateTime fails validation.
        /// </summary>
        [TestMethod]
        public async Task TestVerify_Invalid_MissingLastUpdateTime()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "OracleSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "OracleDocumentID", 1u },
                { "Provider", "chainlink" },
                { "AssetClass", "currency" },
                { "PriceDataSeries", new List<object>
                    {
                        new Dictionary<string, dynamic>
                        {
                            { "PriceData", new Dictionary<string, dynamic>
                                {
                                    { "BaseAsset", "XRP" },
                                    { "QuoteAsset", "USD" }
                                }
                            }
                        }
                    }
                }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateOracleSet(tx),
                "OracleSet: missing field LastUpdateTime");
        }

        /// <summary>
        /// Tests that OracleSet without PriceDataSeries fails validation.
        /// </summary>
        [TestMethod]
        public async Task TestVerify_Invalid_MissingPriceDataSeries()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "OracleSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "OracleDocumentID", 1u },
                { "LastUpdateTime", 1715097600u },
                { "Provider", "chainlink" },
                { "AssetClass", "currency" }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateOracleSet(tx),
                "OracleSet: missing field PriceDataSeries");
        }

        /// <summary>
        /// Tests that OracleSet with empty PriceDataSeries fails validation.
        /// </summary>
        [TestMethod]
        public async Task TestVerify_Invalid_EmptyPriceDataSeries()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "OracleSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "OracleDocumentID", 1u },
                { "LastUpdateTime", 1715097600u },
                { "Provider", "chainlink" },
                { "AssetClass", "currency" },
                { "PriceDataSeries", new List<object>() }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateOracleSet(tx),
                "OracleSet: PriceDataSeries must not be empty");
        }

        /// <summary>
        /// Tests that OracleSet with more than 10 PriceData objects fails validation.
        /// </summary>
        [TestMethod]
        public async Task TestVerify_Invalid_ExceedsMaxPriceDataSeries()
        {
            var priceDataList = new List<object>();
            for (int i = 0; i < 11; i++)
            {
                priceDataList.Add(new Dictionary<string, dynamic>
                {
                    { "PriceData", new Dictionary<string, dynamic>
                        {
                            { "BaseAsset", $"AS{i}" },
                            { "QuoteAsset", "USD" }
                        }
                    }
                });
            }

            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "OracleSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "OracleDocumentID", 1u },
                { "LastUpdateTime", 1715097600u },
                { "Provider", "chainlink" },
                { "AssetClass", "currency" },
                { "PriceDataSeries", priceDataList }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateOracleSet(tx),
                "OracleSet: PriceDataSeries must have at most 10 PriceData objects");
        }

        /// <summary>
        /// Tests that OracleSet with Scale greater than 10 fails validation.
        /// </summary>
        [TestMethod]
        public async Task TestVerify_Invalid_ScaleExceedsMax()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "OracleSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "OracleDocumentID", 1u },
                { "LastUpdateTime", 1715097600u },
                { "Provider", "chainlink" },
                { "AssetClass", "currency" },
                { "PriceDataSeries", new List<object>
                    {
                        new Dictionary<string, dynamic>
                        {
                            { "PriceData", new Dictionary<string, dynamic>
                                {
                                    { "BaseAsset", "XRP" },
                                    { "QuoteAsset", "USD" },
                                    { "AssetPrice", 740u },
                                    { "Scale", 11u }
                                }
                            }
                        }
                    }
                }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateOracleSet(tx),
                "OracleSet: Scale must be in range 0-10");
        }

        /// <summary>
        /// Tests that OracleSet with missing BaseAsset fails validation.
        /// </summary>
        [TestMethod]
        public async Task TestVerify_Invalid_MissingBaseAsset()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "OracleSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "OracleDocumentID", 1u },
                { "LastUpdateTime", 1715097600u },
                { "Provider", "chainlink" },
                { "AssetClass", "currency" },
                { "PriceDataSeries", new List<object>
                    {
                        new Dictionary<string, dynamic>
                        {
                            { "PriceData", new Dictionary<string, dynamic>
                                {
                                    { "QuoteAsset", "USD" }
                                }
                            }
                        }
                    }
                }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateOracleSet(tx),
                "OracleSet: PriceData must have a BaseAsset string");
        }

        /// <summary>
        /// Tests that OracleSet with missing QuoteAsset fails validation.
        /// </summary>
        [TestMethod]
        public async Task TestVerify_Invalid_MissingQuoteAsset()
        {
            var tx = new Dictionary<string, dynamic>
            {
                { "Account", "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W" },
                { "TransactionType", "OracleSet" },
                { "Fee", "12" },
                { "Sequence", 1u },
                { "OracleDocumentID", 1u },
                { "LastUpdateTime", 1715097600u },
                { "Provider", "chainlink" },
                { "AssetClass", "currency" },
                { "PriceDataSeries", new List<object>
                    {
                        new Dictionary<string, dynamic>
                        {
                            { "PriceData", new Dictionary<string, dynamic>
                                {
                                    { "BaseAsset", "XRP" }
                                }
                            }
                        }
                    }
                }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateOracleSet(tx),
                "OracleSet: PriceData must have a QuoteAsset string");
        }

        /// <summary>
        /// Tests that OracleSet with multiple PriceData entries serializes correctly via BinaryCodec.
        /// </summary>
        [TestMethod]
        public void TestVerify_OracleSet_Serialization_MultiplePriceData()
        {
            var oracleSet = Newtonsoft.Json.Linq.JObject.Parse(@"{
                ""TransactionType"": ""OracleSet"",
                ""Account"": ""rME8MrCTc1eCn3cs2HhnzfgJWuJnRNWenK"",
                ""OracleDocumentID"": 12345,
                ""LastUpdateTime"": 1715097600,
                ""Provider"": ""MultiProvider"",
                ""AssetClass"": ""currency"",
                ""Fee"": ""12"",
                ""Sequence"": 1,
                ""PriceDataSeries"": [
                    {
                        ""PriceData"": {
                            ""BaseAsset"": ""BTC"",
                            ""QuoteAsset"": ""USD"",
                            ""AssetPrice"": 65000,
                            ""Scale"": 0
                        }
                    },
                    {
                        ""PriceData"": {
                            ""BaseAsset"": ""XRP"",
                            ""QuoteAsset"": ""USD"",
                            ""AssetPrice"": 740,
                            ""Scale"": 3
                        }
                    }
                ]
            }");

            Console.WriteLine("Input JSON:");
            Console.WriteLine(oracleSet.ToString(Newtonsoft.Json.Formatting.Indented));

            var stObject = global::Xrpl.BinaryCodec.Types.StObject.FromJson(System.Text.Json.Nodes.JsonNode.Parse(oracleSet.ToString()));
            var hex = stObject.ToHex();
            Console.WriteLine($"\nSerialized hex length: {hex.Length}");
            Console.WriteLine($"Serialized hex: {hex}");

            Assert.IsTrue(hex.Length > 0, "Serialization should produce non-empty hex");
            
            // Verify the hex contains BTC encoded in XLS-47 Oracle format (left-aligned bytes 0-2)
            // BTC = 42 54 43 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 (20 bytes)
            Assert.IsTrue(hex.Contains("4254430000000000000000000000000000000000"), 
                "BTC should be encoded in XLS-47 Oracle format (left-aligned bytes 0-2)");
        }

        /// <summary>
        /// Tests that Currency.FromOracleString produces correct XLS-47 left-aligned encoding for Oracle BaseAsset/QuoteAsset.
        /// 3-letter codes use bytes 0-2 (left-aligned), NOT bytes 12-14 like standard IOU currencies.
        /// Non-standard codes (40-hex) use direct bytes.
        /// </summary>
        [TestMethod]
        public void TestVerify_CurrencyOracleEncoding()
        {
            // Standard XRPL encoding: currency code at bytes 12-14
            var btcStandard = BinaryCodecCurrency.FromString("BTC");
            var btcStandardHex = BitConverter.ToString(btcStandard.Buffer).Replace("-", "");
            Console.WriteLine($"BTC Standard (IOU): {btcStandardHex}");
            Assert.AreEqual("0000000000000000000000004254430000000000", btcStandardHex, "Standard encoding should have BTC at bytes 12-14");

            // XLS-47 Oracle encoding: currency code at bytes 0-2 (left-aligned)
            var btcOracle = BinaryCodecCurrency.FromOracleString("BTC");
            var btcOracleHex = BitConverter.ToString(btcOracle.Buffer).Replace("-", "");
            Console.WriteLine($"BTC Oracle (XLS-47): {btcOracleHex}");
            Assert.AreEqual("4254430000000000000000000000000000000000", btcOracleHex, "Oracle encoding should have BTC left-aligned at bytes 0-2");

            // XRP is all zeros in both cases
            var xrpOracle = BinaryCodecCurrency.FromOracleString("XRP");
            var xrpOracleHex = BitConverter.ToString(xrpOracle.Buffer).Replace("-", "");
            Console.WriteLine($"XRP Oracle: {xrpOracleHex}");
            Assert.AreEqual("0000000000000000000000000000000000000000", xrpOracleHex, "XRP should be all zeros");

            // Non-standard currencies (40-hex) use direct bytes
            var fdusdOracle = BinaryCodecCurrency.FromOracleString("4644555344000000000000000000000000000000");
            var fdusdOracleHex = BitConverter.ToString(fdusdOracle.Buffer).Replace("-", "");
            Console.WriteLine($"FDUSD Oracle: {fdusdOracleHex}");
            Assert.AreEqual("4644555344000000000000000000000000000000", fdusdOracleHex, "Non-standard currency should use direct hex bytes");

            // Verify decoding works for both formats
            Assert.AreEqual("BTC", btcStandard.IsoCode, "Standard BTC should decode to BTC");
            Assert.AreEqual("BTC", btcOracle.IsoCode, "Oracle BTC should decode to BTC");
            Assert.AreEqual("XRP", xrpOracle.IsoCode, "Oracle XRP should decode to XRP");
        }

        /// <summary>
        /// Tests that OracleSet JSON serialization produces correct hex format for rippled.
        /// AssetPrice should be hex string, currencies should be 40-char hex, Provider/AssetClass should be hex.
        /// </summary>
        [TestMethod]
        public void TestVerify_OracleSet_JsonSerialization()
        {
            var oracleSet = new OracleSet
            {
                Account = "rME8MrCTc1eCn3cs2HhnzfgJWuJnRNWenK",
                OracleDocumentID = 12345,
                LastUpdateTime = DateTime.UtcNow,
                Provider = "MultiProvider",
                AssetClass = "currency",
                Fee = "12",
                Sequence = 1,
                PriceDataSeries = new List<ModelPriceDataWrapper>
                {
                    new ModelPriceDataWrapper
                    {
                        PriceData = new ModelPriceData
                        {
                            BaseAsset = "BTC",
                            QuoteAsset = "USD",
                            AssetPrice = 65000UL,
                            Scale = 0
                        }
                    },
                    new ModelPriceDataWrapper
                    {
                        PriceData = new ModelPriceData
                        {
                            BaseAsset = "XRP",
                            QuoteAsset = "USD",
                            AssetPrice = 740UL,
                            Scale = 3
                        }
                    }
                }
            };

            var json = JsonConvert.SerializeObject(oracleSet, Formatting.Indented);
            Console.WriteLine("Serialized OracleSet JSON:");
            Console.WriteLine(json);

            // Verify AssetPrice is hex string
            Assert.IsTrue(json.Contains("\"AssetPrice\": \"fde8\""), "AssetPrice 65000 should be serialized as 'fde8'");
            Assert.IsTrue(json.Contains("\"AssetPrice\": \"2e4\""), "AssetPrice 740 should be serialized as '2e4'");

            // Verify 3-char currencies remain as plain strings (XRPL standard rule)
            Assert.IsTrue(json.Contains("\"BaseAsset\": \"BTC\""), "BTC (3 chars) should remain as plain string");
            Assert.IsTrue(json.Contains("\"QuoteAsset\": \"USD\""), "USD (3 chars) should remain as plain string");
            Assert.IsTrue(json.Contains("\"BaseAsset\": \"XRP\""), "XRP (3 chars) should remain as plain string");

            // Verify Provider and AssetClass are hex
            Assert.IsTrue(json.Contains("\"Provider\": \"4d756c746950726f7669646572\""), "Provider should be hex ASCII");
            Assert.IsTrue(json.Contains("\"AssetClass\": \"63757272656e6379\""), "AssetClass should be hex ASCII");
        }

        /// <summary>
        /// Tests that PriceData JSON deserialization correctly reads hex values back to plain text.
        /// </summary>
        [TestMethod]
        public void TestVerify_PriceData_JsonDeserialization()
        {
            // Test with 40-char hex currencies (> 3 chars)
            var json = @"{
                ""PriceData"": {
                    ""BaseAsset"": ""4644555344000000000000000000000000000000"",
                    ""QuoteAsset"": ""USD"",
                    ""AssetPrice"": ""fde8"",
                    ""Scale"": 0
                }
            }";

            var wrapper = JsonConvert.DeserializeObject<ModelPriceDataWrapper>(json);
            
            Assert.AreEqual("FDUSD", wrapper.PriceData.BaseAsset, "BaseAsset FDUSD should be decoded from hex");
            Assert.AreEqual("USD", wrapper.PriceData.QuoteAsset, "QuoteAsset USD should remain as plain string");
            Assert.AreEqual(65000UL, Convert.ToUInt64(wrapper.PriceData.AssetPrice), "AssetPrice should be decoded from hex");
        }

        /// <summary>
        /// Tests that currencies > 3 chars are serialized as 40-char hex.
        /// </summary>
        [TestMethod]
        public void TestVerify_OracleSet_LongCurrencySerialization()
        {
            var oracleSet = new OracleSet
            {
                Account = "rME8MrCTc1eCn3cs2HhnzfgJWuJnRNWenK",
                OracleDocumentID = 12345,
                LastUpdateTime = DateTime.UtcNow,
                Provider = "TestProvider",
                AssetClass = "currency",
                Fee = "12",
                Sequence = 1,
                PriceDataSeries = new List<ModelPriceDataWrapper>
                {
                    new ModelPriceDataWrapper
                    {
                        PriceData = new ModelPriceData
                        {
                            BaseAsset = "FDUSD",  // > 3 chars
                            QuoteAsset = "USD",   // 3 chars
                            AssetPrice = 100UL,
                            Scale = 0
                        }
                    },
                    new ModelPriceDataWrapper
                    {
                        PriceData = new ModelPriceData
                        {
                            BaseAsset = "XRP",    // 3 chars
                            QuoteAsset = "RLUSD", // > 3 chars
                            AssetPrice = 200UL,
                            Scale = 0
                        }
                    }
                }
            };

            var json = JsonConvert.SerializeObject(oracleSet, Formatting.Indented);
            Console.WriteLine("Serialized OracleSet with long currencies:");
            Console.WriteLine(json);

            // Verify > 3 char currencies become 40-char hex (lowercase)
            Assert.IsTrue(json.Contains("4644555344000000000000000000000000000000"), "FDUSD should be 40-char hex");
            Assert.IsTrue(json.Contains("524c555344000000000000000000000000000000"), "RLUSD should be 40-char lowercase hex");

            // Verify 3-char currencies remain as plain strings
            Assert.IsTrue(json.Contains("\"QuoteAsset\": \"USD\""), "USD (3 chars) should remain as plain string");
            Assert.IsTrue(json.Contains("\"BaseAsset\": \"XRP\""), "XRP (3 chars) should remain as plain string");
        }

    }
}
