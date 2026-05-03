using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUAMMClawback
    {
        public static Dictionary<string, object> ammClawback;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            ammClawback = new Dictionary<string, object>
            {
                {"TransactionType", "AMMClawback"},
                {"Account", "rp6abvbTbjoce8ZDJkT6snvxTZSYMBCC9S"},
                {"Holder", "rsA2LpzuawewSBQXkiju3YQTMzW13pAAdW"},
                {"Asset", new Dictionary<string,object>(){{"currency","FOO"},{"issuer", "rp6abvbTbjoce8ZDJkT6snvxTZSYMBCC9S"}}},
                {"Asset2", new Dictionary<string,object>(){{"currency","XRP"}}},
                {"Sequence", 1337u},
            };
        }

        [TestMethod]
        public async Task TestVerifyValid()
        {
            await Validation.Validate(ammClawback);
        }

        [TestMethod]
        public async Task TestThrowsMissingHolder()
        {
            var tx = new Dictionary<string, object>(ammClawback);
            tx.Remove("Holder");
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(tx),
                "AMMClawback: missing field Holder");
        }

        [TestMethod]
        public async Task TestThrowsMissingAsset()
        {
            var tx = new Dictionary<string, object>(ammClawback);
            tx.Remove("Asset");
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(tx),
                "AMMClawback: missing field Asset");
        }

        [TestMethod]
        public async Task TestThrowsAssetMustBeIssue()
        {
            var tx = new Dictionary<string, object>(ammClawback);
            tx["Asset"] = 1234;
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(tx),
                "AMMClawback: Asset must be an Issue");
        }

        [TestMethod]
        public async Task TestThrowsMissingAsset2()
        {
            var tx = new Dictionary<string, object>(ammClawback);
            tx.Remove("Asset2");
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(tx),
                "AMMClawback: missing field Asset2");
        }

        [TestMethod]
        public async Task TestThrowsAsset2MustBeIssue()
        {
            var tx = new Dictionary<string, object>(ammClawback);
            tx["Asset2"] = 1234;
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(tx),
                "AMMClawback: Asset2 must be an Issue");
        }

        [TestMethod]
        public async Task TestValidWithOptionalAmount()
        {
            var tx = new Dictionary<string, object>(ammClawback);
            tx["Amount"] = new Dictionary<string, object>()
            {
                {"currency","FOO"},
                {"issuer","rp6abvbTbjoce8ZDJkT6snvxTZSYMBCC9S"},
                {"value","100"},
            };
            await Validation.Validate(tx);
        }

        [TestMethod]
        public async Task TestThrowsInvalidAmountXRP()
        {
            var tx = new Dictionary<string, object>(ammClawback);
            tx["Amount"] = "1000000";
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(tx),
                "AMMClawback: invalid Amount");
        }
    }
}
