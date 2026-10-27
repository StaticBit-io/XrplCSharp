using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Utils;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// TransactionProposalCreate / TransactionProposalCancel and the TransactionProposal object
/// (Cosign) end to end: a proposal is stored, found through <c>ledger_entry</c> by the proposed
/// transaction's account and ticket, read back with its proposed transaction typed, and cancelled.
/// </summary>
/// <remarks>
/// Cosign is Supported::No on rippled develop, so no stand enables it at genesis; the class runs on
/// the nightly stand with Cosign added to <c>[features]</c> and is inconclusive everywhere else.
/// </remarks>
[TestClass]
public class TestITransactionProposal
{
    private const string Amendment = "Cosign";

    public TestContext TestContext { get; set; }
    private static IXrplClient client;
    private static readonly TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(nodeType);
    }

    [TestInitialize]
    public async Task RequireCosignAsync()
    {
        await AmendmentGuard.RequireTransactionTypeAsync(client, "TransactionProposalCreate", Amendment);
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    [TestMethod]
    public async Task TestTransactionProposal_CreateFindAndCancel()
    {
        XrplWallet owner = XrplWallet.Generate();
        XrplWallet destination = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, owner, destination);

        uint ticket = await CreateTicketAsync(owner);
        DateTime closeTime = await IntegrationTestConfig.ValidatedCloseTimeAsync(client);
        uint expiration = (uint)DateTimeUtils.UnixTimeToRippleTime(new DateTimeOffset(closeTime).ToUnixTimeSeconds()) + 3600;

        TransactionProposalCreate create = new TransactionProposalCreate
        {
            Account = owner.ClassicAddress,
            Expiration = expiration,
            ProposedTransaction = new Payment
            {
                Account = owner.ClassicAddress,
                Destination = destination.ClassicAddress,
                Amount = new Currency { ValueAsXrp = 1 },
                Fee = new Currency { Value = "12" },
                Sequence = 0,
                TicketSequence = ticket,
                SigningPublicKey = "",
            },
        };
        create = await client.Autofill(create);
        Submit created = await client.SubmitRequest(owner.Sign(create).TxBlob, false);
        AmendmentGuard.SkipIfDisabled(created.EngineResult, Amendment);
        Assert.AreEqual("tesSUCCESS", created.EngineResult, created.EngineResultMessage);
        await IntegrationTestConfig.LedgerAcceptAsync(client, nodeType);

        LedgerEntryResponse found = await client.LedgerEntry(new LedgerEntryRequest
        {
            TransactionProposal = new TransactionProposalQuery { Account = owner.ClassicAddress, TicketSequence = ticket },
            LedgerIndex = new LedgerIndex(LedgerIndexType.Validated),
        }).Typed();

        Assert.IsInstanceOfType(found.Node, typeof(LOTransactionProposal), "ledger_entry should deserialize to LOTransactionProposal");
        LOTransactionProposal proposal = (LOTransactionProposal)found.Node;
        Assert.AreEqual(owner.ClassicAddress, proposal.Owner);
        Assert.AreEqual(expiration, proposal.Expiration);
        Assert.IsInstanceOfType(proposal.ProposedTransaction, typeof(Payment), "ProposedTransaction should be read as its own type");
        Payment proposed = (Payment)proposal.ProposedTransaction;
        Assert.AreEqual(destination.ClassicAddress, proposed.Destination);
        Assert.AreEqual(ticket, proposed.TicketSequence);

        AccountObjects objects = await client.AccountObjects(new AccountObjectsRequest(owner.ClassicAddress)
        {
            Type = LedgerEntryType.TransactionProposal,
            LedgerIndex = new LedgerIndex(LedgerIndexType.Validated),
        }).Typed();
        Assert.AreEqual(1, objects.AccountObjectList.OfType<LOTransactionProposal>().Count(), "The owner directory should list the proposal");

        TransactionSummary cancelled = await client.SubmitAndWait(new TransactionProposalCancel
        {
            Account = owner.ClassicAddress,
            ProposalID = found.Index,
        }, owner, true, true);
        Assert.AreEqual("tesSUCCESS", cancelled.Meta.TransactionResult);

        RippledException gone = await Assert.ThrowsAsync<RippledException>(() => client.LedgerEntry(new LedgerEntryRequest
        {
            Index = found.Index,
            LedgerIndex = new LedgerIndex(LedgerIndexType.Validated),
        }).Typed());
        StringAssert.Contains(gone.Message, "entryNotFound");
    }

    [TestMethod]
    public async Task TestTransactionProposalCancel_UnknownProposal_IsTecNoEntry()
    {
        XrplWallet owner = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, owner);

        TransactionProposalCancel cancel = await client.Autofill(new TransactionProposalCancel
        {
            Account = owner.ClassicAddress,
            ProposalID = "1D5A924A24F9AF5F1BF2AD9F4E385E4C0F5B85B94E4C4A1D8B6E1C0B7F3D2E1A",
        });
        Submit response = await client.SubmitRequest(owner.Sign(cancel).TxBlob, false);
        AmendmentGuard.SkipIfDisabled(response.EngineResult, Amendment);

        // tecNO_ENTRY comes from preclaim's lookup of ProposalID: the node parsed the Hash256.
        Assert.AreEqual("tecNO_ENTRY", response.EngineResult, response.EngineResultMessage);
    }

    private static async Task<uint> CreateTicketAsync(XrplWallet wallet)
    {
        TransactionSummary res = await client.SubmitAndWait(new TicketCreate { Account = wallet.ClassicAddress, TicketCount = 1 }, wallet, true, true);
        Assert.AreEqual("tesSUCCESS", res.Meta.TransactionResult);

        AccountObjects objects = await client.AccountObjects(new AccountObjectsRequest(wallet.ClassicAddress)
        {
            Type = LedgerEntryType.Ticket,
            LedgerIndex = new LedgerIndex(LedgerIndexType.Validated),
        }).Typed();
        uint? ticket = objects.AccountObjectList.OfType<LOTicket>().Select(t => t.TicketSequence).Max();
        return ticket ?? throw new AssertFailedException("TicketCreate left no Ticket.");
    }
}
