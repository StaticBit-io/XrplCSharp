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
        client = await IntegrationTestConfig.CreateClientAsync();
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

        // A tec is reported once validated (issue #266), so the summary is always there. It used to
        // race the first poll: a provisional tec was reported before the ledger closed, without it.
        Assert.IsNotNull(error.Result, "A tec is reported from the validated ledger, with its metadata.");
        Assert.AreEqual(
            "tecNO_DST_INSUF_XRP",
            error.Result.Meta?.TransactionResult,
            "The metadata that came with it must be the same outcome, not a second story.");
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
