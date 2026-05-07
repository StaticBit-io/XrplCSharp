using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Client;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/integration/transactions/checkCancel.ts

namespace XrplTests.Xrpl.ClientLib.Integration
{
    [TestClass]
    public class TestICheckCancel
    {
        public TestContext TestContext { get; set; }
        private static TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;

        [TestMethod]
        [Timeout(60000)]
        public async Task TestRequestMethod()
        {
            IXrplClient client = await IntegrationTestConfig.CreateClientAsync(nodeType);
            try
            {
                XrplWallet wallet1 = XrplWallet.Generate();
                XrplWallet wallet2 = XrplWallet.Generate();
                await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, wallet1, wallet2);

                CheckCreate setupTx = new CheckCreate
                {
                    Account = wallet1.ClassicAddress,
                    Destination = wallet2.ClassicAddress,
                    SendMax = new Currency { ValueAsXrp = 50 }
                };
                Dictionary<string, object> setupJson = setupTx.ToDictionary();
                await Utils.TestTransaction(client, setupJson, wallet1);

                AccountObjectsRequest request1 = new AccountObjectsRequest(wallet1.ClassicAddress) { Type = LedgerEntryType.Check };
                AccountObjects response1 = await client.AccountObjects(request1);
                string checkId = response1.AccountObjectList[0].Index;

                CheckCancel tx = new CheckCancel
                {
                    Account = wallet1.ClassicAddress,
                    CheckID = checkId
                };
                Dictionary<string, object> txJson = tx.ToDictionary();
                await Utils.TestTransaction(client, txJson, wallet1);

                AccountObjectsRequest request2 = new AccountObjectsRequest(wallet1.ClassicAddress) { Type = LedgerEntryType.Check };
                AccountObjects response2 = await client.AccountObjects(request2);
                Assert.IsEmpty(response2.AccountObjectList);
            }
            finally
            {
                client.Dispose();
            }
        }
    }
}
