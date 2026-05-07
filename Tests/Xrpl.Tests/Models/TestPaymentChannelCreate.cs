

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/models/paymentChannelCreate.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Transaction;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUPaymentChannelCreate
    {
        public static Dictionary<string, object> channel;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            channel = new Dictionary<string, object>
            {
                {"TransactionType", "PaymentChannelCreate"},
                {"Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn"},
                {"Amount", "1000000"},
                {"Destination", "rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW"},
                {"SettleDelay", 86400u},
                {"PublicKey", "32D2471DB72B27E3310F355BB33E339BF26F8392D5A93D3BC0FC3B566612DA0F0A"},
                {"CancelAfter", 533171558u},
                {"DestinationTag", 23480u},
                {"SourceTag", 11747u},
            };
        }

        [TestMethod]
        public async Task TestVerifyValid()
        {

            //verifies valid PaymentChannelCreate
            await Validation.ValidatePaymentChannelCreate(channel);
            await Validation.Validate(channel);

            // verifies valid PaymentChannelCreate w/o optional
            channel.Remove("CancelAfter");
            channel.Remove("DestinationTag");
            channel.Remove("SourceTag");
            await Validation.ValidatePaymentChannelCreate(channel);
            await Validation.Validate(channel);
            channel["CancelAfter"] = 533171558u;
            channel["DestinationTag"] = 23480u;
            channel["SourceTag"] = 11747u;


            // throws w/ missing Amount
            channel.Remove("Amount");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidatePaymentChannelCreate(channel), "PaymentChannelCreate: missing field Amount");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(channel), "PaymentChannelCreate: missing field Amount");
            channel["Amount"] = "1000000";

            // throws w/ missing Destination
            channel.Remove("Destination");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidatePaymentChannelCreate(channel), "PaymentChannelCreate: missing field Destination");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(channel), "PaymentChannelCreate: missing field Destination");
            channel["Destination"] = "rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW";

            // throws w/ SettleDelay must be a number
            channel.Remove("SettleDelay");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidatePaymentChannelCreate(channel), "PaymentChannelCreate: missing field SettleDelay");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(channel), "PaymentChannelCreate: missing field SettleDelay");
            channel["SettleDelay"] = 86400u;

            // throws w/ missing PublicKey
            channel.Remove("PublicKey");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidatePaymentChannelCreate(channel), "PaymentChannelCreate: missing field PublicKey");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(channel), "PaymentChannelCreate: missing field PublicKey");
            channel["PublicKey"] = "32D2471DB72B27E3310F355BB33E339BF26F8392D5A93D3BC0FC3B566612DA0F0A";

            // throws w/ Amount must be a string
            channel["Amount"] = 1000000;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidatePaymentChannelCreate(channel), "PaymentChannelCreate: Amount must be a string");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(channel), "PaymentChannelCreate: Amount must be a string");
            channel["Amount"] = "1000000";

            // throws w/ Destination must be a string
            channel["Destination"] = 10;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidatePaymentChannelCreate(channel), "PaymentChannelCreate: Destination must be a string");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(channel), "PaymentChannelCreate: Destination must be a string");
            channel["Destination"] = "rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW";

            // throws w/ SettleDelay must be a string
            channel["SettleDelay"] = "10";
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidatePaymentChannelCreate(channel), "PaymentChannelCreate: SettleDelay must be a number");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(channel), "PaymentChannelCreate: SettleDelay must be a number");
            channel["SettleDelay"] = 86400u;

            // throws w/ PublicKey must be a string
            channel["PublicKey"] = 10;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidatePaymentChannelCreate(channel), "PaymentChannelCreate: PublicKey must be a string");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(channel), "PaymentChannelCreate: PublicKey must be a string");
            channel["PublicKey"] = "32D2471DB72B27E3310F355BB33E339BF26F8392D5A93D3BC0FC3B566612DA0F0A";

            // throws w/ DestinationTag must be a number
            channel["DestinationTag"] = 10;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidatePaymentChannelCreate(channel), "PaymentChannelCreate: DestinationTag must be a number");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(channel), "PaymentChannelCreate: DestinationTag must be a number");
            channel["DestinationTag"] = 23480u;

            // throws w/ CancelAfter must be a number
            channel["CancelAfter"] = 10;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidatePaymentChannelCreate(channel), "PaymentChannelCreate: CancelAfter must be a number");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(channel), "PaymentChannelCreate: CancelAfter must be a number");
            channel["CancelAfter"] = 11747u;


        }
    }

}

