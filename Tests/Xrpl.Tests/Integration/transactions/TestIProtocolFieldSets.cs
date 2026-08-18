using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration
{
    /// <summary>
    /// Ground truth for the TxFormat corrections and the new model properties: a real rippled
    /// has to accept exactly the field sets the SDK now declares.
    /// </summary>
    /// <remarks>
    /// TxFormat itself cannot be exercised end-to-end — it is inert at runtime. What these tests
    /// pin is the claim underneath it: that the fields we added are real and land on the ledger,
    /// and that the ones we removed were never top-level fields of those transactions. That claim
    /// was previously backed only by reading rippled's transactions.macro.
    /// </remarks>
    [TestClass]
    public class TestIProtocolFieldSets
    {
        public TestContext TestContext { get; set; }
        private static TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;

        private static DateTime RippleEpoch => new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>Submit responses carry tx_json as a JsonElement on some paths, a JsonObject on others.</summary>
        private static string HashOf(Submit submitted) => submitted.TxJson switch
        {
            System.Text.Json.JsonElement element => element.GetProperty("hash").GetString(),
            System.Text.Json.Nodes.JsonObject node => node["hash"].GetValue<string>(),
            _ => throw new InvalidOperationException($"unexpected TxJson type {submitted.TxJson?.GetType()}"),
        };

        [TestMethod]
        [Timeout(120000)]
        public async Task TestICheckCreate_OptionalFieldsLandOnTheLedger()
        {
            IXrplClient client = await IntegrationTestConfig.CreateClientAsync(nodeType);
            try
            {
                XrplWallet sender = XrplWallet.Generate();
                XrplWallet receiver = XrplWallet.Generate();
                await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, sender, receiver);

                const string invoiceId = "6F1DFD1D0FE8A32E40E1F2C05CF1C15545BAB56B617F9C6C2D63A6B704BEF59B";
                DateTime expiration = RippleEpoch.AddSeconds(900000000);

                CheckCreate tx = new CheckCreate
                {
                    Account = sender.ClassicAddress,
                    Destination = receiver.ClassicAddress,
                    SendMax = new Currency { ValueAsXrp = 50 },
                    DestinationTag = 13,
                    Expiration = expiration,
                    InvoiceID = invoiceId,
                };
                Submit submitted = await client.Submit(tx.ToDictionary(), sender);
                Assert.AreEqual("tesSUCCESS", submitted.EngineResult);
                string hash = HashOf(submitted);
                await Utils.LedgerAccept(client);

                AccountObjects objects = (await client.AccountObjects(
                    new AccountObjectsRequest(sender.ClassicAddress) { Type = LedgerEntryType.Check })).Result;
                LOCheck check = objects.AccountObjectList.OfType<LOCheck>().Single();

                Assert.AreEqual(invoiceId, check.InvoiceID, "InvoiceID must survive as a Hash256");
                Assert.AreEqual(13u, check.DestinationTag);
                Assert.AreEqual(expiration, check.Expiration);

                // ...and back into the typed transaction model, not just the ledger object:
                // InvoiceID was uint? until this change and could not round-trip at all
                CheckCreateResponse readBack = (await client.TxV1(new TxRequest(hash))).Result as CheckCreateResponse;
                Assert.IsNotNull(readBack, "tx must deserialize into CheckCreateResponse");
                Assert.AreEqual(invoiceId, readBack.InvoiceID);
                Assert.AreEqual(13u, readBack.DestinationTag);
                Assert.AreEqual(expiration, readBack.Expiration);
            }
            finally
            {
                client.Dispose();
            }
        }

        [TestMethod]
        [Timeout(120000)]
        public async Task TestICheckCash_DeliverMinLandsOnTheLedger()
        {
            IXrplClient client = await IntegrationTestConfig.CreateClientAsync(nodeType);
            try
            {
                XrplWallet sender = XrplWallet.Generate();
                XrplWallet receiver = XrplWallet.Generate();
                await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, sender, receiver);

                CheckCreate setup = new CheckCreate
                {
                    Account = sender.ClassicAddress,
                    Destination = receiver.ClassicAddress,
                    SendMax = new Currency { ValueAsXrp = 50 },
                };
                await Utils.TestTransaction(client, setup.ToDictionary(), sender);

                AccountObjects created = (await client.AccountObjects(
                    new AccountObjectsRequest(sender.ClassicAddress) { Type = LedgerEntryType.Check })).Result;
                string checkId = created.AccountObjectList.Single().Index;

                // DeliverMin is the CheckCash branch the suite never exercised; rippled takes
                // exactly one of Amount / DeliverMin, which is why both are Optional in the format.
                CheckCash cash = new CheckCash
                {
                    Account = receiver.ClassicAddress,
                    CheckID = checkId,
                    DeliverMin = new Currency { ValueAsXrp = 10 },
                };
                await Utils.TestTransaction(client, cash.ToDictionary(), receiver);

                AccountObjects afterCash = (await client.AccountObjects(
                    new AccountObjectsRequest(sender.ClassicAddress) { Type = LedgerEntryType.Check })).Result;
                Assert.IsEmpty(afterCash.AccountObjectList, "the check must be consumed");
            }
            finally
            {
                client.Dispose();
            }
        }

        [TestMethod]
        [Timeout(120000)]
        public async Task TestINFTokenMint_OfferFieldsLandOnTheLedger()
        {
            IXrplClient client = await IntegrationTestConfig.CreateClientAsync(nodeType);
            try
            {
                XrplWallet minter = XrplWallet.Generate();
                XrplWallet buyer = XrplWallet.Generate();
                await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, minter, buyer);

                DateTime expiration = RippleEpoch.AddSeconds(900000000);

                // Amount/Destination/Expiration are the NFTokenMintOffer fields that TxFormat
                // was missing: minting with them creates the sell offer in the same transaction.
                NFTokenMint mint = new NFTokenMint
                {
                    Account = minter.ClassicAddress,
                    NFTokenTaxon = 0,
                    Flags = NFTokenMintFlags.tfTransferable,
                    Amount = new Currency { ValueAsXrp = 5 },
                    Destination = buyer.ClassicAddress,
                    Expiration = expiration,
                };
                await Utils.TestTransaction(client, mint.ToDictionary(), minter);

                AccountObjects offers = (await client.AccountObjects(
                    new AccountObjectsRequest(minter.ClassicAddress) { Type = LedgerEntryType.NFTokenOffer })).Result;
                LONFTokenOffer offer = offers.AccountObjectList.OfType<LONFTokenOffer>().Single();

                Assert.AreEqual("5000000", offer.Amount.Value, "the mint-time sell offer must carry Amount");
                Assert.AreEqual(buyer.ClassicAddress, offer.Destination);
                Assert.IsNotNull(offer.Expiration, "the mint-time sell offer must carry Expiration");
                Assert.AreEqual(expiration, offer.Expiration.Value);
            }
            finally
            {
                client.Dispose();
            }
        }

        [TestMethod]
        [Timeout(120000)]
        public async Task TestIAccountSet_NewModelFieldsSurviveTheLedgerRoundTrip()
        {
            IXrplClient client = await IntegrationTestConfig.CreateClientAsync(nodeType);
            try
            {
                XrplWallet wallet = XrplWallet.Generate();
                await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, wallet);

                const string walletLocator = "CAFEBABE00000000000000000000000000000000000000000000000000000000";
                const uint operationLimit = 21337;

                // OperationLimit is inert on XRPL but must reach the wire unaltered - this is the
                // Xahau Burn-2-Mint marker the typed API previously could not set at all.
                AccountSet tx = new AccountSet(wallet.ClassicAddress)
                {
                    WalletLocator = walletLocator,
                    WalletSize = 3,
                    OperationLimit = operationLimit,
                };

                Dictionary<string, object> txJson = tx.ToDictionary();
                Submit submitted = await client.Submit(txJson, wallet);
                Assert.AreEqual("tesSUCCESS", submitted.EngineResult);
                string hash = HashOf(submitted);
                await Utils.LedgerAccept(client);

                // Back out of the ledger and into the typed model - the full cycle the models used to break
                TransactionResponse readBack = (await client.TxV1(new TxRequest(hash))).Result;
                Assert.AreEqual(operationLimit, readBack.OperationLimit, "OperationLimit must survive the ledger round trip");

                AccountSetResponse typed = readBack as AccountSetResponse;
                Assert.IsNotNull(typed, "tx must deserialize into AccountSetResponse");
                Assert.AreEqual(walletLocator, typed.WalletLocator);
                Assert.AreEqual(3u, typed.WalletSize);

                AccountInfo info = (await client.AccountInfo(new AccountInfoRequest(wallet.ClassicAddress))).Result;
                Assert.AreEqual(walletLocator, info.AccountData.WalletLocator, "WalletLocator must be stored on the account root");
            }
            finally
            {
                client.Dispose();
            }
        }
    }
}
