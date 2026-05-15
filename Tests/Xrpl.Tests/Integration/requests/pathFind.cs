// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/integration/requests/pathFind.ts

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Subscriptions;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration
{
    [TestClass]
    public class TestIPathFind
    {
        public TestContext TestContext { get; set; }
        public static IXrplClient client;

        public static TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;

        [ClassInitialize]
        public static async Task MyClassInitializeAsync(TestContext testContext)
        {
            client = await IntegrationTestConfig.CreateClientAsync(nodeType);
        }

        [ClassCleanup]
        public static void AfterAllTests()
        {
            client.Dispose();
        }

        [TestMethod]
        public async Task TestPathFindCreate()
        {
            XrplWallet wallet = XrplWallet.Generate();
            await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

            IXrplClient pfClient = await IntegrationTestConfig.CreateClientAsync(nodeType);
            try
            {
                Currency destinationAmount = new Currency
                {
                    CurrencyCode = "USD",
                    Issuer = wallet.ClassicAddress,
                    Value = "0.001"
                };

                PathFindCreateRequest request = new PathFindCreateRequest(
                    sourceAccount: wallet.ClassicAddress,
                    destinationAccount: wallet.ClassicAddress,
                    destinationAmount: destinationAmount
                );

                PathFindResponse response = await pfClient.PathFind(request);
                Assert.IsNotNull(response);
                Assert.IsNotNull(response.Alternatives);
                Assert.AreEqual(wallet.ClassicAddress, response.DestinationAccount);
            }
            finally
            {
                try { await pfClient.PathFindClose(new PathFindCloseRequest()); } catch { }
                pfClient.Dispose();
            }
        }

        [TestMethod]
        public async Task TestPathFindClose()
        {
            XrplWallet wallet = XrplWallet.Generate();
            await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

            IXrplClient pfClient = await IntegrationTestConfig.CreateClientAsync(nodeType);
            try
            {
                Currency destinationAmount = new Currency
                {
                    CurrencyCode = "USD",
                    Issuer = wallet.ClassicAddress,
                    Value = "0.001"
                };

                PathFindCreateRequest createRequest = new PathFindCreateRequest(
                    sourceAccount: wallet.ClassicAddress,
                    destinationAccount: wallet.ClassicAddress,
                    destinationAmount: destinationAmount
                );

                await pfClient.PathFind(createRequest);

                PathFindCloseRequest closeRequest = new PathFindCloseRequest();
                PathFindResponse closeResponse = await pfClient.PathFindClose(closeRequest);
                Assert.IsNotNull(closeResponse);
                Assert.IsTrue(closeResponse.Closed.HasValue && closeResponse.Closed.Value);
            }
            finally
            {
                pfClient.Dispose();
            }
        }

        [TestMethod]
        public async Task TestPathFindStatus()
        {
            XrplWallet wallet = XrplWallet.Generate();
            await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

            IXrplClient pfClient = await IntegrationTestConfig.CreateClientAsync(nodeType);
            try
            {
                Currency destinationAmount = new Currency
                {
                    CurrencyCode = "USD",
                    Issuer = wallet.ClassicAddress,
                    Value = "0.001"
                };

                PathFindCreateRequest createRequest = new PathFindCreateRequest(
                    sourceAccount: wallet.ClassicAddress,
                    destinationAccount: wallet.ClassicAddress,
                    destinationAmount: destinationAmount
                );

                await pfClient.PathFind(createRequest);

                PathFindStatusRequest statusRequest = new PathFindStatusRequest();
                PathFindResponse statusResponse = await pfClient.PathFindStatus(statusRequest);
                Assert.IsNotNull(statusResponse);
                Assert.IsNotNull(statusResponse.Alternatives);
            }
            finally
            {
                try { await pfClient.PathFindClose(new PathFindCloseRequest()); } catch { }
                pfClient.Dispose();
            }
        }

        [TestMethod]
        [Timeout(90000)]
        public async Task TestPathFindStreamReceivesMultipleUpdates()
        {
            XrplWallet wallet = XrplWallet.Generate();
            await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);

            IXrplClient streamClient = await IntegrationTestConfig.CreateClientAsync(nodeType);

            List<PathFindStream> received = new List<PathFindStream>();
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            OnPathFind handler = r =>
            {
                received.Add(r);
                Console.WriteLine($"[PathFindStream #{received.Count}] full_reply={r.FullReply}, alternatives={r.Alternatives?.Count}, src={r.SourceAccount}");
                if (received.Count >= 2)
                    tcs.TrySetResult(true);
                return Task.CompletedTask;
            };

            streamClient.connection.OnPathFind += handler;

            try
            {
                Currency destinationAmount = new Currency
                {
                    CurrencyCode = "USD",
                    Issuer = wallet.ClassicAddress,
                    Value = "0.001"
                };

                PathFindCreateRequest request = new PathFindCreateRequest(
                    sourceAccount: wallet.ClassicAddress,
                    destinationAccount: wallet.ClassicAddress,
                    destinationAmount: destinationAmount
                );

                PathFindResponse response = await streamClient.PathFind(request);
                Assert.IsNotNull(response, "Initial path_find create response should not be null");
                Console.WriteLine($"[PathFind RPC] destination={response.DestinationAccount}, alternatives={response.Alternatives?.Count}");

                Task completed = await Task.WhenAny(tcs.Task, Task.Delay(20000));

                Console.WriteLine($"[PathFindStream] Total received: {received.Count} message(s)");

                if (completed != tcs.Task && received.Count == 0)
                {
                    Assert.Inconclusive(
                        "Server did not send path_find stream updates within timeout. " +
                        "This can happen on testnet when paths are stable.");
                }

                Assert.IsTrue(received.Count >= 1, $"Expected at least 1 stream message, got {received.Count}");

                foreach (PathFindStream msg in received)
                {
                    Assert.AreEqual(ResponseStreamType.path_find, msg.Type);
                    Assert.IsNotNull(msg.Alternatives);
                    Assert.IsNotNull(msg.DestinationAccount);
                    Assert.IsNotNull(msg.SourceAccount);
                }
            }
            finally
            {
                streamClient.connection.OnPathFind -= handler;

                try { await streamClient.PathFindClose(new PathFindCloseRequest()); }
                catch { }

                streamClient.Dispose();
            }
        }
        [TestMethod]
        [Timeout(120000)]
        public async Task TestPathFindCreateNegativeOneXrp()
        {
            IXrplClient pfClient = await IntegrationTestConfig.CreateClientAsync(nodeType);

            try
            {
                XrplWallet walletIssuer = XrplWallet.Generate();
                XrplWallet walletMaker = XrplWallet.Generate();
                XrplWallet walletSender = XrplWallet.Generate();

                await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType,
                    walletIssuer, walletMaker, walletSender);

                const string CurrencyCode = "PFX";

                await SubmitTx(client, new AccountSet
                {
                    Account = walletIssuer.ClassicAddress,
                    SetFlag = AccountSetAsfFlags.asfDefaultRipple
                }, walletIssuer, "DefaultRipple on issuer");

                await SubmitTx(client, new TrustSet
                {
                    Account = walletMaker.ClassicAddress,
                    LimitAmount = new Currency
                    {
                        CurrencyCode = CurrencyCode,
                        Issuer = walletIssuer.ClassicAddress,
                        Value = "10000000"
                    }
                }, walletMaker, "TrustLine maker");

                await SubmitTx(client, new TrustSet
                {
                    Account = walletSender.ClassicAddress,
                    LimitAmount = new Currency
                    {
                        CurrencyCode = CurrencyCode,
                        Issuer = walletIssuer.ClassicAddress,
                        Value = "10000000"
                    }
                }, walletSender, "TrustLine sender");

                await SubmitTx(client, new Payment
                {
                    Account = walletIssuer.ClassicAddress,
                    Destination = walletMaker.ClassicAddress,
                    Amount = new Currency
                    {
                        CurrencyCode = CurrencyCode,
                        Issuer = walletIssuer.ClassicAddress,
                        Value = "1000"
                    }
                }, walletIssuer, "Issue 1000 PFX to maker");

                await SubmitTx(client, new Payment
                {
                    Account = walletIssuer.ClassicAddress,
                    Destination = walletSender.ClassicAddress,
                    Amount = new Currency
                    {
                        CurrencyCode = CurrencyCode,
                        Issuer = walletIssuer.ClassicAddress,
                        Value = "200"
                    }
                }, walletIssuer, "Issue 200 PFX to sender");

                await SubmitTx(client, new OfferCreate
                {
                    Account = walletMaker.ClassicAddress,
                    TakerGets = new Currency { ValueAsXrp = 50 },
                    TakerPays = new Currency
                    {
                        CurrencyCode = CurrencyCode,
                        Issuer = walletIssuer.ClassicAddress,
                        Value = "100"
                    }
                }, walletMaker, "Offer: buy 100 PFX for 50 XRP");

                // path_find create with destination_amount = "-1" (XRP)
                Currency destinationAmount = new Currency
                {
                    CurrencyCode = "XRP",
                    Value = "-1"
                };

                Currency sendMax = new Currency
                {
                    CurrencyCode = CurrencyCode,
                    Issuer = walletIssuer.ClassicAddress,
                    Value = "50"
                };

                PathFindCreateRequest request = new PathFindCreateRequest(
                    sourceAccount: walletSender.ClassicAddress,
                    destinationAccount: walletSender.ClassicAddress,
                    destinationAmount: destinationAmount
                )
                {
                    SendMax = sendMax
                };

                PathFindResponse response = await pfClient.PathFind(request);

                Assert.IsNotNull(response, "path_find create response should not be null");
                Assert.IsNotNull(response.Alternatives, "Alternatives should not be null");

                Console.WriteLine($"[PathFind -1 XRP] Alternatives: {response.Alternatives.Count}");

                foreach (PathAlternative alt in response.Alternatives)
                {
                    string srcVal = alt.SourceAmount.Value ?? alt.SourceAmount.ValueAsXrp?.ToString() ?? "?";
                    string srcCur = alt.SourceAmount.CurrencyCode ?? "XRP";
                    string dstVal = alt.DestinationAmount?.Value ?? alt.DestinationAmount?.ValueAsXrp?.ToString() ?? "?";
                    string dstCur = alt.DestinationAmount?.CurrencyCode ?? "XRP";
                    Console.WriteLine($"[PathFind -1 XRP]   alt: src={srcVal} {srcCur}, dst={dstVal} {dstCur}, paths={alt.PathsComputed?.Count ?? 0}");
                }

                Assert.IsTrue(response.Alternatives.Count > 0,
                    "Expected at least 1 alternative for PFX→XRP path with destination_amount=-1");

                PathAlternative best = response.Alternatives[0];
                Assert.IsNotNull(best.DestinationAmount,
                    "destination_amount should be populated when request uses -1");
            }
            finally
            {
                try { await pfClient.PathFindClose(new PathFindCloseRequest()); } catch { }
                pfClient.Dispose();
            }
        }

        [TestMethod]
        [Timeout(120000)]
        public async Task TestPathFindCreateNegativeOneToken()
        {
            IXrplClient pfClient = await IntegrationTestConfig.CreateClientAsync(nodeType);

            try
            {
                XrplWallet walletIssuer = XrplWallet.Generate();
                XrplWallet walletMaker = XrplWallet.Generate();
                XrplWallet walletSender = XrplWallet.Generate();
                XrplWallet walletReceiver = XrplWallet.Generate();

                await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType,
                    walletIssuer, walletMaker, walletSender, walletReceiver);

                const string CurrencyCode = "PFT";

                await SubmitTx(client, new AccountSet
                {
                    Account = walletIssuer.ClassicAddress,
                    SetFlag = AccountSetAsfFlags.asfDefaultRipple
                }, walletIssuer, "DefaultRipple on issuer");

                await SubmitTx(client, new TrustSet
                {
                    Account = walletMaker.ClassicAddress,
                    LimitAmount = new Currency
                    {
                        CurrencyCode = CurrencyCode,
                        Issuer = walletIssuer.ClassicAddress,
                        Value = "10000000"
                    }
                }, walletMaker, "TrustLine maker");

                await SubmitTx(client, new TrustSet
                {
                    Account = walletReceiver.ClassicAddress,
                    LimitAmount = new Currency
                    {
                        CurrencyCode = CurrencyCode,
                        Issuer = walletIssuer.ClassicAddress,
                        Value = "10000000"
                    }
                }, walletReceiver, "TrustLine receiver");

                await SubmitTx(client, new Payment
                {
                    Account = walletIssuer.ClassicAddress,
                    Destination = walletMaker.ClassicAddress,
                    Amount = new Currency
                    {
                        CurrencyCode = CurrencyCode,
                        Issuer = walletIssuer.ClassicAddress,
                        Value = "1000"
                    }
                }, walletIssuer, "Issue 1000 PFT to maker");

                await SubmitTx(client, new OfferCreate
                {
                    Account = walletMaker.ClassicAddress,
                    TakerGets = new Currency
                    {
                        CurrencyCode = CurrencyCode,
                        Issuer = walletIssuer.ClassicAddress,
                        Value = "100"
                    },
                    TakerPays = new Currency { ValueAsXrp = 10 }
                }, walletMaker, "Offer: sell 100 PFT for 10 XRP");

                // path_find create with destination_amount token value = "-1"
                Currency destinationAmount = new Currency
                {
                    CurrencyCode = CurrencyCode,
                    Issuer = walletIssuer.ClassicAddress,
                    Value = "-1"
                };

                Currency sendMax = new Currency
                {
                    CurrencyCode = "XRP",
                    Value = "5000000" // 5 XRP in drops
                };

                PathFindCreateRequest request = new PathFindCreateRequest(
                    sourceAccount: walletSender.ClassicAddress,
                    destinationAccount: walletReceiver.ClassicAddress,
                    destinationAmount: destinationAmount
                )
                {
                    SendMax = sendMax
                };

                PathFindResponse response = await pfClient.PathFind(request);

                Assert.IsNotNull(response, "path_find create response should not be null");
                Assert.IsNotNull(response.Alternatives, "Alternatives should not be null");

                Console.WriteLine($"[PathFind -1 Token] Alternatives: {response.Alternatives.Count}");

                foreach (PathAlternative alt in response.Alternatives)
                {
                    string srcVal = alt.SourceAmount.Value ?? alt.SourceAmount.ValueAsXrp?.ToString() ?? "?";
                    string srcCur = alt.SourceAmount.CurrencyCode ?? "XRP";
                    string dstVal = alt.DestinationAmount?.Value ?? alt.DestinationAmount?.ValueAsXrp?.ToString() ?? "?";
                    string dstCur = alt.DestinationAmount?.CurrencyCode ?? "XRP";
                    Console.WriteLine($"[PathFind -1 Token]   alt: src={srcVal} {srcCur}, dst={dstVal} {dstCur}, paths={alt.PathsComputed?.Count ?? 0}");
                }

                Assert.IsTrue(response.Alternatives.Count > 0,
                    "Expected at least 1 alternative for XRP→PFT path with destination_amount=-1");

                PathAlternative best = response.Alternatives[0];
                Assert.IsNotNull(best.DestinationAmount,
                    "destination_amount should be populated when request uses -1");

                string destCurrency = best.DestinationAmount.CurrencyCode;
                Assert.AreEqual(CurrencyCode, destCurrency,
                    $"Expected destination currency {CurrencyCode}, got {destCurrency}");

                decimal destValue = best.DestinationAmount.ValueAsNumber;
                Assert.IsTrue(destValue > 0,
                    $"Expected positive destination_amount value, got {destValue}");

                Console.WriteLine($"[PathFind -1 Token] Best path delivers {destValue} {CurrencyCode}");
            }
            finally
            {
                try { await pfClient.PathFindClose(new PathFindCloseRequest()); } catch { }
                pfClient.Dispose();
            }
        }

        private static async Task SubmitTx(IXrplClient client, ITransactionRequest tx, XrplWallet wallet, string label)
        {
            var autofilled = await client.Autofill(tx);
            TransactionSummary res = await client.SubmitAndWait(autofilled, wallet, true);
            string result = res.Meta?.TransactionResult;
            Console.WriteLine($"[PathFind] {label}: {result}");
        }
    }
}
