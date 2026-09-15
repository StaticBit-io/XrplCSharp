using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Models.Common;
using Xrpl.Models.Transactions;
using Xrpl.Models.Utils;

namespace XrplTests.Xrpl.Models
{
    /// <summary>
    /// Pins that BatchUtils.Build validates the batch it assembles. The validation call used to be
    /// made without awaiting it, so every rule in ValidateBatch was reported into a task nobody
    /// observed and a malformed batch left Build looking well formed.
    /// </summary>
    [TestClass]
    public class TestUBatchUtils
    {
        private const string Account = "rMFMJsQaKEMEwwMRBHCoNXkFswJrWyNYLp";
        private const string Destination = "rJcvzBFCTcCrQdTLPZoRXJEboT9C3Wrd6H";

        private static Payment Inner(uint sequence) => new Payment
        {
            Account = Account,
            Destination = Destination,
            Amount = new Currency { Value = "1" },
            Sequence = sequence,
        };

        private static List<ITransactionRequest> Inners(int count) =>
            Enumerable.Range(1, count).Select(i => (ITransactionRequest)Inner((uint)i)).ToList();

        [TestMethod]
        public void TestUBatchUtilsBuild_RejectsASingleInnerTransaction()
        {
            Helper.ThrowsException<ArgumentException>(
                () => BatchUtils.Build(Account, Inners(1)),
                "Batch: RawTransactions must contain at least 2 transactions (rippled answers temARRAY_EMPTY to a single inner).");
        }

        [TestMethod]
        public void TestUBatchUtilsBuild_RejectsMoreThanEightInnerTransactions()
        {
            Helper.ThrowsException<ArgumentException>(
                () => BatchUtils.Build(Account, Inners(9)),
                "Batch: RawTransactions length must be <= 8.");
        }

        [TestMethod]
        public void TestUBatchUtilsBuild_AcceptsAWellFormedBatch()
        {
            Batch batch = BatchUtils.Build(Account, Inners(2));

            Assert.AreEqual(2, batch.RawTransactions.Count);
        }
    }
}
