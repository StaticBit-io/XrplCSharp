// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/models/trustSet.ts

using System;
using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// TrustSet Transaction Verification Testing.<br/>
    /// Providing runtime verification testing for each specific transaction type.
    /// </summary>
    [TestClass]
    public class TestUTrustSet
    {

        public static Dictionary<string, object> trustSet;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            trustSet = new Dictionary<string, object>
            {
                { "TransactionType", "TrustSet" },
                {"Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo"},
                {"LimitAmount",new Dictionary<string,object>()
                {
                    {"currency","XRP"},
                    {"issuer","rcXY84C4g14iFp6taFXjjQGVeHqSCh9RX"},
                    {"value","4329.23"}
                }},
                {"QualityIn", 1234u},
                {"QualityOut", 4321u}
            };
        }

        [TestMethod]
        public void TestVerifyValid()
        {
            //verifies valid TrustSet
            Validation.ValidateTrustSet(trustSet);
            Validation.Validate(trustSet);

            //throws when LimitAmount is missing
            trustSet.Remove("LimitAmount");
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateTrustSet(trustSet), "TrustSet: missing field LimitAmount");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(trustSet), "TrustSet: missing field LimitAmount");

            //throws when LimitAmount is invalid
            trustSet.Add("LimitAmount", 1234);
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateTrustSet(trustSet), "TrustSet: invalid LimitAmount");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(trustSet), "TrustSet: invalid LimitAmount");
            trustSet["LimitAmount"] = new Dictionary<string, object>()
            {
                { "currency", "XRP" },
                { "issuer", "rcXY84C4g14iFp6taFXjjQGVeHqSCh9RX" },
                { "value", "4329.23" }
            };
            //throws when QualityIn is not a number
            trustSet["QualityIn"] = "1234";
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateTrustSet(trustSet), "TrustSet: QualityIn must be a number");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(trustSet), "TrustSet: QualityIn must be a number");
            trustSet["QualityIn"] = 1234u;
            //throws when QualityOut is not a number
            trustSet["QualityOut"] = "4321";
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateTrustSet(trustSet), "TrustSet: QualityOut must be a number");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(trustSet), "TrustSet: QualityOut must be a number");
            trustSet["QualityOut"] = 4321u;

        }
    }
}
