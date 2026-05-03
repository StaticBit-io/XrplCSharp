

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/models/escrowFinish.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Transaction;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUEscrowFinish
    {
        public static Dictionary<string, dynamic> escrowFinish;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            escrowFinish = new Dictionary<string, dynamic>
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
        public async Task TestVerifyValid()
        {

            //verifies valid EscrowFinish
            await Validation.ValidateEscrowFinish(escrowFinish);
            await Validation.Validate(escrowFinish);

            // verifies valid EscrowFinish w/o optional
            escrowFinish.Remove("Condition");
            escrowFinish.Remove("Fulfillment");
            await Validation.ValidateEscrowFinish(escrowFinish);
            await Validation.Validate(escrowFinish);
            escrowFinish["Condition"] = "A0258020E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855810100";
            escrowFinish["Fulfillment"] = "A0028000";

            // throws w/ invalid Owner
            escrowFinish["Owner"] = 0x15415253;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateEscrowFinish(escrowFinish), "EscrowFinish: Owner must be a string");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(escrowFinish), "EscrowFinish: Owner must be a string");
            escrowFinish["Owner"] = "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn";

            // throws w/ invalid OfferSequence
            escrowFinish["OfferSequence"] = "10";
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateEscrowFinish(escrowFinish), "EscrowFinish: OfferSequence must be a number");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(escrowFinish), "EscrowFinish: OfferSequence must be a number");
            escrowFinish["OfferSequence"] = 7u;

            // Invalid Condition
            escrowFinish["Condition"] = 10;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateEscrowFinish(escrowFinish), "EscrowFinish: Condition must be a string");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(escrowFinish), "EscrowFinish: Condition must be a string");
            escrowFinish["Condition"] = "A0258020E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855810100";

            // Invalid Fulfillment
            escrowFinish["Fulfillment"] = 0x142341;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateEscrowFinish(escrowFinish), "EscrowFinish: Fulfillment must be a string");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(escrowFinish), "EscrowFinish: Fulfillment must be a string");
            escrowFinish["Fulfillment"] = "A0028000";

        }

        [TestMethod]
        public async Task TestVerify_Valid_EscrowFinish_WithCredentialIDs()
        {
            Dictionary<string, dynamic> tx = new Dictionary<string, dynamic>
            {
                { "TransactionType", "EscrowFinish" },
                { "Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "Owner", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "OfferSequence", 7u },
                { "CredentialIDs", new List<object> { "A1B2C3D4E5F6789012345678901234567890ABCDEF1234567890ABCDEF123456" } }
            };
            await Validation.ValidateEscrowFinish(tx);
            await Validation.Validate(tx);
        }

        [TestMethod]
        public async Task TestVerify_Invalid_EscrowFinish_DuplicateCredentialIDs()
        {
            string id = "A1B2C3D4E5F6789012345678901234567890ABCDEF1234567890ABCDEF123456";
            Dictionary<string, dynamic> tx = new Dictionary<string, dynamic>
            {
                { "TransactionType", "EscrowFinish" },
                { "Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "Owner", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn" },
                { "OfferSequence", 7u },
                { "CredentialIDs", new List<object> { id, id.ToLowerInvariant() } }
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.ValidateEscrowFinish(tx),
                "EscrowFinish: CredentialIDs cannot contain duplicate credential IDs");
        }
    }

}

