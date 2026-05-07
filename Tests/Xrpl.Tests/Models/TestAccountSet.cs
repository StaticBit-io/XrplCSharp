

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/models/accountSet.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Threading.Tasks;

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
        public async Task TestVerifyValid()
        {
            //verifies valid AccountSet
            await Validation.ValidateAccountSet(accountSet);
            await Validation.Validate(accountSet);

            //throws w/ invalid SetFlag (out of range)
            accountSet["SetFlag"] = 12;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateAccountSet(accountSet), "AccountSet: invalid SetFlag");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(accountSet), "AccountSet: invalid SetFlag");

            //throws w/ invalid SetFlag (incorrect type)
            accountSet["SetFlag"] = "abc";
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateAccountSet(accountSet), "AccountSet: invalid SetFlag");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(accountSet), "AccountSet: invalid SetFlag");

            accountSet["SetFlag"] = 5u;

            //throws w/ invalid ClearFlag
            accountSet["ClearFlag"] = 12;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateAccountSet(accountSet), "AccountSet: invalid ClearFlag");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(accountSet), "AccountSet: invalid ClearFlag");
            accountSet.Remove("ClearFlag");

            //throws w/ invalid Domain
            accountSet["Domain"] = 6578616;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateAccountSet(accountSet), "AccountSet: invalid Domain");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(accountSet), "AccountSet: invalid Domain");
            accountSet["Domain"] = "6578616D706C652E636F6D";

            //throws w/ invalid EmailHash
            accountSet["EmailHash"] = 6578656789876543;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateAccountSet(accountSet), "AccountSet: invalid EmailHash");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(accountSet), "AccountSet: invalid EmailHash");
            accountSet.Remove("EmailHash");

            //throws w/ invalid MessageKey
            accountSet["MessageKey"] = 6578656789876543;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateAccountSet(accountSet), "AccountSet: invalid MessageKey");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(accountSet), "AccountSet: invalid MessageKey");
            accountSet["MessageKey"] = "03AB40A0490F9B7ED8DF29D246BF2D6269820A0EE7742ACDD457BEA7C7D0931EDB";

            //throws w/ invalid TransferRate
            accountSet["TransferRate"] = "1000000001";
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateAccountSet(accountSet), "AccountSet: invalid TransferRate");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(accountSet), "AccountSet: invalid TransferRate");
            accountSet.Remove("TransferRate");

            //throws w/ invalid TickSize
            accountSet["TickSize"] = 5;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateAccountSet(accountSet), "AccountSet: invalid TickSize");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(accountSet), "AccountSet: invalid TickSize");
            //throws w/ invalid TickSize
            accountSet["TickSize"] = 20u;
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.ValidateAccountSet(accountSet), "AccountSet: out of TickSize");
            await Helper.ThrowsExceptionAsync<ValidationException>(() => Validation.Validate(accountSet), "AccountSet: out of TickSize");
            accountSet.Remove("TickSize");
        }
    }
}

