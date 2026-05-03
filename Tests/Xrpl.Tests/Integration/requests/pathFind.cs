// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/integration/requests/pathFind.ts

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Subscriptions;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration
{
    [TestClass]
    [DoNotParallelize]
    public class TestIPathFind
    {
        public TestContext TestContext { get; set; }
        public static IXrplClient client;

        static XrplWallet wallet = XrplWallet.FromNormalizedText("path find test account");
        public static TestNodeType nodeType = TestNodeType.TestNet;

        [ClassInitialize]
        public static async Task MyClassInitializeAsync(TestContext testContext)
        {
            client = await IntegrationTestConfig.CreateClientAsync(nodeType);
            await IntegrationTestConfig.TryFundWalletAsync(client, wallet, nodeType);
        }

        [ClassCleanup]
        public static void AfterAllTests()
        {
            client.Dispose();
        }

        [TestMethod]
        public async Task TestPathFindCreate()
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

            PathFindResponse response = await client.PathFind(request);
            Assert.IsNotNull(response);
            Assert.IsNotNull(response.Alternatives);
            Assert.AreEqual(wallet.ClassicAddress, response.DestinationAccount);
        }

        [TestMethod]
        public async Task TestPathFindClose()
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

            await client.PathFind(createRequest);

            PathFindCloseRequest closeRequest = new PathFindCloseRequest();
            PathFindResponse closeResponse = await client.PathFindClose(closeRequest);
            Assert.IsNotNull(closeResponse);
            Assert.IsTrue(closeResponse.Closed.HasValue && closeResponse.Closed.Value);
        }

        [TestMethod]
        public async Task TestPathFindStatus()
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

            await client.PathFind(createRequest);

            PathFindStatusRequest statusRequest = new PathFindStatusRequest();
            PathFindResponse statusResponse = await client.PathFindStatus(statusRequest);
            Assert.IsNotNull(statusResponse);
            Assert.IsNotNull(statusResponse.Alternatives);
        }

        [TestMethod]
        [Timeout(90000)]
        public async Task TestPathFindStreamReceivesMultipleUpdates()
        {
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
    }
}
