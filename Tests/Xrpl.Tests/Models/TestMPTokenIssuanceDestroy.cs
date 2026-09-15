using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUMPTokenIssuanceDestroy
    {
        public static Dictionary<string, object> mpTokenIssuanceDestroy;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            mpTokenIssuanceDestroy = new Dictionary<string, object>
            {
                {"TransactionType", "MPTokenIssuanceDestroy"},
                {"Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm"},
                {"MPTokenIssuanceID", "00000001A407AF5856CCF3C42619DAA925813FC955C72983"},
                {"Sequence", 1337u},
            };
        }

        [TestMethod]
        public void TestVerifyValid()
        {
            Validation.Validate(mpTokenIssuanceDestroy);
        }

        [TestMethod]
        public void TestThrowsWithMissingMPTokenIssuanceID()
        {
            mpTokenIssuanceDestroy.Remove("MPTokenIssuanceID");
            Helper.ThrowsException<ValidationException>(
                () => Validation.Validate(mpTokenIssuanceDestroy),
                "MPTokenIssuanceDestroy: missing field MPTokenIssuanceID");
            mpTokenIssuanceDestroy["MPTokenIssuanceID"] = "00000001A407AF5856CCF3C42619DAA925813FC955C72983";
        }

        [TestMethod]
        public void TestThrowsWithInvalidMPTokenIssuanceID()
        {
            mpTokenIssuanceDestroy["MPTokenIssuanceID"] = 12345;
            Helper.ThrowsException<ValidationException>(
                () => Validation.Validate(mpTokenIssuanceDestroy),
                "MPTokenIssuanceDestroy: MPTokenIssuanceID must be a string");
            mpTokenIssuanceDestroy["MPTokenIssuanceID"] = "00000001A407AF5856CCF3C42619DAA925813FC955C72983";
        }
    }
}