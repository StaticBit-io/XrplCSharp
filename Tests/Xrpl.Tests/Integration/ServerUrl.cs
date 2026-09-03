namespace XrplTests.Xrpl.ClientLib.Integration
{
    public class ServerUrl
    {
        /// <summary>
        /// WebSocket URL of the node under test, resolved from the active
        /// <see cref="IntegrationTestConfig"/> profile (XRPL_TEST_NODE / XRPL_TEST_NODE_URL).
        /// </summary>
        public static string serverUrl => IntegrationTestConfig.GetNodeUrl(IntegrationTestConfig.CurrentNodeType);
    }
}