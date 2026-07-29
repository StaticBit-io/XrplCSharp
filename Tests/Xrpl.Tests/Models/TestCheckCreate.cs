

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/models/checkCreate.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Transaction;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUCheckCreate
    {
        [TestMethod]
        public async Task TestVerify_Valid_CheckCreate()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "CheckCreate" },
                {"Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo"},
                {"Destination", "rfkE1aSy9G8Upk4JssnwBxhEv5p4mn2KTy"},
                {"SendMax", "100000000"},
                {"Expiration", 570113521u},
                {"InvoiceID", "6F1DFD1D0FE8A32E40E1F2C05CF1C15545BAB56B617F9C6C2D63A6B704BEF59B"},
                {"DestinationTag", 1u},
                {"Fee", "12"},
            };
            await Validation.ValidateCheckCreate(tx);
            await Validation.Validate(tx);
        }
        [TestMethod]
        public async Task TestVerify_InValid_Destination()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "CheckCreate" },
                {"Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo"},
                {"Destination", 7896214789632154},
                {"SendMax", "100000000"}, //todo in tests must be Issued Currency
                {"Expiration", 570113521u},
                {"InvoiceID", "6F1DFD1D0FE8A32E40E1F2C05CF1C15545BAB56B617F9C6C2D63A6B704BEF59B"},
                {"DestinationTag", 1u},
                {"Fee", "12"},
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateCheckCreate(tx), "CheckCreate: invalid Destination");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(tx), "CheckCreate: invalid Destination");
        }
        [TestMethod]
        public async Task TestVerify_InValid_SendMax()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "CheckCreate" },
                {"Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo"},
                {"Destination", "rfkE1aSy9G8Upk4JssnwBxhEv5p4mn2KTy"},
                {"SendMax", 100000000},
                {"Expiration", 570113521u},
                {"InvoiceID", "6F1DFD1D0FE8A32E40E1F2C05CF1C15545BAB56B617F9C6C2D63A6B704BEF59B"},
                {"DestinationTag", 1u},
                {"Fee", "12"},
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateCheckCreate(tx), "CheckCreate: invalid SendMax");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(tx), "CheckCreate: invalid SendMax");
        }
        [TestMethod]
        public async Task TestVerify_InValid_DestinationTag()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "CheckCreate" },
                {"Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo"},
                {"Destination", "rfkE1aSy9G8Upk4JssnwBxhEv5p4mn2KTy"},
                {"SendMax", "100000000"}, //todo in tests must be Issued Currency
                {"Expiration", 570113521u},
                {"InvoiceID", "6F1DFD1D0FE8A32E40E1F2C05CF1C15545BAB56B617F9C6C2D63A6B704BEF59B"},
                {"DestinationTag", "1"},
                {"Fee", "12"},
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateCheckCreate(tx), "CheckCreate: invalid DestinationTag");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(tx), "CheckCreate: invalid DestinationTag");
        }
        [TestMethod]
        public async Task TestVerify_InValid_Expiration()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "CheckCreate" },
                {"Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo"},
                {"Destination", "rfkE1aSy9G8Upk4JssnwBxhEv5p4mn2KTy"},
                {"SendMax", "100000000"}, //todo in tests must be Issued Currency
                {"Expiration", "570113521"},
                {"InvoiceID", "6F1DFD1D0FE8A32E40E1F2C05CF1C15545BAB56B617F9C6C2D63A6B704BEF59B"},
                {"DestinationTag", 1u},
                {"Fee", "12"},
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateCheckCreate(tx), "CheckCreate: invalid Expiration");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(tx), "CheckCreate: invalid Expiration");
        }
        [TestMethod]
        public async Task TestVerify_InValid_InvoiceID()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "CheckCreate" },
                {"Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo"},
                {"Destination", "rfkE1aSy9G8Upk4JssnwBxhEv5p4mn2KTy"},
                {"SendMax", "100000000"}, //todo in tests must be Issued Currency
                {"Expiration", 570113521u},
                {"InvoiceID", 789656963258531},
                {"DestinationTag", 1u},
                {"Fee", "12"},
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateCheckCreate(tx), "CheckCreate: invalid InvoiceID");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(tx), "CheckCreate: invalid InvoiceID");
        }

        [TestMethod]
        public void TestUCheckCreate_InvoiceIDIsAHash256()
        {
            // sfInvoiceID is Hash256, and ValidateCheckCreate above already demands a string.
            // The typed property was uint?, so every non-null value threw on signing:
            // "Can't decode `InvoiceID` from `123`".
            const string invoiceId = "6F1DFD1D0FE8A32E40E1F2C05CF1C15545BAB56B617F9C6C2D63A6B704BEF59B";
            CheckCreate tx = new CheckCreate
            {
                Account = "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo",
                Destination = "rfkE1aSy9G8Upk4JssnwBxhEv5p4mn2KTy",
                SendMax = new global::Xrpl.Models.Common.Currency { ValueAsXrp = 50 },
                InvoiceID = invoiceId,
                Sequence = 1,
                Fee = new global::Xrpl.Models.Common.Currency { Value = "12" },
                SigningPublicKey = "",
            };

            System.Text.Json.Nodes.JsonObject json = System.Text.Json.Nodes.JsonNode.Parse(tx.ToJson()).AsObject();
            System.Text.Json.Nodes.JsonObject decoded = global::Xrpl.BinaryCodec.XrplBinaryCodec
                .Decode(global::Xrpl.BinaryCodec.XrplBinaryCodec.Encode(json)).AsObject();

            Assert.AreEqual(invoiceId, decoded["InvoiceID"].GetValue<string>());
        }

        [TestMethod]
        public async Task TestUCheckCreate_RejectsInvoiceIDThatIsNotA256BitHexValue()
        {
            // sfInvoiceID is Hash256. A string of the wrong length or with non-hex characters is
            // malformed and must fail validation rather than blow up later inside the codec —
            // the same rule SignerListSet and AccountSet already apply to WalletLocator.
            string[] malformed =
            {
                "6F1DFD1D0FE8A32E40E1F2C05CF1C15545BAB56B617F9C6C2D63A6B704BEF59",   // 63 chars
                "6F1DFD1D0FE8A32E40E1F2C05CF1C15545BAB56B617F9C6C2D63A6B704BEF59BA", // 65 chars
                "6F1DFD1D0FE8A32E40E1F2C05CF1C15545BAB56B617F9C6C2D63A6B704BEF5XZ",  // non-hex
                "",
            };

            foreach (string invoiceId in malformed)
            {
                Dictionary<string, object> tx = new Dictionary<string, object>
                {
                    { "TransactionType", "CheckCreate" },
                    { "Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo" },
                    { "Destination", "rfkE1aSy9G8Upk4JssnwBxhEv5p4mn2KTy" },
                    { "SendMax", "100000000" },
                    { "InvoiceID", invoiceId },
                    { "Fee", "12" },
                };

                await Helper.ThrowsExceptionAsync<ValidationException>(
                    () => Validation.ValidateCheckCreate(tx),
                    "CheckCreate: invalid InvoiceID");
            }
        }

        [TestMethod]
        public async Task TestUCheckCreate_AcceptsA256BitHexInvoiceID()
        {
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                { "TransactionType", "CheckCreate" },
                { "Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo" },
                { "Destination", "rfkE1aSy9G8Upk4JssnwBxhEv5p4mn2KTy" },
                { "SendMax", "100000000" },
                { "InvoiceID", "6f1dfd1d0fe8a32e40e1f2c05cf1c15545bab56b617f9c6c2d63a6b704bef59b" },
                { "Fee", "12" },
            };

            await Validation.ValidateCheckCreate(tx);
        }
    }

}

