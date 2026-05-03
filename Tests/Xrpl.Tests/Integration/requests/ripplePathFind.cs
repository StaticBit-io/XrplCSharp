// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/integration/requests/ripplePathFind.ts

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration
{
    [TestClass]
    [DoNotParallelize]
    public class TestIRipplePathFind
    {
        public TestContext TestContext { get; set; }
        public static IXrplClient client;

        static XrplWallet wallet = XrplWallet.FromNormalizedText("ripple path find test account");
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
        public async Task TestRequestMethod()
        {
            Currency destinationAmount = new Currency
            {
                CurrencyCode = "USD",
                Issuer = wallet.ClassicAddress,
                Value = "0.001"
            };

            RipplePathFindRequest request = new RipplePathFindRequest(
                sourceAccount: wallet.ClassicAddress,
                destinationAccount: wallet.ClassicAddress,
                destinationAmount: destinationAmount
            );

            RipplePathFindResponse response = await client.RipplePathFind(request);
            Assert.IsNotNull(response);
            Assert.IsNotNull(response.Alternatives);
            Assert.IsNotNull(response.DestinationCurrencies);
        }

        [TestMethod]
        public async Task TestRequestWithSourceCurrencies()
        {
            Currency destinationAmount = new Currency
            {
                CurrencyCode = "USD",
                Issuer = wallet.ClassicAddress,
                Value = "0.001"
            };

            RipplePathFindRequest request = new RipplePathFindRequest(
                sourceAccount: wallet.ClassicAddress,
                destinationAccount: wallet.ClassicAddress,
                destinationAmount: destinationAmount
            )
            {
                SourceCurrencies = new List<SourceCurrency>
                {
                    new SourceCurrency { Currency = "XRP" },
                    new SourceCurrency { Currency = "USD" }
                }
            };

            RipplePathFindResponse response = await client.RipplePathFind(request);
            Assert.IsNotNull(response);
            Assert.IsNotNull(response.Alternatives);
        }
    }
}
