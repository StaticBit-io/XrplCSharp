using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Client;
using Xrpl.Client.Exceptions;

using XrplTests.Xrpl.ClientLib.Integration;

namespace Xrpl.Tests.Integration;

/// <summary>
/// Connection lifecycle against the local standalone node.
/// </summary>
/// <remarks>
/// These tests used to point at the public testnet and devnet, which made them depend on
/// third-party availability and latency inside a suite that is otherwise hermetic — they
/// failed intermittently for reasons that had nothing to do with the SDK. Nothing here is
/// specific to a public network: every assertion is about the client's own state machine,
/// so the local node serves the purpose and the run becomes deterministic and offline.
///
/// Fixed sleeps were the other half of the flakiness; state is now awaited with a timeout
/// instead of guessed at.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
[TestCategory("TestI")]
public class TestIConnectionStates
{
    private static string LocalServer => IntegrationTestConfig.GetNodeUrl(IntegrationTestConfig.CurrentNodeType);

    /// <summary>
    /// The same node under a different URL spelling — enough to exercise a real server switch
    /// (teardown plus reconnect to a new endpoint) without a second container.
    /// </summary>
    /// <remarks>
    /// Null when the spelling cannot be varied, which is any address that does not say
    /// <c>localhost</c>. Switching to the string already in use is not a server switch, and the
    /// assertion that the client passed through Connecting would hold without one, so the test
    /// that needs it skips instead of passing on nothing.
    /// </remarks>
    private static string LocalServerAlternateSpelling
    {
        get
        {
            string alternate = LocalServer.Replace("localhost", "127.0.0.1", StringComparison.OrdinalIgnoreCase);
            return string.Equals(alternate, LocalServer, StringComparison.Ordinal) ? null : alternate;
        }
    }

    /// <summary>
    /// A closed port on the loopback interface: refuses immediately and, unlike a bogus
    /// hostname, involves no DNS resolver and so no external dependency.
    /// </summary>
    private const string UnreachableServer = "ws://127.0.0.1:1";

    private static XrplClient.ClientOptions LocalOptions() => new XrplClient.ClientOptions
    {
        MaxReconnectAttempts = 3,
        StopAfterMaxAttempts = true,
        ConnectionAttemptTimeout = TimeSpan.FromSeconds(15),
        ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(30)
    };

    private static async Task WaitForStateAsync(
        XrplClient client,
        XrpConnectionState expected,
        string because,
        int timeoutMs = 20000)
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < timeoutMs)
        {
            if (client.connection.CurrentConnectionState == expected)
                return;
            await Task.Delay(50);
        }

        Assert.AreEqual(expected, client.connection.CurrentConnectionState, $"{because} (waited {timeoutMs} ms)");
    }

    [TestMethod]
    public async Task TestConnectionStateSequence_ConnectDisconnect()
    {
        List<XrpConnectionState> stateChanges = new List<XrpConnectionState>();

        XrplClient client = new XrplClient(LocalServer, LocalOptions());

        client.connection.OnConnectionStatus += (status) =>
        {
            stateChanges.Add(status.ConnectionState);
            Console.WriteLine($"State: {status.ConnectionState}, Message: {status.Message}");
        };

        Assert.AreEqual(XrpConnectionState.Disconnected, client.connection.CurrentConnectionState, "Initial state should be Disconnected");

        await client.Connect();
        await WaitForStateAsync(client, XrpConnectionState.Connected, "Current state should be Connected");

        Assert.IsTrue(stateChanges.Contains(XrpConnectionState.Connecting), "Should have Connecting state during connection");
        Assert.IsTrue(stateChanges.Contains(XrpConnectionState.Connected), "Should have Connected state after connection");

        stateChanges.Clear();

        await client.Disconnect();
        await WaitForStateAsync(client, XrpConnectionState.Disconnected, "Current state should be Disconnected");

        Assert.IsTrue(stateChanges.Contains(XrpConnectionState.Disconnected), "Should have Disconnected state after user disconnect");
    }

    [TestMethod]
    public async Task TestConnectionStateReconnect_InvalidServer()
    {
        List<XrpConnectionState> stateChanges = new List<XrpConnectionState>();
        int reconnectAttempts = 0;
        TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

        XrplClient client = new XrplClient(UnreachableServer, new XrplClient.ClientOptions
        {
            MaxReconnectAttempts = 2,
            StopAfterMaxAttempts = true,
            ConnectionAttemptTimeout = TimeSpan.FromSeconds(5),
            ReconnectBaseDelay = TimeSpan.FromSeconds(1),
            ReconnectMaxDelay = TimeSpan.FromSeconds(2)
        });

        client.connection.OnConnectionStatus += (status) =>
        {
            stateChanges.Add(status.ConnectionState);
            Console.WriteLine($"State: {status.ConnectionState}, Message: {status.Message}");

            if (status.ConnectionState == XrpConnectionState.RestoringConnection && status.Reconnect != null)
            {
                reconnectAttempts++;
            }

            if (status.ConnectionState == XrpConnectionState.Disconnected && status.Message.Contains("stopped"))
            {
                tcs.TrySetResult(true);
            }
        };

        try
        {
            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await client.Connect(cts.Token);
        }
        catch
        {
        }

        Task terminal = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.AreSame(tcs.Task, terminal, "Reconnect exhaustion was not observed before the 30s timeout");
        await tcs.Task;

        Assert.IsTrue(stateChanges.Contains(XrpConnectionState.Connecting), "Should have Connecting state");
        Assert.IsTrue(reconnectAttempts >= 1, $"Expected at least one reconnect attempt, got {reconnectAttempts}");

        await client.Disconnect();
    }

    [TestMethod]
    public async Task TestCurrentConnectionStateProperty()
    {
        XrplClient client = new XrplClient(LocalServer, LocalOptions());

        Assert.AreEqual(XrpConnectionState.Disconnected, client.connection.CurrentConnectionState, "Initial CurrentConnectionState should be Disconnected");

        await client.Connect();
        await WaitForStateAsync(client, XrpConnectionState.Connected, "After connect, CurrentConnectionState should be Connected");

        await client.Disconnect();
        await WaitForStateAsync(client, XrpConnectionState.Disconnected, "After disconnect, CurrentConnectionState should be Disconnected");
    }

    [TestMethod]
    public async Task TestIdempotentConnect_StaysConnected()
    {
        List<XrpConnectionState> stateChanges = new List<XrpConnectionState>();

        XrplClient client = new XrplClient(LocalServer, LocalOptions());

        client.connection.OnConnectionStatus += (status) =>
        {
            stateChanges.Add(status.ConnectionState);
            Console.WriteLine($"State: {status.ConnectionState}, Message: {status.Message}");
        };

        await client.Connect();
        await WaitForStateAsync(client, XrpConnectionState.Connected, "Should be Connected after first connect");

        stateChanges.Clear();

        await client.Connect();
        await WaitForStateAsync(client, XrpConnectionState.Connected, "Should remain Connected after idempotent connect call");

        Assert.IsTrue(stateChanges.All(s => s == XrpConnectionState.Connected), "Only Connected state should be emitted for idempotent call");

        await client.Disconnect();
    }

    [TestMethod]
    public async Task TestChangeServer_SwitchesSuccessfully()
    {
        string alternate = LocalServerAlternateSpelling;
        if (alternate is null)
        {
            Assert.Inconclusive(
                $"There is no second spelling of {LocalServer} to switch to, so this would assert a switch that did not happen.");
        }

        List<XrpConnectionState> stateChanges = new List<XrpConnectionState>();

        XrplClient client = new XrplClient(LocalServer, LocalOptions());

        client.connection.OnConnectionStatus += (status) =>
        {
            stateChanges.Add(status.ConnectionState);
            Console.WriteLine($"State: {status.ConnectionState}, Message: {status.Message}");
        };

        await client.Connect();
        await WaitForStateAsync(client, XrpConnectionState.Connected, "Should be Connected after first connect");

        stateChanges.Clear();

        await client.ChangeServer(alternate, LocalOptions());
        await WaitForStateAsync(client, XrpConnectionState.Connected, "Should be Connected after ChangeServer");

        Assert.IsTrue(
            stateChanges.Contains(XrpConnectionState.Disconnected) || stateChanges.Contains(XrpConnectionState.Connecting),
            "Should have gone through Disconnected or Connecting state during server change");

        await client.Disconnect();
    }

    [TestMethod]
    public async Task TestChangeServer_NoWebSocketCleanupError()
    {
        XrplClient client = new XrplClient(LocalServer, LocalOptions());

        await client.Connect();
        await WaitForStateAsync(client, XrpConnectionState.Connected, "Should be Connected after connect");

        for (int i = 0; i < 3; i++)
        {
            Console.WriteLine($"ChangeServer iteration {i + 1}");

            await client.ChangeServer(LocalServer);
            await WaitForStateAsync(client, XrpConnectionState.Connected, $"Should be Connected after ChangeServer iteration {i + 1}");
        }

        await client.Disconnect();
    }

    [TestMethod]
    public async Task TestChangeServer_AfterMaxReconnectAttempts_NoNotConnectedException()
    {
        List<XrpConnectionState> stateChanges = new List<XrpConnectionState>();
        TaskCompletionSource<bool> disconnectedPermanently = new TaskCompletionSource<bool>();

        XrplClient client = new XrplClient(UnreachableServer, new XrplClient.ClientOptions
        {
            MaxReconnectAttempts = 2,
            StopAfterMaxAttempts = true,
            ConnectionAttemptTimeout = TimeSpan.FromSeconds(3),
            ReconnectBaseDelay = TimeSpan.FromMilliseconds(500),
            ReconnectMaxDelay = TimeSpan.FromSeconds(1)
        });

        client.connection.OnConnectionStatus += (status) =>
        {
            stateChanges.Add(status.ConnectionState);
            Console.WriteLine($"State: {status.ConnectionState}, Message: {status.Message}");

            if (status.ConnectionState == XrpConnectionState.Disconnected && status.Message.Contains("stopped"))
            {
                disconnectedPermanently.TrySetResult(true);
            }
        };

        try
        {
            using CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await client.Connect(cts.Token);
        }
        catch
        {
        }

        Task terminal = await Task.WhenAny(disconnectedPermanently.Task, Task.Delay(TimeSpan.FromSeconds(30)));
        Assert.AreSame(disconnectedPermanently.Task, terminal,
            "Permanent disconnect was not observed before the 30s timeout");
        await disconnectedPermanently.Task;

        Assert.AreEqual(XrpConnectionState.Disconnected, client.connection.CurrentConnectionState,
            "Should be Disconnected after max reconnect attempts");

        stateChanges.Clear();

        try
        {
            await client.ChangeServer(LocalServer, LocalOptions());
            await WaitForStateAsync(client, XrpConnectionState.Connected, "Should be Connected after ChangeServer to valid server");
        }
        catch (NotConnectedException ex)
        {
            Assert.Fail($"ChangeServer should not throw NotConnectedException after max reconnect attempts: {ex.Message}");
        }
        finally
        {
            await client.Disconnect();
        }
    }
}
