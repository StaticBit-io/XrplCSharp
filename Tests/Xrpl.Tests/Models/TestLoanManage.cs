using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// LoanManage flags: at most one action flag, and none is a valid no-op
    /// (rippled <c>LoanManage::preflight</c> rejects only more than one with temINVALID_FLAG).
    /// </summary>
    [TestClass]
    public class TestULoanManage
    {
        private const string LoanId = "2222222222222222222222222222222222222222222222222222222222222222";

        private static Dictionary<string, object> Tx(LoanManageFlags? flags) => new LoanManage
        {
            Account = "r3rhWeE31Jt5sWmi4QiGLMZnY3ENgqw96W",
            LoanID = LoanId,
            Fee = new global::Xrpl.Models.Common.Currency { Value = "12" },
            Sequence = 1u,
            Flags = flags,
        }.ToDictionary();

        [TestMethod]
        public void TestVerify_NoFlags_IsValidNoOp()
        {
            Dictionary<string, object> tx = Tx(null);
            Assert.IsFalse(tx.ContainsKey("Flags"));
            Validation.ValidateLoanManage(tx);
        }

        [TestMethod]
        public void TestVerify_ZeroFlags_IsValidNoOp()
        {
            Validation.ValidateLoanManage(Tx((LoanManageFlags)0));
        }

        [TestMethod]
        public void TestVerify_InnerBatchFlagOnly_IsValidNoOp()
        {
            Validation.ValidateLoanManage(Tx(LoanManageFlags.tfInnerBatchTxn));
        }

        [TestMethod]
        [DataRow(LoanManageFlags.tfLoanDefault)]
        [DataRow(LoanManageFlags.tfLoanImpair)]
        [DataRow(LoanManageFlags.tfLoanUnimpair)]
        public void TestVerify_OneActionFlag_IsValid(LoanManageFlags flag)
        {
            Validation.ValidateLoanManage(Tx(flag));
            Validation.ValidateLoanManage(Tx(flag | LoanManageFlags.tfInnerBatchTxn));
        }

        [TestMethod]
        [DataRow(LoanManageFlags.tfLoanDefault | LoanManageFlags.tfLoanImpair)]
        [DataRow(LoanManageFlags.tfLoanImpair | LoanManageFlags.tfLoanUnimpair)]
        [DataRow(LoanManageFlags.tfLoanDefault | LoanManageFlags.tfLoanUnimpair)]
        public void TestVerify_TwoActionFlags_Throws(LoanManageFlags flags)
        {
            ValidationException ex = Assert.ThrowsExactly<ValidationException>(() => Validation.ValidateLoanManage(Tx(flags)));
            StringAssert.Contains(ex.Message, "at most one action flag");
        }

        [TestMethod]
        public void TestVerify_MissingLoanId_Throws()
        {
            Dictionary<string, object> tx = Tx(LoanManageFlags.tfLoanDefault);
            tx.Remove("LoanID");
            Assert.ThrowsExactly<ValidationException>(() => Validation.ValidateLoanManage(tx));
        }
    }
}
