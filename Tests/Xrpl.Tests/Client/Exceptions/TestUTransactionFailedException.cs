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
        /// A <c>tec</c> reported before the ledger closed is still a <c>tec</c>: applied, fee taken,
        /// and no summary to hand over yet.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the case the whole design of <see cref="TransactionFailedException.ReachedLedger"/>
        /// turns on, and the one an integration run found: the same failure is reported at one of
        /// two moments depending on whether the ledger closed before the first poll. Reading the
        /// answer off <see cref="TransactionFailedException.Result"/> would make it depend on which
        /// moment won that race, and a caller would be told the fee was not taken when it was.
        /// </para>
        /// <para>
        /// Without this test the two above pass either way - a <c>tec</c> with a summary and a
        /// <c>tem</c> without one give the same answer under both readings. Only this combination
        /// tells them apart, which is why it is written down rather than left to the integration
        /// test, whose branch depends on ledger timing.
        /// </para>
        /// </remarks>
        [TestMethod]
        public void TestUATecWithoutASummaryStillReachedALedger()
        {
            TransactionFailedException error = new TransactionFailedException(
                "Final tx result is not success: tecNO_DST_INSUF_XRP",
                engineResult: "tecNO_DST_INSUF_XRP",
                hash: "5F8A1B2C3D4E5F60718293A4B5C6D7E8F90A1B2C3D4E5F60718293A4B5C6D7E8");

            Assert.IsNull(error.Result, "Precondition: this is the moment before validation.");
            Assert.IsTrue(
                error.ReachedLedger,
                "A tec was applied whether or not the summary has arrived - the fee is gone either way.");
            Assert.IsFalse(
                string.IsNullOrEmpty(error.Hash),
                "And the hash, which is what an explorer needs, is there in this case too.");
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
