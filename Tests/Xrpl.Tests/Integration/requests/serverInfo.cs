

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/integration/requests/serverInfo.ts

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Models.Methods;

using Xrpl.Client;
namespace XrplTests.Xrpl.ClientLib.Integration
{
    [TestClass]
    public class TestIServerInfo
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
            ServerInfoRequest request = new ServerInfoRequest();
            ServerInfo response = await runner.client.ServerInfo(request).Typed();
            Assert.IsNotNull(response);
        }
    }
}