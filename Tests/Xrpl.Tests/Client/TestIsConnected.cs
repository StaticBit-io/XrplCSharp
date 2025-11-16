using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;
using Xrpl.Models.Methods;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/client/isConnected.ts

namespace Xrpl.Tests.ClientLib
{
    [TestClass]
    public class TestUIsConnected
    {

        public static SetupUnitClient runner;

        [TestInitialize]
        public async Task MyTestInitializeAsync()
        {
            runner = await new SetupUnitClient().SetupClient();
        }

        [TestCleanup]
        public async Task MyTestCleanupAsync()
        {
            await runner.client.Disconnect();
        }

        [TestMethod]
        public async Task TestConnectedDisconnect()
        {
            Assert.IsTrue(runner.client.IsConnected());
            await runner.client.Disconnect();
            Assert.IsFalse(runner.client.IsConnected());
        }
    }
}

