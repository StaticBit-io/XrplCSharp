

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/models/paymentChannelFund.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Transaction;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUPaymentChannelFund
    {
        public static Dictionary<string, object> channel;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            channel = new Dictionary<string, object>
            {
                {"TransactionType", "PaymentChannelFund"},
                {"Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn"},
                {"Amount", "200000"},
                {"Channel", "C1AE6DDDEEC05CF2978C0BAD6FE302948E9533691DC749DCDD3B9E5992CA6198"},
                {"Expiration", 543171558u},
            };
        }

        [TestMethod]
        public void TestVerifyValid()
        {

            //verifies valid PaymentChannelFund
            Validation.ValidatePaymentChannelFund(channel);
            Validation.Validate(channel);

            // verifies valid PaymentChannelFund w/o optional
            channel.Remove("Expiration");
            Validation.ValidatePaymentChannelFund(channel);
            Validation.Validate(channel);
            channel["Expiration"] = 533171558u;


            // throws w/ missing Amount
            channel.Remove("Amount");
            Helper.ThrowsException<ValidationException>(() => Validation.ValidatePaymentChannelFund(channel), "PaymentChannelFund: missing field Amount");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(channel), "PaymentChannelFund: missing field Amount");
            channel["Amount"] = "200000";

            // throws w/ missing Channel
            channel.Remove("Channel");
            Helper.ThrowsException<ValidationException>(() => Validation.ValidatePaymentChannelFund(channel), "PaymentChannelFund: missing field Channel");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(channel), "PaymentChannelFund: missing field Channel");
            channel["Channel"] = "C1AE6DDDEEC05CF2978C0BAD6FE302948E9533691DC749DCDD3B9E5992CA6198";

            // throws w/ Amount must be a string
            channel["Amount"] = 100;
            Helper.ThrowsException<ValidationException>(() => Validation.ValidatePaymentChannelFund(channel), "PaymentChannelFund: Amount must be a string");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(channel), "PaymentChannelFund: Amount must be a string");
            channel["Amount"] = "1000000";

            // throws w/ Channel must be a string
            channel["Channel"] = 1000;
            Helper.ThrowsException<ValidationException>(() => Validation.ValidatePaymentChannelFund(channel), "PaymentChannelFund: Channel must be a string");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(channel), "PaymentChannelFund: Channel must be a string");
            channel["Channel"] = "C1AE6DDDEEC05CF2978C0BAD6FE302948E9533691DC749DCDD3B9E5992CA6198";

            // throws w/ Expiration must be a string
            channel["Expiration"] = "10";
            Helper.ThrowsException<ValidationException>(() => Validation.ValidatePaymentChannelFund(channel), "PaymentChannelFund: Expiration must be a number");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(channel), "PaymentChannelFund: Expiration must be a number");
            channel["Expiration"] = 543171558u;

        }
    }

}

