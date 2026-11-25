using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration
{
    /// <summary>
    /// Fields a node sends, or reads, that the models had no member for: the ledger a result was
    /// read from on several commands, the binary forms of ledger_entry and simulate, gateway
    /// balances held in escrow, and the sponsored and delegate filters.
    /// </summary>
    [TestClass]
    public class TestIRpcResultFields
    {
        public TestContext TestContext { get; set; }
        private static TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;
        private static IXrplClient client;

        private static LedgerIndex Validated => new LedgerIndex(LedgerIndexType.Validated);

        [ClassInitialize]
        public static async Task ClassInitializeAsync(TestContext testContext)
        {
            client = await IntegrationTestConfig.CreateClientAsync(nodeType);
        }

        [ClassCleanup]
        public static void ClassCleanup() => client?.Dispose();

        private static async Task<XrplWallet> FundedWalletAsync()
        {
            XrplWallet wallet = XrplWallet.Generate();
            await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, wallet);
            return wallet;
        }

        private static async Task SubmitAsync(ITransactionRequest tx, XrplWallet wallet)
        {
            TransactionSummary result = await client.SubmitAndWait(await client.Autofill(tx), wallet, true);
            Assert.AreEqual("tesSUCCESS", result.Meta.TransactionResult);
        }

        [TestMethod]
        [Timeout(120000)]
        public async Task TestIAccountChannels_SaysWhichLedger()
        {
            XrplWallet wallet = await FundedWalletAsync();

            AccountChannels closed = await client.AccountChannels(
                new AccountChannelsRequest(wallet.ClassicAddress) { LedgerIndex = Validated }).Typed();
            Assert.IsNotNull(closed.LedgerIndex);
            Assert.IsFalse(string.IsNullOrEmpty(closed.LedgerHash));
            Assert.AreEqual(true, closed.Validated);
            Assert.IsNull(closed.LedgerCurrentIndex);

            AccountChannels open = await client.AccountChannels(new AccountChannelsRequest(wallet.ClassicAddress)).Typed();
            Assert.IsNotNull(open.LedgerCurrentIndex);
            Assert.IsNull(open.LedgerIndex);
            Assert.AreEqual(false, open.Validated);
        }

        [TestMethod]
        [Timeout(120000)]
        public async Task TestILedgerEntry_SingletonsAndBinary()
        {
            LedgerEntryResponse fee = await client.LedgerEntry(new LedgerEntryRequest { Fee = true, LedgerIndex = Validated }).Typed();
            Assert.IsInstanceOfType<LOFeeSettings>(fee.Node);
            Assert.IsNotNull(fee.LedgerIndex);
            Assert.IsFalse(string.IsNullOrEmpty(fee.LedgerHash));
            Assert.AreEqual(true, fee.Validated);

            LedgerEntryResponse hashes = await client.LedgerEntry(new LedgerEntryRequest { Hashes = true, LedgerIndex = Validated }).Typed();
            Assert.AreEqual((LedgerEntryType?)LedgerEntryType.LedgerHashes, hashes.Node.LedgerEntryType);

            LedgerEntryResponse binary = await client.LedgerEntry(
                new LedgerEntryRequest { Fee = true, Binary = true, LedgerIndex = Validated }).Typed();
            Assert.IsFalse(string.IsNullOrEmpty(binary.NodeBinary), "binary: true returns node_binary");
            Assert.IsNull(binary.Node);
            Assert.AreEqual(fee.Index, binary.Index);
        }

        [TestMethod]
        [Timeout(120000)]
        public async Task TestIRipplePathFindAndBookOffers_SayWhichLedger()
        {
            XrplWallet wallet = await FundedWalletAsync();

            RipplePathFindResponse paths = await client.RipplePathFind(new RipplePathFindRequest(
                wallet.ClassicAddress, wallet.ClassicAddress, new Currency { Value = "1", CurrencyCode = "XRP" })).Typed();
            Assert.IsNotNull(paths.LedgerCurrentIndex);
            Assert.AreEqual(false, paths.Validated);

            AccountOffers offers = await client.AccountOffers(
                new AccountOffersRequest(wallet.ClassicAddress) { LedgerIndex = Validated }).Typed();
            Assert.AreEqual(true, offers.Validated);
        }

        [TestMethod]
        [Timeout(120000)]
        public async Task TestISimulate_BinaryReturnsMetaBlob()
        {
            XrplWallet wallet = await FundedWalletAsync();

            SimulateResponse simulated = await client.Simulate(new SimulateRequest
            {
                Transaction = new AccountSet(wallet.ClassicAddress),
                Binary = true,
            }).Typed();

            Assert.AreEqual("tesSUCCESS", simulated.EngineResult);
            Assert.IsFalse(string.IsNullOrEmpty(simulated.MetaBlob), "binary: true returns meta_blob");
            Assert.IsNull(simulated.Meta);
        }

        [TestMethod]
        [Timeout(120000)]
        public async Task TestIGatewayBalances_ReadsLockedAndTheOpenLedger()
        {
            XrplWallet wallet = await FundedWalletAsync();

            await SubmitAsync(new EscrowCreate
            {
                Account = wallet.ClassicAddress,
                Destination = wallet.ClassicAddress,
                Amount = new Currency { Value = "5000000", CurrencyCode = "XRP" },
                FinishAfter = DateTime.UtcNow.AddDays(30),
            }, wallet);

            GatewayBalancesResponse closed = await client.GatewayBalances(
                new GatewayBalancesRequest(wallet.ClassicAddress) { LedgerIndex = Validated }).Typed();
            Assert.IsNotNull(closed.Locked, "an escrow in the owner directory is reported as locked");
            Assert.AreEqual("5000000", closed.Locked["XRP"]);

            GatewayBalancesResponse open = await client.GatewayBalances(new GatewayBalancesRequest(wallet.ClassicAddress)).Typed();
            Assert.IsNotNull(open.LedgerCurrentIndex);
        }

        [TestMethod]
        [Timeout(120000)]
        public async Task TestIAccountObjects_SponsoredFilter()
        {
            XrplWallet wallet = await FundedWalletAsync();
            await SubmitAsync(new TicketCreate { Account = wallet.ClassicAddress, TicketCount = 1 }, wallet);

            AccountObjects unsponsored = await client.AccountObjects(
                new AccountObjectsRequest(wallet.ClassicAddress) { Sponsored = false, LedgerIndex = Validated }).Typed();
            Assert.AreEqual(1, unsponsored.AccountObjectList.OfType<LOTicket>().Count());

            AccountObjects sponsored = await client.AccountObjects(
                new AccountObjectsRequest(wallet.ClassicAddress) { Sponsored = true, LedgerIndex = Validated }).Typed();
            Assert.AreEqual(0, sponsored.AccountObjectList.Count, "nothing on this account carries a sponsor");
        }

        [TestMethod]
        [Timeout(120000)]
        public async Task TestIAccountTransactions_DelegateFilter()
        {
            XrplWallet wallet = await FundedWalletAsync();
            await SubmitAsync(new AccountSet(wallet.ClassicAddress), wallet);

            AccountTransactions all = await client.AccountTransactions(
                new AccountTransactionsRequest(wallet.ClassicAddress)).Typed();
            Assert.IsTrue(all.Transactions.Count > 0);

            AccountTransactions delegated = await client.AccountTransactions(new AccountTransactionsRequest(wallet.ClassicAddress)
            {
                Delegate = new AccountTransactionsDelegateFilter { DelegateFilter = "actor" },
            }).Typed();
            Assert.AreEqual(0, delegated.Transactions.Count, "the account never acted for another account");
        }
    }
}
