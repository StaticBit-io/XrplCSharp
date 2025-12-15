using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUMPTokenAuthorize
    {
        public static Dictionary<string, dynamic> mpTokenAuthorize;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            mpTokenAuthorize = new Dictionary<string, dynamic>
            {
                {"TransactionType", "MPTokenAuthorize"},
                {"Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm"},
                {"MPTokenIssuanceID", "00000001A407AF5856CCF3C42619DAA925813FC955C72983"},
                {"Sequence", 1337u},
            };
        }

        [TestMethod]
        public async Task TestVerifyValid()
        {
            await Validation.Validate(mpTokenAuthorize);
        }

        [TestMethod]
        public async Task TestVerifyWithHolder()
        {
            mpTokenAuthorize["Holder"] = "rPyfep3gcLzkosKC9XiE77Y8DZWG6iWDT9";
            await Validation.Validate(mpTokenAuthorize);
            mpTokenAuthorize.Remove("Holder");
        }

        [TestMethod]
        public async Task TestVerifyWithUnauthorizeFlag()
        {
            mpTokenAuthorize["Flags"] = (uint)MPTokenAuthorizeFlags.tfMPTUnauthorize;
            await Validation.Validate(mpTokenAuthorize);
            mpTokenAuthorize.Remove("Flags");
        }

        [TestMethod]
        public async Task TestThrowsWithMissingMPTokenIssuanceID()
        {
            mpTokenAuthorize.Remove("MPTokenIssuanceID");
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(mpTokenAuthorize),
                "MPTokenAuthorize: missing field MPTokenIssuanceID");
            mpTokenAuthorize["MPTokenIssuanceID"] = "00000001A407AF5856CCF3C42619DAA925813FC955C72983";
        }

        [TestMethod]
        public async Task TestThrowsWithInvalidMPTokenIssuanceID()
        {
            mpTokenAuthorize["MPTokenIssuanceID"] = 12345;
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(mpTokenAuthorize),
                "MPTokenAuthorize: MPTokenIssuanceID must be a string");
            mpTokenAuthorize["MPTokenIssuanceID"] = "00000001A407AF5856CCF3C42619DAA925813FC955C72983";
        }

        [TestMethod]
        public async Task TestThrowsWithInvalidHolder()
        {
            mpTokenAuthorize["Holder"] = 12345;
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(mpTokenAuthorize),
                "MPTokenAuthorize: Holder must be a string");
            mpTokenAuthorize.Remove("Holder");
        }
    }
}
