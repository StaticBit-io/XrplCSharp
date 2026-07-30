using Microsoft.VisualStudio.TestTools.UnitTesting;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;

using XrplTests;

namespace Xrpl.Tests.Integration
{
    /// <summary>
    /// Verifies that <see cref="XrplClient.ClientOptions.AdminUser"/>/<see cref="XrplClient.ClientOptions.AdminPassword"/>
    /// actually unlock rippled admin commands over WebSocket.
    /// <para>
    /// Runs against <c>[port_ws_admin_auth]</c> of the standalone stand (port 6007), which sets
    /// <c>admin_user</c>/<c>admin_password</c>. rippled carries these credentials in the request JSON,
    /// not in an HTTP header — Basic auth on the ws handshake is never checked by the node itself.
    /// </para>
    /// </summary>
    [TestClass]
    public class TestIAdminCredentials
    {
        private const string AdminUser = "xrpl_admin";
        private const string AdminPassword = "xrpl_admin_secret";
        private const string ServerUrl = "ws://127.0.0.1:6007";

        private static readonly Dictionary<string, object> LedgerAccept = new()
        {
            { "command", "ledger_accept" },
        };

        private static XrplClient CreateClient(bool withCredentials)
        {
            XrplClient.ClientOptions options = new XrplClient.ClientOptions
            {
                RequestPolicy = RequestFailurePolicy.ImmediateFail,
                UseCustomPing = false,
            };

            if (withCredentials)
            {
                options.AdminUser = AdminUser;
                options.AdminPassword = AdminPassword;
            }

            return new XrplClient(ServerUrl, options);
        }

        [TestMethod]
        public async Task TestAdminCommandIsRejectedWithoutCredentials()
        {
            XrplClient client = CreateClient(withCredentials: false);
            await client.Connect();
            try
            {
                RippledException error = await Helper.ThrowsExceptionAsync<RippledException>(
                    () => client.Request(new Dictionary<string, object>(LedgerAccept)));

                // A port that sets admin_user/admin_password answers Role::FORBID — rippled rejects the
                // missing credentials outright rather than demoting the client to guest and replying noPermission.
                StringAssert.Contains(error.Message, "forbidden");
                StringAssert.Contains(error.Message, "Bad credentials");
            }
            finally
            {
                await client.Disconnect();
            }
        }

        [TestMethod]
        public async Task TestAdminCommandSucceedsWithCredentials()
        {
            XrplClient client = CreateClient(withCredentials: true);
            await client.Connect();
            try
            {
                Dictionary<string, object> response =
                    await client.Request(new Dictionary<string, object>(LedgerAccept));

                Assert.IsTrue(
                    response.ContainsKey("ledger_current_index"),
                    $"ledger_accept did not return a ledger index: {string.Join(", ", response.Keys)}");
            }
            finally
            {
                await client.Disconnect();
            }
        }
    }
}
