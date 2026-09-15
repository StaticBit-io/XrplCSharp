using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUClawback
    {
        public static Dictionary<string, object> clawback;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            clawback = new Dictionary<string, object>
            {
                {"TransactionType", "Clawback"},
                {"Account", "rp6abvbTbjoce8ZDJkT6snvxTZSYMBCC9S"},
                {"Amount", new Dictionary<string,object>()
                {
                    {"currency","FOO"},
                    {"issuer","rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW"},
                    {"value","314.159"},
                }},
                {"Sequence", 1337u},
            };
        }

        [TestMethod]
        public void TestVerifyValid()
        {
            Validation.Validate(clawback);
        }

        [TestMethod]
        public void TestThrowsMissingAmount()
        {
            var tx = new Dictionary<string, object>(clawback);
            tx.Remove("Amount");
            Helper.ThrowsException<ValidationException>(
                () => Validation.Validate(tx),
                "ClawBack: missing field Amount");
        }

        [TestMethod]
        public void TestThrowsInvalidAmountXRP()
        {
            var tx = new Dictionary<string, object>(clawback);
            tx["Amount"] = "1000000";
            Helper.ThrowsException<ValidationException>(
                () => Validation.Validate(tx),
                "ClawBack: invalid Amount");
        }

        [TestMethod]
        public void TestThrowsHolderSameAsAccount()
        {
            var tx = new Dictionary<string, object>(clawback);
            tx["Amount"] = new Dictionary<string, object>()
            {
                {"currency","FOO"},
                {"issuer","rp6abvbTbjoce8ZDJkT6snvxTZSYMBCC9S"},
                {"value","100"},
            };
            Helper.ThrowsException<ValidationException>(
                () => Validation.Validate(tx),
                "ClawBack: invalid holder Account");
        }

        [TestMethod]
        public void TestValidWithHolderForMPT()
        {
            var tx = new Dictionary<string, object>(clawback);
            tx["Holder"] = "rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW";
            Validation.Validate(tx);
        }
    }
}
