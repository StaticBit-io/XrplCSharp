using System.Net;
using System.Net.Sockets;

using Xrpl.Client;
using Xrpl.Client.Exceptions;

namespace Xrpl.Samples.ConnectionLifecycle;

/// <summary>
/// Every way an XrplClient connection can end, and what a consumer is supposed to do about each.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here submits a transaction or needs a funded account: the subject is the connection
/// itself. Run it against any node - a local standalone rippled is the easiest:
/// </para>
/// <code>
/// docker compose -f .ci-config/docker-compose.ci.yml up -d
/// dotnet run --project Tests/TestsClients/ConnectionLifecycleSample
/// dotnet run --project Tests/TestsClients/ConnectionLifecycleSample -- wss://s.altnet.rippletest.net:51233
/// </code>
/// <para>
/// The scenarios that need a server which is not answering use a port nothing is listening on, so
/// only the one node is required.
/// </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    /// The address the CI stand publishes on, and deliberately not <c>localhost</c>: that name
    /// resolves to <c>::1</c> first on a dual-stack host while the container publishes on IPv4
    /// only, so every connection pays a failed IPv6 attempt first. Harmless with the default
    /// attempt timeout and fatal with the short ones below, which is a trap worth not shipping in
    /// a sample.
    /// </summary>
    private static string _server = "ws://127.0.0.1:6006";

    private static async Task<int> Main(string[] args)
    {
        if (args.Length > 0)
        {
            _server = args[0];
        }

        Console.WriteLine($"Node under test: {_server}");
        Console.WriteLine("Each scenario prints the status stream it produced, then the answer a caller gets.");

        try
        {
            await NothingIsInProgress();
            await TheEndpointIsNotAnswering();
            await AConnectionThatWorks();
            await ARequestWithNoConnection();
            await ASwitchToAnEndpointThatIsDown();
            await AConsumerDisconnect();
            await ABrokenConnectHandler();
            await AnOperationThatWasOvertaken();
        }
        catch (Exception unexpected)
        {
            Console.WriteLine();
            Console.WriteLine($"The sample itself failed: {unexpected.GetType().Name}: {unexpected.Message}");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Done. Every ending above is a distinct type and a distinct ConnectionStopReason,");
        Console.WriteLine("which is what lets a consumer react without reading message text.");
        return 0;
    }

    /// <summary>
    /// A client nobody has called <c>Connect()</c> on. Not an error state - an actionable one.
    /// </summary>
    private static async Task NothingIsInProgress()
    {
        Scenario("A client that was never told to connect");

        XrplClient client = new XrplClient(UnusedEndpoint());

        ConnectionWaitOutcome outcome =
            await client.connection.WaitForConnectionOutcomeAsync(TimeSpan.FromSeconds(1));

        Console.WriteLine($"  outcome: {outcome}");
        Report(await Caught(() => client.connection.WaitForConnectionAsync(TimeSpan.FromSeconds(1))));
    }

    /// <summary>
    /// The endpoint is not answering and the client has stopped trying. This is the one ending
    /// that justifies failing over to another server.
    /// </summary>
    private static async Task TheEndpointIsNotAnswering()
    {
        Scenario("An endpoint that is down, with a reconnect budget that runs out");

        // Without StopAfterMaxAttempts the loop keeps trying for ever and there is no ending to
        // report - which is the right default for a long-lived client, and the wrong one for a
        // client that is supposed to move to another node.
        XrplClient client = new XrplClient(UnusedEndpoint(), new XrplClient.ClientOptions
        {
            MaxReconnectAttempts = 2,
            StopAfterMaxAttempts = true,
            ReconnectBaseDelay = TimeSpan.FromMilliseconds(200),
            ReconnectMaxDelay = TimeSpan.FromMilliseconds(400),
            ConnectionAttemptTimeout = TimeSpan.FromSeconds(2),
            UseCustomPing = false,
        });

        Trace(client);
        Report(await Caught(() => client.Connect()));

        // The same answer from a caller arriving afterwards, and from a request. All three ways of
        // asking read one record, so a consumer cannot get "this endpoint gave up" from one and
        // "you never connected" from another.
        Console.WriteLine($"  a later caller: {await client.connection.WaitForConnectionOutcomeAsync(TimeSpan.FromSeconds(1))}");
        Report(await Caught(() => ServerInfo(client)));

        await client.Disconnect();
    }

    private static async Task AConnectionThatWorks()
    {
        Scenario("A connection that comes up");

        XrplClient client = await Connected();

        Console.WriteLine($"  outcome: {await client.connection.WaitForConnectionOutcomeAsync(TimeSpan.FromSeconds(5))}");
        Console.WriteLine($"  IsConnected: {client.connection.IsConnected()}");

        await client.Disconnect();
    }

    /// <summary>
    /// What a request does while there is no connection is a policy, not an accident.
    /// </summary>
    private static async Task ARequestWithNoConnection()
    {
        Scenario("A request issued while the client is not connected");

        XrplClient refuses = new XrplClient(UnusedEndpoint(), new XrplClient.ClientOptions
        {
            RequestPolicy = RequestFailurePolicy.ImmediateFail,
            MaxReconnectAttempts = 1,
            ConnectionAttemptTimeout = TimeSpan.FromSeconds(2),
            UseCustomPing = false,
        });

        // Started and deliberately not awaited: the request has to be issued while an attempt is
        // in progress, which is the case the policy is about.
        Task connecting = Swallow(refuses.Connect());
        await Task.Delay(300);

        Console.WriteLine("  RequestFailurePolicy.ImmediateFail:");
        Report(await Caught(() => ServerInfo(refuses)), indent: "    ");

        await refuses.Disconnect();
        await connecting;

        Console.WriteLine("  RequestFailurePolicy.WaitForConnection: the request waits for the connection instead,");
        Console.WriteLine("  and fails with the same typed ending if the connection never arrives.");
    }

    /// <summary>
    /// A switch to a node that is down leaves the client stopped - and still recoverable.
    /// </summary>
    private static async Task ASwitchToAnEndpointThatIsDown()
    {
        Scenario("Switching to an endpoint that is down, then coming back");

        XrplClient client = await Connected(new XrplClient.ClientOptions
        {
            MaxReconnectAttempts = 2,
            StopAfterMaxAttempts = true,
            ReconnectBaseDelay = TimeSpan.FromMilliseconds(200),
            ReconnectMaxDelay = TimeSpan.FromMilliseconds(400),
            ConnectionAttemptTimeout = TimeSpan.FromSeconds(2),
            UseCustomPing = false,
        });

        Trace(client);
        Report(await Caught(() => client.connection.ChangeServer(UnusedEndpoint())));

        // The client stays stopped until the consumer decides otherwise - that is what a terminal
        // ending means - and deciding otherwise is one call.
        Console.WriteLine("  recovering by switching back to a node that is up:");
        await client.connection.ChangeServer(_server);
        Console.WriteLine($"    IsConnected: {client.connection.IsConnected()}");

        await client.Disconnect();
    }

    private static async Task AConsumerDisconnect()
    {
        Scenario("The consumer disconnects the client");

        XrplClient client = await Connected();
        Trace(client);

        await client.Disconnect();

        Console.WriteLine($"  outcome: {await client.connection.WaitForConnectionOutcomeAsync(TimeSpan.FromSeconds(2))}");
        Report(await Caught(() => ServerInfo(client)));

        // Nothing is going to bring this connection back on its own, which is exactly why the
        // ending has a type of its own: failing over here would be leaving a healthy node.
        Console.WriteLine("  Connect() is the way out, and it works:");
        await client.Connect();
        Console.WriteLine($"    IsConnected: {client.connection.IsConnected()}");
        await client.Disconnect();
    }

    /// <summary>
    /// The node is fine and this side is broken. Failing over would be leaving a healthy server.
    /// </summary>
    private static async Task ABrokenConnectHandler()
    {
        Scenario("An OnConnected handler that keeps throwing");

        XrplClient client = new XrplClient(_server, new XrplClient.ClientOptions
        {
            MaxReconnectAttempts = 2,
            StopAfterMaxAttempts = true,
            ReconnectBaseDelay = TimeSpan.FromMilliseconds(200),
            ReconnectMaxDelay = TimeSpan.FromMilliseconds(400),
            ConnectionAttemptTimeout = TimeSpan.FromSeconds(5),
            UseCustomPing = false,
        });

        client.connection.OnConnected += () =>
            throw new InvalidOperationException("restoring subscriptions failed");

        Trace(client);
        Report(await Caught(() => client.Connect()));

        await client.Disconnect();
    }

    /// <summary>
    /// An operation another one overtook reports the winner rather than a bare cancellation.
    /// </summary>
    /// <remarks>
    /// The overtaking operation here is a second switch. A <c>Disconnect()</c> overtaking a switch
    /// is the other half of the same rule and reports <c>ClientDisconnectedException</c>, because
    /// the reaction a consumer owes it is the opposite one.
    /// </remarks>
    private static async Task AnOperationThatWasOvertaken()
    {
        Scenario("A switch that another switch overtook");

        XrplClient client = await Connected(new XrplClient.ClientOptions
        {
            MaxReconnectAttempts = 20,
            ReconnectBaseDelay = TimeSpan.FromSeconds(2),
            ReconnectMaxDelay = TimeSpan.FromSeconds(2),
            ConnectionAttemptTimeout = TimeSpan.FromSeconds(5),
            ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(30),
            UseCustomPing = false,
        });

        Trace(client);

        // Issued from the first switch's own status notification, which lands it inside that
        // switch every time. A consumer meets this shape whenever a status handler reacts to the
        // connection - "this node is not answering, go to the other one" is exactly such a
        // handler.
        bool overtaking = false;
        client.connection.OnConnectionStatus += status =>
        {
            if (status.ConnectionState == XrpConnectionState.RestoringConnection && !overtaking)
            {
                overtaking = true;
                _ = Swallow(client.connection.ChangeServer(_server));
            }
        };

        Report(await Caught(() => client.connection.ChangeServer(UnusedEndpoint())));

        Console.WriteLine($"  the winner is where the client ended up - IsConnected: {client.connection.IsConnected()}");

        // A Disconnect() that overtakes a switch reports ClientDisconnectedException instead: the
        // client is down because it was asked to be, which calls for the opposite reaction, so the
        // two cases do not share a type.
        await client.Disconnect();
    }

    /// <summary>
    /// The point of the whole exercise: one <c>catch</c> per ending, and a different reaction for
    /// each. None of it reads message text.
    /// </summary>
    private static void Report(Exception? error, string indent = "  ")
    {
        if (error is null)
        {
            Console.WriteLine($"{indent}completed without an exception");
            return;
        }

        switch (error)
        {
            case ReconnectExhaustedException exhausted:
                Console.WriteLine($"{indent}{nameof(ReconnectExhaustedException)} after {exhausted.Attempts} of {exhausted.MaxAttempts} attempts");
                Console.WriteLine($"{indent}  -> this endpoint is not answering. Fail over to another server.");
                break;

            case ConnectHandlerFailedException handler:
                Console.WriteLine($"{indent}{nameof(ConnectHandlerFailedException)} after {handler.Failures} failure(s)");
                Console.WriteLine($"{indent}  caused by: {handler.InnerException?.GetType().Name}: {handler.InnerException?.Message}");
                Console.WriteLine($"{indent}  -> the node is fine and this side is broken. Fix the handler; do not fail over.");
                break;

            case ConnectionClosedPermanentlyException:
                Console.WriteLine($"{indent}{nameof(ConnectionClosedPermanentlyException)}");
                Console.WriteLine($"{indent}  -> the node closed with a code that says retrying is pointless. Use another server.");
                break;

            case ClientDisconnectedException:
                Console.WriteLine($"{indent}{nameof(ClientDisconnectedException)}");
                Console.WriteLine($"{indent}  -> the consumer took the client down. Only Connect() brings it back.");
                break;

            case NotConnectingException:
                Console.WriteLine($"{indent}{nameof(NotConnectingException)}");
                Console.WriteLine($"{indent}  -> nothing is being attempted. Call Connect().");
                break;

            case RequestRefusedException:
                Console.WriteLine($"{indent}{nameof(RequestRefusedException)}");
                Console.WriteLine($"{indent}  -> the connection was being rebuilt and this caller asked not to wait. Retry once connected.");
                break;

            case ConnectionSupersededException superseded:
                Console.WriteLine($"{indent}{nameof(ConnectionSupersededException)}: overtaken by {superseded.Kind}");
                Console.WriteLine($"{indent}  -> a later operation owns the connection. Its result is the one that counts.");
                break;

            case NotConnectedException other:
                Console.WriteLine($"{indent}{other.GetType().Name}: {other.Message}");
                Console.WriteLine($"{indent}  -> the base type still catches every one of the above.");
                break;

            case System.TimeoutException:
                Console.WriteLine($"{indent}{nameof(System.TimeoutException)}");
                Console.WriteLine($"{indent}  -> it did not come up in the time allowed. Nothing has given up; waiting longer may still work.");
                break;

            default:
                Console.WriteLine($"{indent}{error.GetType().Name}: {error.Message}");
                break;
        }
    }

    /// <summary>Prints the status stream, which carries the same endings as a value.</summary>
    private static void Trace(XrplClient client)
    {
        client.connection.OnConnectionStatus += status =>
        {
            string stopped = status.StopReason == ConnectionStopReason.None
                ? string.Empty
                : $" [stopped: {status.StopReason}]";

            Console.WriteLine($"    status: {status.ConnectionState}{stopped} - {status.Message}");
        };
    }

    /// <summary>The cheapest real request there is - it needs no account and no funds.</summary>
    private static Task ServerInfo(XrplClient client) =>
        client.connection.Request(new Dictionary<string, object> { { "command", "server_info" } });

    private static async Task<XrplClient> Connected(XrplClient.ClientOptions? options = null)
    {
        XrplClient client = new XrplClient(
            _server,
            options ?? new XrplClient.ClientOptions { UseCustomPing = false });

        await client.Connect();
        return client;
    }

    private static async Task<Exception?> Caught(Func<Task> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (Exception error)
        {
            return error;
        }
    }

    private static async Task Swallow(Task operation)
    {
        try
        {
            await operation;
        }
        catch
        {
            // The scenario reports through its own caller; this one exists only to be started.
        }
    }

    /// <summary>A loopback port nothing is listening on, so "the server is down" needs no server.</summary>
    private static string UnusedEndpoint()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        return $"ws://127.0.0.1:{port}";
    }

    private static void Scenario(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"== {title}");
    }
}
