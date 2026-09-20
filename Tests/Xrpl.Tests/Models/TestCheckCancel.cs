

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/models/checkCancel.ts

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transaction;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    [TestClass]
    public class TestUCheckCancel
    {
        [TestMethod]
        public void TestVerify_Valid_CheckCancel()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "CheckCancel" },
                {"Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm"},
                {"CheckID", "49647F0D748DC3FE26BDACBC57F251AADEFFF391403EC9BF87C97F67E9977FB0"},
            };
            Validation.ValidateCheckCancel(tx);
            Validation.Validate(tx);
        }
        [TestMethod]
        public void TestVerify_InValid_CheckID()
        {
            var tx = new Dictionary<string, object>
            {
                { "TransactionType", "CheckCancel" },
                {"Account", "rWYkbWkCeg8dP6rXALnjgZSjjLyih5NXm" },
                {"CheckID", 4964734566545678 }, //todo no check for CheckID size
            };
            Helper.ThrowsException<ValidationException>(() => Validation.ValidateCheckCancel(tx));
            Helper.ThrowsException<ValidationException>(() => Validation.Validate(tx));
        }
    }

}

