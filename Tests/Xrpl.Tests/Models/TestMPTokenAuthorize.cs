using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUMPTokenAuthorize
    {
        public static Dictionary<string, object> mpTokenAuthorize;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            mpTokenAuthorize = new Dictionary<string, object>
            {
                {"TransactionType", "MPTokenAuthorize"},
                {"Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm"},
                {"MPTokenIssuanceID", "00000001A407AF5856CCF3C42619DAA925813FC955C72983"},
                {"Sequence", 1337u},
            };
        }

        [TestMethod]
        public void TestVerifyValid()
        {
            Validation.Validate(mpTokenAuthorize);
        }

        [TestMethod]
        public void TestVerifyWithHolder()
        {
            mpTokenAuthorize["Holder"] = "rPyfep3gcLzkosKC9XiE77Y8DZWG6iWDT9";
            Validation.Validate(mpTokenAuthorize);
            mpTokenAuthorize.Remove("Holder");
        }

        [TestMethod]
        public void TestVerifyWithUnauthorizeFlag()
        {
            mpTokenAuthorize["Flags"] = (uint)MPTokenAuthorizeFlags.tfMPTUnauthorize;
            Validation.Validate(mpTokenAuthorize);
            mpTokenAuthorize.Remove("Flags");
        }

        [TestMethod]
        public void TestThrowsWithMissingMPTokenIssuanceID()
        {
            mpTokenAuthorize.Remove("MPTokenIssuanceID");
            Helper.ThrowsException<ValidationException>(
                () => Validation.Validate(mpTokenAuthorize),
                "MPTokenAuthorize: missing field MPTokenIssuanceID");
            mpTokenAuthorize["MPTokenIssuanceID"] = "00000001A407AF5856CCF3C42619DAA925813FC955C72983";
        }

        [TestMethod]
        public void TestThrowsWithInvalidMPTokenIssuanceID()
        {
            mpTokenAuthorize["MPTokenIssuanceID"] = 12345;
            Helper.ThrowsException<ValidationException>(
                () => Validation.Validate(mpTokenAuthorize),
                "MPTokenAuthorize: MPTokenIssuanceID must be a string");
            mpTokenAuthorize["MPTokenIssuanceID"] = "00000001A407AF5856CCF3C42619DAA925813FC955C72983";
        }

        [TestMethod]
        public void TestThrowsWithInvalidHolder()
        {
            mpTokenAuthorize["Holder"] = 12345;
            Helper.ThrowsException<ValidationException>(
                () => Validation.Validate(mpTokenAuthorize),
                "MPTokenAuthorize: Holder must be a string");
            mpTokenAuthorize.Remove("Holder");
        }
    }
}