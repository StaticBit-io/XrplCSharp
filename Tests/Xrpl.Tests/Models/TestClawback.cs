using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Threading.Tasks;

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
        public async Task TestVerifyValid()
        {
            await Validation.Validate(clawback);
        }

        [TestMethod]
        public async Task TestThrowsMissingAmount()
        {
            var tx = new Dictionary<string, object>(clawback);
            tx.Remove("Amount");
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(tx),
                "ClawBack: missing field Amount");
        }

        [TestMethod]
        public async Task TestThrowsInvalidAmountXRP()
        {
            var tx = new Dictionary<string, object>(clawback);
            tx["Amount"] = "1000000";
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(tx),
                "ClawBack: invalid Amount");
        }

        [TestMethod]
        public async Task TestThrowsHolderSameAsAccount()
        {
            var tx = new Dictionary<string, object>(clawback);
            tx["Amount"] = new Dictionary<string, object>()
            {
                {"currency","FOO"},
                {"issuer","rp6abvbTbjoce8ZDJkT6snvxTZSYMBCC9S"},
                {"value","100"},
            };
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(tx),
                "ClawBack: invalid holder Account");
        }

        [TestMethod]
        public async Task TestValidWithHolderForMPT()
        {
            var tx = new Dictionary<string, object>(clawback);
            tx["Holder"] = "rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW";
            await Validation.Validate(tx);
        }
    }
}
