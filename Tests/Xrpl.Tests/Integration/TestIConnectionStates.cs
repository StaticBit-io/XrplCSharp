using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Client;

namespace Xrpl.Tests.Integration;

[TestClass]
[TestCategory("Integration")]
[TestCategory("TestI")]
public class TestIConnectionStates
{
    [TestMethod]
    public async Task TestConnectionStateSequence_ConnectDisconnect()
    {
        var stateChanges = new List<XrpConnectionState>();
        var stateMessages = new List<string>();

        var client = new XrplClient("wss://s.altnet.rippletest.net:51233", new XrplClient.ClientOptions
        {
            MaxReconnectAttempts = 3,
            StopAfterMaxAttempts = true,
            ConnectionAttemptTimeout = TimeSpan.FromSeconds(15),
            ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(30)
        });

        client.connection.OnConnectionStatus += (status) =>
        {
            stateChanges.Add(status.ConnectionState);
            stateMessages.Add($"[{status.ConnectionState}] {status.Message}");
            Console.WriteLine($"State: {status.ConnectionState}, Message: {status.Message}");
        };

        Assert.AreEqual(XrpConnectionState.Disconnected, client.connection.CurrentConnectionState, "Initial state should be Disconnected");

        await client.Connect();
        await Task.Delay(3000);

        Assert.IsTrue(stateChanges.Contains(XrpConnectionState.Connecting), "Should have Connecting state during connection");
        Assert.IsTrue(stateChanges.Contains(XrpConnectionState.Connected), "Should have Connected state after connection");
        Assert.AreEqual(XrpConnectionState.Connected, client.connection.CurrentConnectionState, "Current state should be Connected");

        stateChanges.Clear();

        await client.Disconnect();
        await Task.Delay(3000);
        Assert.IsTrue(stateChanges.Contains(XrpConnectionState.Disconnected), "Should have Disconnected state after user disconnect");
        Assert.AreEqual(XrpConnectionState.Disconnected, client.connection.CurrentConnectionState, "Current state should be Disconnected");

        Console.WriteLine("\n=== Test completed successfully ===");
        Console.WriteLine("State sequence: " + string.Join(" -> ", stateChanges));
    }

    [TestMethod]
    public async Task TestConnectionStateReconnect_InvalidServer()
    {
        var stateChanges = new List<XrpConnectionState>();
        var reconnectAttempts = 0;
        var tcs = new TaskCompletionSource<bool>();

        var client = new XrplClient("wss://invalid-server-that-does-not-exist.example.com:51233", new XrplClient.ClientOptions
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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await client.Connect(cts.Token); 
        }
        catch
        {
        }

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(30)));

        Assert.IsTrue(stateChanges.Contains(XrpConnectionState.Connecting), "Should have Connecting state");
        
        Console.WriteLine($"\n=== Reconnect attempts: {reconnectAttempts} ===");
        Console.WriteLine("State changes: " + string.Join(" -> ", stateChanges));

        await client.Disconnect();
    }

    [TestMethod]
    public async Task TestCurrentConnectionStateProperty()
    {
        var client = new XrplClient("wss://s.altnet.rippletest.net:51233", new XrplClient.ClientOptions
        {
            MaxReconnectAttempts = 3,
            StopAfterMaxAttempts = true,
            ConnectionAttemptTimeout = TimeSpan.FromSeconds(15),
            ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(30)
        });

        Assert.AreEqual(XrpConnectionState.Disconnected, client.connection.CurrentConnectionState, "Initial CurrentConnectionState should be Disconnected");

        await client.Connect();
        await Task.Delay(3000);

        Assert.AreEqual(XrpConnectionState.Connected, client.connection.CurrentConnectionState, "After connect, CurrentConnectionState should be Connected");

        await client.Disconnect();
        await Task.Delay(3000);

        Assert.AreEqual(XrpConnectionState.Disconnected, client.connection.CurrentConnectionState, "After disconnect, CurrentConnectionState should be Disconnected");

        Console.WriteLine("=== CurrentConnectionState property test passed ===");
    }

    [TestMethod]
    public async Task TestIdempotentConnect_StaysConnected()
    {
        var stateChanges = new List<XrpConnectionState>();

        var client = new XrplClient("wss://s.altnet.rippletest.net:51233", new XrplClient.ClientOptions
        {
            MaxReconnectAttempts = 3,
            StopAfterMaxAttempts = true,
            ConnectionAttemptTimeout = TimeSpan.FromSeconds(15),
            ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(30)
        });

        client.connection.OnConnectionStatus += (status) =>
        {
            stateChanges.Add(status.ConnectionState);
            Console.WriteLine($"State: {status.ConnectionState}, Message: {status.Message}");
        };

        await client.Connect(); 
        await Task.Delay(3000);

        Assert.AreEqual(XrpConnectionState.Connected, client.connection.CurrentConnectionState, "Should be Connected after first connect");

        stateChanges.Clear();

        await client.Connect();
        await Task.Delay(3000);

        Assert.AreEqual(XrpConnectionState.Connected, client.connection.CurrentConnectionState, "Should remain Connected after idempotent connect call");
        Assert.IsTrue(stateChanges.All(s => s == XrpConnectionState.Connected), "Only Connected state should be emitted for idempotent call");

        await client.Disconnect();

        Console.WriteLine("=== Idempotent connect test passed ===");
    }
}