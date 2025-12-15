using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUMPTokenIssuanceDestroy
    {
        public static Dictionary<string, dynamic> mpTokenIssuanceDestroy;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            mpTokenIssuanceDestroy = new Dictionary<string, dynamic>
            {
                {"TransactionType", "MPTokenIssuanceDestroy"},
                {"Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm"},
                {"MPTokenIssuanceID", "00000001A407AF5856CCF3C42619DAA925813FC955C72983"},
                {"Sequence", 1337u},
            };
        }

        [TestMethod]
        public async Task TestVerifyValid()
        {
            await Validation.Validate(mpTokenIssuanceDestroy);
        }

        [TestMethod]
        public async Task TestThrowsWithMissingMPTokenIssuanceID()
        {
            mpTokenIssuanceDestroy.Remove("MPTokenIssuanceID");
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(mpTokenIssuanceDestroy),
                "MPTokenIssuanceDestroy: missing field MPTokenIssuanceID");
            mpTokenIssuanceDestroy["MPTokenIssuanceID"] = "00000001A407AF5856CCF3C42619DAA925813FC955C72983";
        }

        [TestMethod]
        public async Task TestThrowsWithInvalidMPTokenIssuanceID()
        {
            mpTokenIssuanceDestroy["MPTokenIssuanceID"] = 12345;
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(mpTokenIssuanceDestroy),
                "MPTokenIssuanceDestroy: MPTokenIssuanceID must be a string");
            mpTokenIssuanceDestroy["MPTokenIssuanceID"] = "00000001A407AF5856CCF3C42619DAA925813FC955C72983";
        }
    }
}
