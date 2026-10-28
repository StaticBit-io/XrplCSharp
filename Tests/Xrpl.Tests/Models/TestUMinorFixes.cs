using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.AddressCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Common;
using Xrpl.Models.Enums;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Models.Utils;
using Xrpl.Sugar;
using Xrpl.Utils;
using Xrpl.Utils.Hashes;
using Xrpl.Wallet;

namespace XrplTests.MinorFixes
{
    [TestClass]
    public class TestUMinorFixes
    {
        private const string Alice = "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo";
        private const string Bob = "r9LqNeG6qHxjeUocjvVki2XR35weJ9mZgQ";

        // Currency

        [TestMethod]
        public void Currency_MptValueAsNumber_WritesIntegersExactly()
        {
            Currency mpt = new Currency { MPTokenIssuanceID = "00000001B5E1A9A0C3A4C9F0A3A1B2C3D4E5F60718293A4B" };

            mpt.ValueAsNumber = 10_000_000_000_000_000m;
            Assert.AreEqual("10000000000000000", mpt.Value);

            mpt.ValueAsNumber = long.MaxValue;
            Assert.AreEqual("9223372036854775807", mpt.Value);
        }

        [TestMethod]
        public void Currency_MptValueAsNumber_Fractional_Throws()
        {
            Currency mpt = new Currency { MPTokenIssuanceID = "00000001B5E1A9A0C3A4C9F0A3A1B2C3D4E5F60718293A4B" };
            Assert.ThrowsExactly<ArgumentException>(() => mpt.ValueAsNumber = 1.5m);
        }

        [TestMethod]
        public void Currency_ValueAsXrp_FractionalDrops_Throws()
        {
            Currency xrp = new Currency();
            xrp.ValueAsXrp = 1.5m;
            Assert.AreEqual("1500000", xrp.Value);
            Assert.ThrowsExactly<ArgumentException>(() => xrp.ValueAsXrp = 0.0000015m);
        }

        [TestMethod]
        public void Currency_IsNFT14D_NullCurrencyCode_IsFalse()
        {
            Assert.IsFalse(new Currency().IsNFT14D());
            Assert.IsTrue(new Currency { CurrencyCode = "0200000000000000000000000000000000000000" }.IsNFT14D());
        }

        // ServerFeatures

        [TestMethod]
        public void ServerFeaturesRequest_SerializesLowercaseFeature()
        {
            string json = JsonSerializer.Serialize(new ServerFeaturesRequest { Feature = "Batch" }, XrplJsonOptions.Default);
            Assert.IsTrue(json.Contains("\"feature\":\"Batch\""), json);
        }

        // Hashes

        [TestMethod]
        public void Hashes_LedgerSpaceHex_ByName_MatchesEnumOverload()
        {
            Assert.AreEqual("0061", Hashes.LedgerSpaceHex("Account"));
            Assert.AreEqual(LedgerSpace.Offer.LedgerSpaceHex(), Hashes.LedgerSpaceHex("Offer"));
            Assert.ThrowsExactly<ArgumentException>(() => Hashes.LedgerSpaceHex("NoSuchSpace"));
        }

        // BatchNormalizer

        [TestMethod]
        public void BatchNormalizer_CodeBuiltFlags_AreOredWithInnerBatchFlag()
        {
            foreach (JsonNode flags in new JsonNode[] { JsonValue.Create(0x00010000u), JsonValue.Create(0x00010000), JsonValue.Create(0x00010000L) })
            {
                JsonObject inner = new JsonObject
                {
                    ["TransactionType"] = "Payment",
                    ["Account"] = Alice,
                    ["Flags"] = flags,
                };

                JsonObject normalized = inner.NormalizeInnerTransaction();
                Assert.AreEqual(0x40010000u, (uint)normalized["Flags"]!);
            }
        }

        // LedgerSequenceHelper

        [TestMethod]
        public void LedgerSequenceHelper_ObjectFallback_ReadsFieldWithoutParsingHex()
        {
            Assert.AreEqual(123u, LedgerSequenceHelper.GetLastLedgerSequence(new { LastLedgerSequence = 123u }));
            Assert.IsNull(LedgerSequenceHelper.GetLastLedgerSequence(new { Account = Alice }));
        }

        // Submit

        [TestMethod]
        public void SubmitSugar_IsSigned_TypedTransaction_ReadsFields()
        {
            Payment unsigned = new Payment { Account = Alice, Destination = Bob, Amount = new Currency { ValueAsXrp = 1m } };
            Payment signed = new Payment { Account = Alice, Destination = Bob, Amount = new Currency { ValueAsXrp = 1m }, SigningPublicKey = "ED" + new string('A', 64) };

            Assert.IsFalse(SubmitSugar.IsSigned(unsigned));
            Assert.IsTrue(SubmitSugar.IsSigned(signed));
        }

        // Payment

        [TestMethod]
        public void Payment_Paths_AcceptsListOfObjectShapes()
        {
            Dictionary<string, object> step = new Dictionary<string, object> { ["currency"] = "USD", ["issuer"] = Bob };

            Assert.IsTrue(Validation.IsPathSet(new List<object> { new List<object> { step } }));
            Assert.IsTrue(Validation.IsPathSet(new List<List<Dictionary<string, object>>> { new List<Dictionary<string, object>> { step } }));
            Assert.IsFalse(Validation.IsPathSet("paths"));
            Assert.IsFalse(Validation.IsPathSet(new List<object> { "step" }));
            Assert.IsFalse(Validation.IsPathSet(new List<object> { new List<object>() }));
        }

        [TestMethod]
        public void Payment_Validate_PathsFromJson_AreAccepted()
        {
            string json = "{\"TransactionType\":\"Payment\",\"Account\":\"" + Alice + "\",\"Destination\":\"" + Bob + "\",\"Amount\":\"1000\",\"Fee\":\"12\",\"Sequence\":1," +
                          "\"Paths\":[[{\"currency\":\"USD\",\"issuer\":\"" + Bob + "\"}]]}";
            Dictionary<string, object> tx = JsonSerializer.Deserialize<Dictionary<string, object>>(json, XrplJsonOptions.Default)!;
            Validation.ValidatePayment(tx);

            tx["Paths"] = new List<object> { "bad" };
            Assert.ThrowsExactly<ValidationException>(() => Validation.ValidatePayment(tx));

            tx["Paths"] = null!;
            Validation.ValidatePayment(tx);
        }

        // CrossChainPayment

        [TestMethod]
        public void CreateCrossChainPayment_KeepsFieldsOfTheSource()
        {
            Payment source = new Payment
            {
                Account = Alice,
                Destination = Bob,
                Amount = new Currency { ValueAsXrp = 1m },
                SourceTag = 77,
                DestinationTag = 5,
                Sequence = 9,
                TransactionSignature = "AABB",
            };

            Payment result = source.CreateCrossChainPayment("rDestOnSidechain");

            Assert.AreEqual(77u, result.SourceTag);
            Assert.AreEqual(5u, result.DestinationTag);
            Assert.AreEqual(9u, result.Sequence);
            Assert.AreEqual(Bob, result.Destination);
            Assert.IsNull(result.TransactionSignature);
            Assert.AreEqual(1, result.Memos.Count);
            Assert.AreEqual("AABB", source.TransactionSignature);
        }

        // DIDSet

        [TestMethod]
        public void DIDSet_EmptyStringCountsAsPresent()
        {
            Dictionary<string, object> tx = Did(("Data", ""), ("URI", "AB"));
            Validation.ValidateDIDSet(tx);

            Validation.ValidateDIDSet(Did(("Data", "")));
        }

        [TestMethod]
        public void DIDSet_AllAbsent_OrAllEmpty_Throws()
        {
            Assert.ThrowsExactly<ValidationException>(() => Validation.ValidateDIDSet(Did()));
            Assert.ThrowsExactly<ValidationException>(() => Validation.ValidateDIDSet(Did(("Data", ""), ("DIDDocument", ""), ("URI", ""))));
        }

        private static Dictionary<string, object> Did(params (string Key, string Value)[] fields)
        {
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                ["Account"] = Alice,
                ["TransactionType"] = "DIDSet",
                ["Fee"] = "12",
                ["Sequence"] = 1u,
            };
            foreach ((string key, string value) in fields)
            {
                tx[key] = value;
            }

            return tx;
        }

        // NFToken account comparison

        [TestMethod]
        public void NFTokenMint_IssuerEqualToAccount_ComparedByValue()
        {
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                ["TransactionType"] = "NFTokenMint",
                ["Account"] = new string(Alice.ToCharArray()),
                ["Issuer"] = new string(Alice.ToCharArray()),
                ["NFTokenTaxon"] = 0u,
                ["Fee"] = "12",
                ["Sequence"] = 1u,
            };

            ValidationException ex = Assert.ThrowsExactly<ValidationException>(() => Validation.ValidateNFTokenMint(tx));
            StringAssert.Contains(ex.Message, "Issuer must not be equal to Account");
        }

        [TestMethod]
        public void NFTokenCreateOffer_OwnerAndDestinationEqualToAccount_ComparedByValue()
        {
            Dictionary<string, object> ownerEqual = new Dictionary<string, object>
            {
                ["TransactionType"] = "NFTokenCreateOffer",
                ["Account"] = new string(Alice.ToCharArray()),
                ["Owner"] = new string(Alice.ToCharArray()),
                ["NFTokenID"] = new string('0', 64),
                ["Amount"] = "1",
                ["Fee"] = "12",
                ["Sequence"] = 1u,
            };
            StringAssert.Contains(
                Assert.ThrowsExactly<ValidationException>(() => Validation.ValidateNFTokenCreateOffer(ownerEqual)).Message,
                "Owner and Account must not be equal");

            Dictionary<string, object> destinationEqual = new Dictionary<string, object>(ownerEqual);
            destinationEqual.Remove("Owner");
            destinationEqual["Destination"] = new string(Alice.ToCharArray());
            StringAssert.Contains(
                Assert.ThrowsExactly<ValidationException>(() => Validation.ValidateNFTokenCreateOffer(destinationEqual)).Message,
                "Destination and Account must not be equal");
        }

        // BatchUtils

        [TestMethod]
        public void BatchUtils_ToBatchTx_DoesNotMutateTheCallersTransaction()
        {
            Payment payment = new Payment
            {
                Account = Alice,
                Destination = Bob,
                Amount = new Currency { ValueAsXrp = 1m },
                Fee = new Currency { Value = "12" },
                LastLedgerSequence = 500,
                TransactionSignature = "AABB",
                SigningPublicKey = "CC",
            };

            RawTransactionWrapper wrapper = payment.ToBatchTx();
            ITransactionRequest inner = wrapper.RawTransaction;

            Assert.AreNotSame(payment, inner);
            Assert.AreEqual("0", inner.Fee.Value);
            Assert.IsNull(inner.TransactionSignature);
            Assert.IsNull(inner.LastLedgerSequence);
            Assert.AreEqual((uint)XrplGlobalFlags.tfInnerBatchTxn, inner.Flags);

            Assert.AreEqual("12", payment.Fee.Value);
            Assert.AreEqual("AABB", payment.TransactionSignature);
            Assert.AreEqual("CC", payment.SigningPublicKey);
            Assert.AreEqual(500u, payment.LastLedgerSequence);
            Assert.IsNull(payment.Flags);
        }

        // MPTokenMetadataSchema

        [TestMethod]
        public void MPTokenMetadataSchema_NonAscii_IsWrittenAsUtf8()
        {
            MPTokenMetadataSchema schema = new MPTokenMetadataSchema { Ticker = "TST", Name = "Ünïcode ₿ токен" };

            string json = schema.ToJson();

            Assert.IsTrue(json.Contains("Ünïcode ₿ токен"), json);
            Assert.IsFalse(json.Contains("\\u"), json);
            Assert.AreEqual(Encoding.UTF8.GetByteCount(json), schema.GetByteSize());
        }

        // Autofill

        [TestMethod]
        public void ValidateAccountAddress_XAddressTag_ComparedAcrossIntegralTypes()
        {
            string xAddress = XrplAddressCodec.ClassicAddressToXAddress(Alice, 123, false);

            foreach (object tag in new object[] { 123u, 123, 123L, 123UL })
            {
                Dictionary<string, object> tx = new Dictionary<string, object> { ["Account"] = xAddress, ["SourceTag"] = tag };
                tx.ValidateAccountAddress("Account", "SourceTag");
                Assert.AreEqual(Alice, tx["Account"]);
            }

            Dictionary<string, object> mismatch = new Dictionary<string, object> { ["Account"] = xAddress, ["SourceTag"] = 124L };
            Assert.ThrowsExactly<ValidationException>(() => mismatch.ValidateAccountAddress("Account", "SourceTag"));
        }

        // Fee

        [TestMethod]
        public void Fee_DropsAndLevels_ReadValuesAboveUInt32()
        {
            Drops drops = JsonSerializer.Deserialize<Drops>(
                "{\"base_fee\":\"5000000000\",\"median_fee\":\"1\",\"minimum_fee\":\"2\",\"open_ledger_fee\":\"6000000000\"}", XrplJsonOptions.Default)!;
            Assert.AreEqual(5_000_000_000UL, drops.BaseFee);
            Assert.AreEqual(6_000_000_000UL, drops.OpenLedgerFee);

            Levels levels = JsonSerializer.Deserialize<Levels>(
                "{\"median_level\":\"128000\",\"minimum_level\":\"256\",\"open_ledger_level\":\"9999999999\",\"reference_level\":\"256\"}", XrplJsonOptions.Default)!;
            Assert.AreEqual(9_999_999_999UL, levels.OpenLedgerLevel);
        }

        // AccountLines

        [TestMethod]
        public void TrustLine_BalanceAsNumber_LargeBalance_IsNotAnOverflowException()
        {
            Assert.AreEqual(-12.5m, new TrustLine { Balance = "-12.5" }.BalanceAsNumber);
            Assert.AreEqual(1.5m, new TrustLine { Balance = "1.5e0" }.BalanceAsNumber);
            Assert.ThrowsExactly<AmountOutOfRangeException>(() => new TrustLine { Balance = "1e40" }.BalanceAsNumber);
        }

        // Xumm

        [TestMethod]
        public void Xumm_CheckXummSum_RequiresSixAsciiDigitsAndValueInRange()
        {
            Assert.IsTrue(XummExtension.CheckXummSum(0, "123401"));
            Assert.IsTrue(XummExtension.CheckXummSum(0, "655356"));
            Assert.IsFalse(XummExtension.CheckXummSum(0, "700007"));
            Assert.IsFalse(XummExtension.CheckXummSum(0, "+12341"));
            Assert.IsFalse(XummExtension.CheckXummSum(0, " 12341"));
        }

        // UnixDateTimeConverter

        [TestMethod]
        public void UnixDateTimeConverter_Local_IsConvertedToUtc_AndUnspecifiedIsUtc()
        {
            JsonSerializerOptions options = new JsonSerializerOptions { Converters = { new UnixDateTimeConverter() } };

            DateTime local = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Local);
            Assert.AreEqual(new DateTimeOffset(local).ToUnixTimeSeconds().ToString(), JsonSerializer.Serialize<DateTime?>(local, options));

            DateTime unspecified = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Unspecified);
            Assert.AreEqual(
                new DateTimeOffset(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString(),
                JsonSerializer.Serialize<DateTime?>(unspecified, options));
        }

        [TestMethod]
        public void BatchNormalizer_FloatingPointFlags_AreRead()
        {
            JsonObject inner = new JsonObject
            {
                ["TransactionType"] = "Payment",
                ["Account"] = Alice,
                ["Flags"] = JsonValue.Create(131072d),
            };

            Assert.AreEqual(0x40020000u, (uint)inner.NormalizeInnerTransaction()["Flags"]!);
        }

        [TestMethod]
        public void BatchNormalizer_FractionalFlags_AreRefused()
        {
            JsonObject inner = new JsonObject
            {
                ["TransactionType"] = "Payment",
                ["Account"] = Alice,
                ["Flags"] = JsonValue.Create(1.5d),
            };

            Assert.ThrowsExactly<ValidationException>(() => inner.NormalizeInnerTransaction());
        }

        [TestMethod]
        public void BatchUtils_ToBatchTx_KeepsClearedFlagsCleared()
        {
            // TrustSet's constructor sets tfSetNoRipple; a copy must not bring it back.
            TrustSet trustSet = new TrustSet { Account = Alice, Flags = null };

            ITransactionRequest inner = trustSet.ToBatchTx().RawTransaction;

            Assert.AreEqual((uint)XrplGlobalFlags.tfInnerBatchTxn, inner.Flags);
            Assert.IsNull(trustSet.Flags);
        }
    }
}
