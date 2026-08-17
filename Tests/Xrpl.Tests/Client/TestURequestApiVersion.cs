using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Models.Methods;

namespace Xrpl.Tests.ClientLib
{
    /// <summary>
    /// The untyped <see cref="XrplClient.Request(Dictionary{string, object}, System.Threading.CancellationToken)"/>
    /// used to stamp the version under <c>nameof(ApiVersion)</c> — literally <c>"ApiVersion"</c>.
    /// rippled knows only <c>api_version</c>, ignores anything else and falls back to API v1, so
    /// the client's configured version never reached the node and the two request paths of one
    /// client spoke different protocol versions. These tests read what actually goes on the wire,
    /// because a field the node ignores is invisible from the response.
    /// </summary>
    /// <remarks>
    /// The untyped calls below deliberately use a different command from the typed ones:
    /// <see cref="XrplClient.Connect"/> issues a typed <c>server_info</c> of its own, so matching
    /// on that command alone cannot tell the two paths apart.
    /// </remarks>
    [TestClass]
    public class TestURequestApiVersion
    {
        private const string UntypedCommand = "ledger_current";

        private static uint? ApiVersionOf(string request)
        {
            using JsonDocument document = JsonDocument.Parse(request);
            return document.RootElement.TryGetProperty("api_version", out JsonElement version)
                ? version.GetUInt32()
                : null;
        }

        private static void AssertNoMemberNameOnTheWire(string request)
        {
            using JsonDocument document = JsonDocument.Parse(request);
            Assert.IsFalse(
                document.RootElement.TryGetProperty("ApiVersion", out _),
                $"the C# member name must not reach the wire, rippled ignores it: {request}");
        }

        [TestMethod]
        public async Task TestUntypedRequestSendsTheWireFieldName()
        {
            using RequestCapturingServer server = new RequestCapturingServer();
            using XrplClient client = new XrplClient(server.Url, new XrplClient.ClientOptions { ApiVersion = 2 });

            await client.Connect().ConfigureAwait(false);
            await client.Request(new Dictionary<string, object> { ["command"] = UntypedCommand }).ConfigureAwait(false);
            await client.Disconnect().ConfigureAwait(false);

            string sent = server.LastRequestFor(UntypedCommand);
            Assert.IsNotNull(sent, "the server saw no untyped request");

            Assert.AreEqual(2u, ApiVersionOf(sent), $"the untyped path dropped the client's version: {sent}");
            AssertNoMemberNameOnTheWire(sent);
        }

        [TestMethod]
        public async Task TestUntypedRequestKeepsAVersionTheCallerSet()
        {
            using RequestCapturingServer server = new RequestCapturingServer();
            using XrplClient client = new XrplClient(server.Url, new XrplClient.ClientOptions { ApiVersion = 2 });

            await client.Connect().ConfigureAwait(false);
            await client.Request(new Dictionary<string, object>
            {
                ["command"] = UntypedCommand,
                ["api_version"] = 1
            }).ConfigureAwait(false);
            await client.Disconnect().ConfigureAwait(false);

            string sent = server.LastRequestFor(UntypedCommand);
            Assert.IsNotNull(sent);

            Assert.AreEqual(1u, ApiVersionOf(sent), $"an explicit api_version must not be overwritten: {sent}");
            AssertNoMemberNameOnTheWire(sent);
        }

        /// <summary>
        /// The typed path was always correct — <see cref="BaseRequest.ApiVersion"/> carries
        /// <c>[JsonPropertyName("api_version")]</c>. Both paths are pinned together here because
        /// the defect was precisely that one client spoke two protocol versions depending on which
        /// method the caller reached for.
        /// </summary>
        [TestMethod]
        public async Task TestBothRequestPathsSendTheSameVersion()
        {
            using RequestCapturingServer server = new RequestCapturingServer();
            using XrplClient client = new XrplClient(server.Url, new XrplClient.ClientOptions { ApiVersion = 2 });

            await client.Connect().ConfigureAwait(false);
            await client.ServerInfo(new ServerInfoRequest()).ConfigureAwait(false);
            await client.Request(new Dictionary<string, object> { ["command"] = UntypedCommand }).ConfigureAwait(false);
            await client.Disconnect().ConfigureAwait(false);

            string typed = server.LastRequestFor("server_info");
            string untyped = server.LastRequestFor(UntypedCommand);

            Assert.IsNotNull(typed, "the server saw no typed request");
            Assert.IsNotNull(untyped, "the server saw no untyped request");

            Assert.AreEqual(2u, ApiVersionOf(typed), $"typed request: {typed}");
            Assert.AreEqual(2u, ApiVersionOf(untyped), $"untyped request: {untyped}");
            AssertNoMemberNameOnTheWire(untyped);
        }
    }
}
