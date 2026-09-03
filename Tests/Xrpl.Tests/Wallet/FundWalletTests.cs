using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Client;
using Xrpl.Wallet;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/wallet/fundWallet.ts

namespace Xrpl.Tests.Wallet.Tests
{
    [TestClass]
    public class TestUFundWallet
    {
        //[TestMethod]
        public async Task TestUFaucetHostsAsync()
        {
            string serverUrl = "wss://s.altnet.rippletest.net:51233";
            XrplClient client = new XrplClient(serverUrl);
            await client.Connect();
            XrplWallet wallet = XrplWallet.Generate();
            await WalletSugar.FundWallet(client, wallet);
        }
    }
}
