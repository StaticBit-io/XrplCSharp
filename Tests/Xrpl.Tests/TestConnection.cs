using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

using Xrpl.BinaryCodec.Types;
using Xrpl.Client;
using Xrpl.Client.Exceptions;

using XrplTests;

using static Xrpl.Client.Connection;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/connection.ts

namespace Xrpl.Tests
{

    [TestClass]
    public class TestUConnection
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
            if (runner?.client != null)
            {
                await runner.client.Disconnect();
            }
        }

        [TestMethod]
        public void TestDefaultOptions()
        {
            ConnectionOptions options = new ConnectionOptions();
            Connection connection = new Connection("url", options);
            Assert.AreEqual("url", connection.GetUrl());
            Assert.IsNull(connection.config.proxy);
            Assert.IsNull(connection.config.authorization);
        }

        //[TestMethod]
        //public async void TestMultipleDisconnect()
        //{
        //    await runner.client.Disconnect();
        //    await runner.client.Disconnect();
        //}

        //[TestMethod]
        //public void TestReconnect()
        //{
        //    runner.client.connection.Reconnect();
        //}

        [TestMethod]
        public async Task TestNotConnectedException()
        {
            ConnectionOptions options = new ConnectionOptions(){RequestPolicy = RequestFailurePolicy.ImmediateFail};
            Connection connection = new Connection("url", options);

            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                { "command", "ledger" },
                { "ledger_index", "validated" },
            };
            await Helper.ThrowsExceptionAsync<NotConnectedException>(() => connection.Request(tx, null));
        }

        [TestMethod]
        public async Task TestDisconnectedError()
        {
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                { "command", "test_command" },
                { "data", new Dictionary<string, object> {
                   { "closeServer", true },
                } },
            };
            await runner.client.Disconnect();
            await Helper.ThrowsExceptionAsync<NotConnectedException>(() => runner.client.Request(tx));
        }

        //[TestMethod]
        //public void TestTimeoutError()
        //{
        //    
        //}

        //[TestMethod]
        //public void TestDisconnectedErrorOnSend()
        //{
        //    
        //}

        //[TestMethod]
        //public void TestDisconnectedErrorOnInitial()
        //{
        //    
        //}

        //[TestMethod]
        //public void TestResponseFormatError()
        //{
        //    
        //}

        //[TestMethod]
        //public void TestReconnectUnexpected()
        //{
        //    
        //}

        //[TestMethod]
        //public void TestReconnectUnexpected()
        //{
        //    
        //}

        [TestMethod]
        public async Task TestNoCrashError()
        {
            runner.mockedRippled.suppressOutput = true;
            Dictionary<string, object> tx = new Dictionary<string, object>
            {
                { "command", "test_garbage" },
            };
            await Helper.ThrowsExceptionAsync<XrplException>(() => runner.client.connection.Request(tx));
        }
    }
}
