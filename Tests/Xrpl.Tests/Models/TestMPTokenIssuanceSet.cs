using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUMPTokenIssuanceSet
    {
        public static Dictionary<string, dynamic> mpTokenIssuanceSet;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            mpTokenIssuanceSet = new Dictionary<string, dynamic>
            {
                {"TransactionType", "MPTokenIssuanceSet"},
                {"Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm"},
                {"MPTokenIssuanceID", "00000001A407AF5856CCF3C42619DAA925813FC955C72983"},
                {"Sequence", 1337u},
            };
        }

        [TestMethod]
        public async Task TestVerifyValid()
        {
            await Validation.Validate(mpTokenIssuanceSet);
        }

        [TestMethod]
        public async Task TestVerifyWithHolder()
        {
            mpTokenIssuanceSet["Holder"] = "rPyfep3gcLzkosKC9XiE77Y8DZWG6iWDT9";
            await Validation.Validate(mpTokenIssuanceSet);
            mpTokenIssuanceSet.Remove("Holder");
        }

        [TestMethod]
        public async Task TestThrowsWithMissingMPTokenIssuanceID()
        {
            mpTokenIssuanceSet.Remove("MPTokenIssuanceID");
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(mpTokenIssuanceSet),
                "MPTokenIssuanceSet: missing field MPTokenIssuanceID");
            mpTokenIssuanceSet["MPTokenIssuanceID"] = "00000001A407AF5856CCF3C42619DAA925813FC955C72983";
        }

        [TestMethod]
        public async Task TestThrowsWithInvalidMPTokenIssuanceID()
        {
            mpTokenIssuanceSet["MPTokenIssuanceID"] = 12345;
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(mpTokenIssuanceSet),
                "MPTokenIssuanceSet: MPTokenIssuanceID must be a string");
            mpTokenIssuanceSet["MPTokenIssuanceID"] = "00000001A407AF5856CCF3C42619DAA925813FC955C72983";
        }

        [TestMethod]
        public async Task TestThrowsWithInvalidHolder()
        {
            mpTokenIssuanceSet["Holder"] = 12345;
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(mpTokenIssuanceSet),
                "MPTokenIssuanceSet: Holder must be a string");
            mpTokenIssuanceSet.Remove("Holder");
        }

        [TestMethod]
        public async Task TestThrowsWithBothLockAndUnlockFlags()
        {
            mpTokenIssuanceSet["Flags"] = (uint)(MPTokenIssuanceSetFlags.tfMPTLock | MPTokenIssuanceSetFlags.tfMPTUnlock);
            await Helper.ThrowsExceptionAsync<ValidationException>(
                () => Validation.Validate(mpTokenIssuanceSet),
                "MPTokenIssuanceSet: cannot set both tfMPTLock and tfMPTUnlock flags");
            mpTokenIssuanceSet.Remove("Flags");
        }
    }
}
