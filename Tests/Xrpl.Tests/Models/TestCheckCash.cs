using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Xrpl.Client.Exceptions;

using Xrpl.Models.Transaction;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUCheckCash
    {
        [TestMethod]
        public void TestVerify_Valid_CheckCash()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "CheckCash" },
                {"Account", "rfkE1aSy9G8Upk4JssnwBxhEv5p4mn2KTy"},
                {"Amount", "100000000"},
                {"CheckID", "838766BA2B995C00744175F69A1B11E32C3DBC40E64801A4056FCBD657F57334"},
                {"Fee", "12"},
            };
            Validation.ValidateCheckCash(tx);
            Validation.Validate(tx);
        }
        [TestMethod]
        public void TestVerify_InValid_CheckID()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "CheckCash" },
                {"Account", "rfkE1aSy9G8Upk4JssnwBxhEv5p4mn2KTy"},
                {"CheckID", 83876645678567890 },
                {"Amount", "100000000"}
            };
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateCheckCash(tx), "CheckCash: invalid CheckID");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(tx), "CheckCash: invalid CheckID");
        }
        [TestMethod]
        public void TestVerify_InValid_Amount()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "CheckCash" },
                {"Account", "rfkE1aSy9G8Upk4JssnwBxhEv5p4mn2KTy"},
                {"CheckID", "838766BA2B995C00744175F69A1B11E32C3DBC40E64801A4056FCBD657F57334"},
                {"Amount", 100000000}
            };
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateCheckCash(tx), "CheckCash: invalid Amount");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(tx), "CheckCash: invalid Amount");
        }
        [TestMethod]
        public void TestVerify_InValid_having_both_Amount_and_DeliverMin()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "CheckCash" },
                {"Account", "rfkE1aSy9G8Upk4JssnwBxhEv5p4mn2KTy"},
                {"CheckID", "838766BA2B995C00744175F69A1B11E32C3DBC40E64801A4056FCBD657F57334"},
                {"Amount", "100000000"},
                {"DeliverMin", 852156963}
            };
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateCheckCash(tx), "CheckCash: cannot have both Amount and DeliverMin");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(tx), "CheckCash: cannot have both Amount and DeliverMin");
        }
        [TestMethod]
        public void TestVerify_InValid_DeliverMin()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "CheckCash" },
                {"Account", "rfkE1aSy9G8Upk4JssnwBxhEv5p4mn2KTy"},
                {"CheckID", "838766BA2B995C00744175F69A1B11E32C3DBC40E64801A4056FCBD657F57334"},
                {"DeliverMin", 852156963}
            };
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateCheckCash(tx), "CheckCash: invalid DeliverMin");
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(tx), "CheckCash: invalid DeliverMin");
        }
    }

}
