using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/integration/transactions/accountDelete.ts

namespace XrplTests.Xrpl.ClientLib.Integration
{
    [TestClass]
    [DoNotParallelize] // requires multiple ledger skip transactions to be submitted after 256 ledgers, and may cause issues if run in parallel with other tests
    public class TestIAccountDelete
    {
        // private static int Timeout = 20;
        public TestContext TestContext { get; set; }
        public static SetupIntegration runner;

        [ClassInitialize]
        public static async Task MyClassInitializeAsync(TestContext testContext)
        {
            runner = await new SetupIntegration().SetupClient(ServerUrl.serverUrl);
        }

        [TestMethod]
        public async Task TestRequestMethod()
        {
            XrplWallet wallet2 = await Utils.GenerateFundedWallet(runner.client);

            for (int iter = 0; iter < 256; iter++)
            {
                await Utils.LedgerAccept(runner.client);
            }

            LedgerIndex index = new LedgerIndex(LedgerIndexType.Validated);
            AccountChannelsRequest request = new AccountChannelsRequest(runner.wallet.ClassicAddress) { LedgerIndex = index };
            AccountChannels response = await runner.client.AccountChannels(request);
            Assert.IsNotNull(response);
            AccountDelete tx = new AccountDelete
            {
                Account = runner.wallet.ClassicAddress,
                Destination = wallet2.ClassicAddress,
            };
            Dictionary<string, object> txJson = tx.ToDictionary();
            await Utils.TestTransaction(runner.client, txJson, runner.wallet);
        }
    }
}