

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/models/accountSet.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUAccountSet
    {
        public static Dictionary<string, object> accountSet;

        [ClassInitialize]
        public static void MyClassInitialize(TestContext testContext)
        {
            accountSet = new Dictionary<string, object>
            {
                {"TransactionType", "AccountSet"},
                {"Account", "rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn"},
                {"Fee", "12"},
                {"Sequence", 5u},
                {"Domain", "6578616D706C652E636F6D"},
                {"SetFlag", 5u},
                {"MessageKey", "03AB40A0490F9B7ED8DF29D246BF2D6269820A0EE7742ACDD457BEA7C7D0931EDB"},
            };
        }

        [TestMethod]
        public void TestVerifyValid()
        {
            //verifies valid AccountSet
            Validation.ValidateAccountSet(accountSet);
            Validation.Validate(accountSet);

            //throws w/ invalid SetFlag (out of range; 12 is a valid asf value and int is a valid representation)
            accountSet["SetFlag"] = 9999;
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateAccountSet(accountSet), "AccountSet: invalid SetFlag");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(accountSet), "AccountSet: invalid SetFlag");

            //throws w/ invalid SetFlag (incorrect type)
            accountSet["SetFlag"] = "abc";
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateAccountSet(accountSet), "AccountSet: invalid SetFlag");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(accountSet), "AccountSet: invalid SetFlag");

            accountSet["SetFlag"] = 5u;

            //throws w/ invalid ClearFlag (out of range)
            accountSet["ClearFlag"] = 9999;
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateAccountSet(accountSet), "AccountSet: invalid ClearFlag");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(accountSet), "AccountSet: invalid ClearFlag");
            accountSet.Remove("ClearFlag");

            //throws w/ invalid Domain
            accountSet["Domain"] = 6578616;
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateAccountSet(accountSet), "AccountSet: invalid Domain");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(accountSet), "AccountSet: invalid Domain");
            accountSet["Domain"] = "6578616D706C652E636F6D";

            //throws w/ invalid EmailHash
            accountSet["EmailHash"] = 6578656789876543;
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateAccountSet(accountSet), "AccountSet: invalid EmailHash");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(accountSet), "AccountSet: invalid EmailHash");
            accountSet.Remove("EmailHash");

            //throws w/ invalid MessageKey
            accountSet["MessageKey"] = 6578656789876543;
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateAccountSet(accountSet), "AccountSet: invalid MessageKey");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(accountSet), "AccountSet: invalid MessageKey");
            accountSet["MessageKey"] = "03AB40A0490F9B7ED8DF29D246BF2D6269820A0EE7742ACDD457BEA7C7D0931EDB";

            //throws w/ invalid TransferRate
            accountSet["TransferRate"] = "1000000001";
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateAccountSet(accountSet), "AccountSet: invalid TransferRate");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(accountSet), "AccountSet: invalid TransferRate");
            accountSet.Remove("TransferRate");

            //throws w/ invalid TickSize (non-numeric type; int/long are valid integral representations)
            accountSet["TickSize"] = "5";
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateAccountSet(accountSet), "AccountSet: invalid TickSize");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(accountSet), "AccountSet: invalid TickSize");
            //throws w/ invalid TickSize
            accountSet["TickSize"] = 20u;
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateAccountSet(accountSet), "AccountSet: out of TickSize");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(accountSet), "AccountSet: out of TickSize");
            accountSet.Remove("TickSize");
        }

        [TestMethod]
        public void TestUAccountSet_ValidatesWalletFieldTypes()
        {
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                { "TransactionType", "AccountSet" },
                { "Account", "rUn84CUYbNjRoTQ6mSW7BVJPSVJNLb1QLo" },
                { "Fee", "12" },
            };

            // sfWalletLocator is a Hash256: a plain string is not enough, it must be 64 hex chars.
            // Same rule the SignerListSet validator already applies to WalletLocator in a SignerEntry.
            tx["WalletLocator"] = new string('A', 64);
            tx["WalletSize"] = 3u;
            Validation.ValidateAccountSet(tx);

            tx["WalletLocator"] = 12345;
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateAccountSet(tx), "AccountSet: invalid WalletLocator");

            tx["WalletLocator"] = "not a hash";
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateAccountSet(tx), "AccountSet: invalid WalletLocator");

            tx["WalletLocator"] = new string('A', 64);
            tx["WalletSize"] = "3";
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateAccountSet(tx), "AccountSet: invalid WalletSize");
        }
    }
}

