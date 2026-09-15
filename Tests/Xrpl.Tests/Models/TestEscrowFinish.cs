

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/models/escrowFinish.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Transaction;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUEscrowFinish
    {
        public static Dictionary<string, object> escrowFinish;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            escrowFinish = new Dictionary<string, object>
            {
                {"TransactionType", "EscrowFinish"},
                {"Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn"},
                {"Owner", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn"},
                {"OfferSequence", 7u},
                {"Fulfillment", "A0028000"},
                {"Condition", "A0258020E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855810100"},
            };
        }

        [TestMethod]
        public void TestVerifyValid()
        {

            //verifies valid EscrowFinish
            Validation.ValidateEscrowFinish(escrowFinish);
            Validation.Validate(escrowFinish);

            // verifies valid EscrowFinish w/o optional
            escrowFinish.Remove("Condition");
            escrowFinish.Remove("Fulfillment");
            Validation.ValidateEscrowFinish(escrowFinish);
            Validation.Validate(escrowFinish);
            escrowFinish["Condition"] = "A0258020E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855810100";
            escrowFinish["Fulfillment"] = "A0028000";

            // throws w/ invalid Owner
            escrowFinish["Owner"] = 0x15415253;
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateEscrowFinish(escrowFinish), "EscrowFinish: Owner must be a string");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(escrowFinish), "EscrowFinish: Owner must be a string");
            escrowFinish["Owner"] = "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn";

            // throws w/ invalid OfferSequence
            escrowFinish["OfferSequence"] = "10";
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateEscrowFinish(escrowFinish), "EscrowFinish: OfferSequence must be a number");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(escrowFinish), "EscrowFinish: OfferSequence must be a number");
            escrowFinish["OfferSequence"] = 7u;

            // Invalid Condition
            escrowFinish["Condition"] = 10;
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateEscrowFinish(escrowFinish), "EscrowFinish: Condition must be a string");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(escrowFinish), "EscrowFinish: Condition must be a string");
            escrowFinish["Condition"] = "A0258020E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855810100";

            // Invalid Fulfillment
            escrowFinish["Fulfillment"] = 0x142341;
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateEscrowFinish(escrowFinish), "EscrowFinish: Fulfillment must be a string");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(escrowFinish), "EscrowFinish: Fulfillment must be a string");
            escrowFinish["Fulfillment"] = "A0028000";

        }

        [TestMethod]
        public void TestVerify_Valid_EscrowFinish_WithCredentialIDs()
        {
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                { "TransactionType", "EscrowFinish" },
                { "Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "Owner", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "OfferSequence", 7u },
                { "CredentialIDs", new List<object> { "A1B2C3D4E5F6789012345678901234567890ABCDEF1234567890ABCDEF123456" } }
            };
            Validation.ValidateEscrowFinish(tx);
            Validation.Validate(tx);
        }

        [TestMethod]
        public void TestVerify_Invalid_EscrowFinish_DuplicateCredentialIDs()
        {
            string id = "A1B2C3D4E5F6789012345678901234567890ABCDEF1234567890ABCDEF123456";
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                { "TransactionType", "EscrowFinish" },
                { "Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "Owner", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "OfferSequence", 7u },
                { "CredentialIDs", new List<object> { id, id.ToLowerInvariant() } }
            };
            Helper.ThrowsException<ValidationException>(
                () => Validation.ValidateEscrowFinish(tx),
                "EscrowFinish: CredentialIDs cannot contain duplicate credential IDs");
        }
    }

}

