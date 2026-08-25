using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// A failed submission arrives as something a caller can act on - issue #131.
/// </summary>
/// <remarks>
/// Unit tests can build the exception and check its shape; only a node can show that the shape is
/// filled in on the path that actually produces it. This sends a payment a node refuses for a
/// reason that is easy to arrange and impossible to mistake for anything else.
/// </remarks>
[TestClass]
public class TestITypedSubmitFailure
{
    private static IXrplClient client;
    private static XrplWallet wallet;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(TestNodeType.Standalone);
        wallet = await Utils.GenerateFundedWallet(client);
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    /// <summary>
    /// A <c>tec</c> reaches a ledger, so the failure carries the code, the hash and the transaction.
    /// </summary>
    /// <remarks>
    /// One drop to an account that does not exist is below the reserve needed to create it, which
    /// the node answers with <c>tecNO_DST_INSUF_XRP</c>: applied, fee taken, and there in the
    /// ledger to be looked up. That is exactly the case where the caller has something to show and
    /// used to have only a sentence to parse.
    /// <para>
    /// Which moment reports it is a race with ledger closing, and the assertions below are chosen
    /// to be true at both: see the note beside them.
    /// </para>
    /// </remarks>
    [TestMethod]
    public async Task TestIAFailureInALedgerArrivesTyped()
    {
        Dictionary<string, object> tx = new Dictionary<string, object>
        {
            { "TransactionType", "Payment" },
            { "Account", wallet.ClassicAddress },
            { "Destination", XrplWallet.Generate().ClassicAddress },
            { "Amount", "1" },
        };

        TransactionFailedException error = await Assert.ThrowsExactlyAsync<TransactionFailedException>(
            () => client.SubmitAndWait(tx, wallet));

        Assert.AreEqual(
            "tecNO_DST_INSUF_XRP",
            error.EngineResult,
            $"The code must arrive as a code, not as prose to search. Message was: {error.Message}");
        Assert.IsTrue(
            error.ReachedLedger,
            "A tec was applied to a ledger: the fee is gone, and that is the caller's business to know.");
        Assert.IsFalse(
            string.IsNullOrEmpty(error.Hash),
            "Without the hash there is no way to show the transaction that just cost a fee.");

        // Result is deliberately not asserted to be present. The same failure is reported at one of
        // two moments depending on whether the ledger closed before the first poll: after
        // validation, with the metadata, or earlier from the node's provisional answer, when only
        // the code and the hash exist. Requiring the summary here would make this test a race with
        // ledger timing. What can be required is that it does not contradict the code when it is
        // there - and that ReachedLedger says the same thing either way, which is why it is read
        // from the code rather than from this.
        if (error.Result is not null)
        {
            Assert.AreEqual(
                "tecNO_DST_INSUF_XRP",
                error.Result.Meta?.TransactionResult,
                "The metadata that came with it must be the same outcome, not a second story.");
        }
    }

    /// <summary>
    /// And it is still a <c>RippleException</c>, so code written before this keeps working.
    /// </summary>
    [TestMethod]
    public async Task TestITheFailureIsStillARippleException()
    {
        Dictionary<string, object> tx = new Dictionary<string, object>
        {
            { "TransactionType", "Payment" },
            { "Account", wallet.ClassicAddress },
            { "Destination", XrplWallet.Generate().ClassicAddress },
            { "Amount", "1" },
        };

        // Caught by the base type on purpose: that a derived exception still lands in an
        // existing catch is the compatibility claim, and asserting the exact type would test
        // something else.
        RippleException error = null;
        try
        {
            await client.SubmitAndWait(tx, wallet);
        }
        catch (RippleException thrown)
        {
            error = thrown;
        }

        Assert.IsNotNull(error, "The payment must have been refused.");

        StringAssert.Contains(
            error.Message,
            "Final tx result is not success",
            "The message is unchanged on purpose: consumers matching on it are not broken by this.");
    }
}
