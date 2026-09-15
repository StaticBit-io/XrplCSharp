

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/models/escrowCancel.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Transaction;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUEscrowCancel
    {
        public static Dictionary<string, object> depositPreauth;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            depositPreauth = new Dictionary<string, object>
            {
                {"TransactionType", "EscrowCancel"},
                {"Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn"},
                {"Owner", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn"},
                {"OfferSequence", 7u},
            };
        }

        [TestMethod]
        public void TestVerifyValid()
        {

            //verifies valid EscrowCancel
            Validation.ValidateEscrowCancel(depositPreauth);
            Validation.Validate(depositPreauth);

            // valid EscrowCancel missing owner
            depositPreauth.Remove("Owner");
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateEscrowCancel(depositPreauth), "EscrowCancel: missing Owner");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(depositPreauth), "EscrowCancel: missing Owner");
            depositPreauth["Owner"] = "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn";

            // valid EscrowCancel missing OfferSequence
            depositPreauth.Remove("OfferSequence");
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateEscrowCancel(depositPreauth), "EscrowCancel: missing OfferSequence");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(depositPreauth), "EscrowCancel: missing OfferSequence");
            depositPreauth["OfferSequence"] = 7u;

            // Invalid owner
            depositPreauth["Owner"] = 10;
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateEscrowCancel(depositPreauth), "EscrowCancel: Owner must be a string");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(depositPreauth), "EscrowCancel: Owner must be a string");
            depositPreauth["Owner"] = "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn";

            // Invalid OfferSequence
            depositPreauth["OfferSequence"] = "10";
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateEscrowCancel(depositPreauth), "EscrowCancel: OfferSequence must be a number");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(depositPreauth), "EscrowCancel: OfferSequence must be a number");
            depositPreauth["OfferSequence"] = 7u;
        }
    }

}

