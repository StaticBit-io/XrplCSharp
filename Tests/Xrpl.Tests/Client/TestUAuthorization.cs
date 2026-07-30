using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Models.Methods;

using XrplTests;

using static Xrpl.Client.Connection;

namespace Xrpl.Tests
{
    /// <summary>
    /// Covers the two authentication mechanisms rippled exposes:
    /// <list type="bullet">
    /// <item>HTTP Basic on the WebSocket upgrade handshake — <c>user</c>/<c>password</c> of a port stanza,
    /// in practice consumed by a reverse proxy in front of the node (xrpl.js calls this <c>authorization</c>).</item>
    /// <item><c>admin_user</c>/<c>admin_password</c> carried inside the request JSON, which is the only way
    /// to reach admin commands over ws/wss.</item>
    /// </list>
    /// </summary>
    [TestClass]
    public class TestUAuthorization
    {
        private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);

        private static async Task<string> CaptureHandshakeAsync(Action<ConnectionOptions> configure)
        {
            using HandshakeCapturingServer server = new HandshakeCapturingServer();

            ConnectionOptions options = new ConnectionOptions
            {
                MaxReconnectAttempts = 0,
                UseCustomPing = false,
            };
            configure(options);

            Connection connection = new Connection(server.Url, options);
            try
            {
                _ = connection.Connect(System.Threading.CancellationToken.None);
                return await server.WaitForHandshakeAsync(HandshakeTimeout);
            }
            finally
            {
                try
                {
                    await connection.Disconnect();
                }
                catch
                {
                    // the fake server never speaks the WebSocket protocol beyond the handshake
                }
            }
        }

        [TestMethod]
        public async Task TestAuthorizationSendsBasicHeaderOnHandshake()
        {
            string handshake = await CaptureHandshakeAsync(options => options.authorization = "user:pass");

            // base64("user:pass") == "dXNlcjpwYXNz"
            StringAssert.Contains(handshake, "Authorization: Basic dXNlcjpwYXNz");
        }

        [TestMethod]
        public async Task TestNoAuthorizationSendsNoAuthorizationHeader()
        {
            string handshake = await CaptureHandshakeAsync(_ => { });

            Assert.IsFalse(
                handshake.Contains("Authorization:", StringComparison.OrdinalIgnoreCase),
                $"Handshake unexpectedly carried an Authorization header:\n{handshake}");
        }

        [TestMethod]
        public async Task TestCustomHeadersAreSentOnHandshake()
        {
            string handshake = await CaptureHandshakeAsync(options =>
                options.headers = new Dictionary<string, string> { { "X-Api-Key", "abc123" } });

            StringAssert.Contains(handshake, "X-Api-Key: abc123");
        }

        [TestMethod]
        public void TestAdminCredentialsAddedToTypedRequest()
        {
            RequestManager manager = new RequestManager();

            RequestManager.XrplGRequest request = manager.CreateGRequest<object, BaseRequest>(
                new BaseRequest { Command = "ledger_accept" },
                timeout: System.Threading.Timeout.InfiniteTimeSpan,
                adminCredentials: new AdminCredentials("root", "passw0rd"));

            StringAssert.Contains(request.Message, "\"admin_user\":\"root\"");
            StringAssert.Contains(request.Message, "\"admin_password\":\"passw0rd\"");
        }

        [TestMethod]
        public void TestAdminCredentialsAddedToDictionaryRequest()
        {
            RequestManager manager = new RequestManager();

            RequestManager.XrplRequest request = manager.CreateRequest(
                new Dictionary<string, object> { { "command", "ledger_accept" } },
                timeout: System.Threading.Timeout.InfiniteTimeSpan,
                adminCredentials: new AdminCredentials("root", "passw0rd"));

            StringAssert.Contains(request.Message, "\"admin_user\":\"root\"");
            StringAssert.Contains(request.Message, "\"admin_password\":\"passw0rd\"");
        }

        [TestMethod]
        public void TestRequestWithoutAdminCredentialsCarriesNoAdminFields()
        {
            RequestManager manager = new RequestManager();

            RequestManager.XrplRequest request = manager.CreateRequest(
                new Dictionary<string, object> { { "command", "ledger_accept" } },
                timeout: System.Threading.Timeout.InfiniteTimeSpan);

            Assert.IsFalse(
                request.Message.Contains("admin_user", StringComparison.Ordinal),
                $"Unexpected admin_user in request: {request.Message}");
            Assert.IsFalse(
                request.Message.Contains("admin_password", StringComparison.Ordinal),
                $"Unexpected admin_password in request: {request.Message}");
        }

        [TestMethod]
        public async Task TestAdminPasswordIsNotLeakedIntoTimeoutMessage()
        {
            RequestManager manager = new RequestManager();

            RequestManager.XrplGRequest request = manager.CreateGRequest<object, BaseRequest>(
                new BaseRequest { Command = "ledger_accept" },
                timeout: TimeSpan.FromMilliseconds(50),
                adminCredentials: new AdminCredentials("root", "passw0rd"));

            // The credentials go on the wire...
            StringAssert.Contains(request.Message, "passw0rd");

            // ...but the timeout message is logged by consumers, so it must stay clean.
            Xrpl.Client.Exceptions.TimeoutException error =
                await Helper.ThrowsExceptionAsync<Xrpl.Client.Exceptions.TimeoutException>(() => request.Promise);

            Assert.IsFalse(
                error.Message.Contains("passw0rd", StringComparison.Ordinal),
                $"Admin password leaked into the timeout message: {error.Message}");
        }
    }
}
