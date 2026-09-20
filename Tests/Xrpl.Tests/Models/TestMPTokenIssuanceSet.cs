using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUMPTokenIssuanceSet
    {
        public static Dictionary<string, object> mpTokenIssuanceSet;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            mpTokenIssuanceSet = new Dictionary<string, object>
            {
                {"TransactionType", "MPTokenIssuanceSet"},
                {"Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm"},
                {"MPTokenIssuanceID", "00000001A407AF5856CCF3C42619DAA925813FC955C72983"},
                {"Sequence", 1337u},
            };
        }

        [TestMethod]
        public void TestVerifyValid()
        {
            Validation.Validate(mpTokenIssuanceSet);
        }

        [TestMethod]
        public void TestVerifyWithHolder()
        {
            mpTokenIssuanceSet["Holder"] = "rPyfep3gcLzkosKC9XiE77Y8DZWG6iWDT9";
            Validation.Validate(mpTokenIssuanceSet);
            mpTokenIssuanceSet.Remove("Holder");
        }

        [TestMethod]
        public void TestThrowsWithMissingMPTokenIssuanceID()
        {
            mpTokenIssuanceSet.Remove("MPTokenIssuanceID");
            Helper.ThrowsException<ValidationException>(
                () => Validation.Validate(mpTokenIssuanceSet),
                "MPTokenIssuanceSet: missing field MPTokenIssuanceID");
            mpTokenIssuanceSet["MPTokenIssuanceID"] = "00000001A407AF5856CCF3C42619DAA925813FC955C72983";
        }

        [TestMethod]
        public void TestThrowsWithInvalidMPTokenIssuanceID()
        {
            mpTokenIssuanceSet["MPTokenIssuanceID"] = 12345;
            Helper.ThrowsException<ValidationException>(
                () => Validation.Validate(mpTokenIssuanceSet),
                "MPTokenIssuanceSet: MPTokenIssuanceID must be a string");
            mpTokenIssuanceSet["MPTokenIssuanceID"] = "00000001A407AF5856CCF3C42619DAA925813FC955C72983";
        }

        [TestMethod]
        public void TestThrowsWithInvalidHolder()
        {
            mpTokenIssuanceSet["Holder"] = 12345;
            Helper.ThrowsException<ValidationException>(
                () => Validation.Validate(mpTokenIssuanceSet),
                "MPTokenIssuanceSet: Holder must be a string");
            mpTokenIssuanceSet.Remove("Holder");
        }

        [TestMethod]
        public void TestThrowsWithBothLockAndUnlockFlags()
        {
            mpTokenIssuanceSet["Flags"] = (uint)(MPTokenIssuanceSetFlags.tfMPTLock | MPTokenIssuanceSetFlags.tfMPTUnlock);
            Helper.ThrowsException<ValidationException>(
                () => Validation.Validate(mpTokenIssuanceSet),
                "MPTokenIssuanceSet: cannot set both tfMPTLock and tfMPTUnlock flags");
            mpTokenIssuanceSet.Remove("Flags");
        }

        [TestMethod]
        public void TestVerifyWithZeroDomainId()
        {
            try
            {
                // rippled MPTokenIssuanceSet: a zero DomainID clears the domain - legal
                mpTokenIssuanceSet["DomainID"] = new string('0', 64);
                Validation.Validate(mpTokenIssuanceSet);
            }
            finally
            {
                mpTokenIssuanceSet.Remove("DomainID");
            }
        }

        [TestMethod]
        public void TestThrowsWithMalformedDomainId()
        {
            try
            {
                mpTokenIssuanceSet["DomainID"] = "1234";
                Helper.ThrowsException<ValidationException>(
                    () => Validation.Validate(mpTokenIssuanceSet),
                    "MPTokenIssuanceSet: DomainID must be a 64-character hexadecimal string");
            }
            finally
            {
                mpTokenIssuanceSet.Remove("DomainID");
            }
        }
    }
}