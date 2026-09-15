

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/models/setRegularKey.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Transaction;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUSetRegularKey
    {
        public static Dictionary<string, object> account;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            account = new Dictionary<string, object>
            {
                {"TransactionType", "SetRegularKey"},
                {"Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn"},
                {"Fee", "12"},
                {"Flags", 0},
                {"RegularKey", "rAR8rR8sUkBoCZFawhkWzY4Y5YoyuznwD"},
            };
        }

        [TestMethod]
        public void TestVerifyValid()
        {
            //verifies valid SetRegularKey
            Validation.ValidateSetRegularKey(account);
            Validation.Validate(account);

            // verifies w/o SetRegularKey
            account.Remove("SetRegularKey");
            Validation.ValidateSetRegularKey(account);
            Validation.Validate(account);
            account["SetRegularKey"] = "rAR8rR8sUkBoCZFawhkWzY4Y5YoyuznwD";


            // throws w/ invalid RegularKey
            account["RegularKey"] = 12369846963;
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateSetRegularKey(account), "SetRegularKey: RegularKey must be a string");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(account), "SetRegularKey: RegularKey must be a string");
            account["RegularKey"] = "rAR8rR8sUkBoCZFawhkWzY4Y5YoyuznwD";
        }
    }

}

