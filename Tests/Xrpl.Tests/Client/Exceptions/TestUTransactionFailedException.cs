using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;

using Xrpl.Client.Exceptions;
using Xrpl.Models.Methods;

namespace Xrpl.Tests.Client.Exceptions
{
    /// <summary>
    /// The typed outcome of a failed submission - issue #131.
    /// </summary>
    /// <remarks>
    /// What a caller does next is decided by the class of the result code, and those classes mean
    /// entirely different things: <c>tem</c> is a malformed request to fix before resending,
    /// <c>tec</c> was applied to a ledger with the fee taken, <c>ter</c> may work later. Reading
    /// that out of the message worked until the first transaction whose text contained the
    /// substring somewhere else.
    /// </remarks>
    [TestClass]
    public class TestUTransactionFailedException
    {
        /// <summary>
        /// A failure that reached a ledger carries the transaction with it.
        /// </summary>
        [TestMethod]
        public void TestUAFailureInALedgerCarriesItsTransaction()
        {
            TransactionSummary summary = new TransactionSummary();

            TransactionFailedException error = new TransactionFailedException(
                "Final tx result is not success: tecDUPLICATE",
                engineResult: "tecDUPLICATE",
                hash: "5F8A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A4B5C6D7E8",
                result: summary);

            Assert.AreEqual("tecDUPLICATE", error.EngineResult);
            Assert.AreSame(summary, error.Result);
            Assert.IsTrue(error.ReachedLedger, "A tec was applied: the fee is gone and the transaction can be looked up.");
            Assert.IsFalse(string.IsNullOrEmpty(error.Hash), "The hash is what makes 'show me the transaction' possible.");
        }

        /// <summary>
        /// One refused before a ledger carries no transaction, and says so.
        /// </summary>
        /// <remarks>
        /// The distinction is the point of <see cref="TransactionFailedException.ReachedLedger"/>:
        /// nothing was charged and there is nothing to show, which is a different event for the
        /// caller even though it arrives as the same kind of failure.
        /// </remarks>
        [TestMethod]
        public void TestUARefusalBeforeALedgerHasNothingToShow()
        {
            TransactionFailedException error = new TransactionFailedException(
                "Final tx result is not success: temBAD_FEE",
                engineResult: "temBAD_FEE",
                hash: "5F8A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A4B5C6D7E8");

            Assert.AreEqual("temBAD_FEE", error.EngineResult);
            Assert.IsNull(error.Result);
            Assert.IsFalse(error.ReachedLedger, "A tem never reached a ledger, so no fee was taken.");
        }

        /// <summary>
        /// Existing code keeps working: the type and the message are both unchanged from a
        /// consumer's point of view.
        /// </summary>
        /// <remarks>
        /// This is the whole reason the type derives from <see cref="RippleException"/> rather than
        /// standing on its own, and the reason the message was left exactly as it was rather than
        /// improved while the code was open. Four integration tests in this repository assert that
        /// text word for word and needed no change - which is the same claim, made from the other
        /// side.
        /// </remarks>
        [TestMethod]
        public void TestUItIsStillARippleExceptionWithTheSameMessage()
        {
            Exception error = new TransactionFailedException(
                "Final tx result is not success: tecDUPLICATE",
                engineResult: "tecDUPLICATE",
                hash: "5F8A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A4B5C6D7E8");

            Assert.IsInstanceOfType<RippleException>(error, "catch (RippleException) must keep catching this.");
            Assert.AreEqual("Final tx result is not success: tecDUPLICATE", error.Message);
        }
    }
}
