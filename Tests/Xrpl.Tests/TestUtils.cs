
// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/testUtils.ts

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Xrpl.Tests
{
    public class TestUtils
    {
        /// <summary>
        /// Ports this process has already handed out. The OS is free to return a just-released
        /// port to the next caller, and test classes run in parallel (see test.runsettings), so
        /// two callers could otherwise receive the same port and the second server would fail to
        /// bind — silently, because the mock listens on a background thread, leaving the test to
        /// time out instead of reporting a conflict.
        /// </summary>
        private static readonly ConcurrentDictionary<int, byte> ClaimedPorts = new();

        /// <summary>
        /// A loopback port free at the moment of the call and not handed out before.
        /// </summary>
        /// <remarks>
        /// The listener is stopped before returning, so the port is closed when the caller gets
        /// it — several tests need exactly that (connect to a server that is not up yet, start it
        /// later). The gap that leaves cannot be closed while callers need a closed port; what
        /// this does remove is the collision between concurrent callers inside this process,
        /// which is the reachable half of the race.
        /// </remarks>
        static public int GetFreePort()
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();

                if (ClaimedPorts.TryAdd(port, 0))
                {
                    return port;
                }
            }

            throw new InvalidOperationException(
                "GetFreePort: could not obtain an unclaimed loopback port after 50 attempts");
        }

        /// <summary>
        /// Whether a mock server on <paramref name="port"/> still completes a WebSocket handshake.
        /// </summary>
        /// <remarks>
        /// A dead accept loop leaves the listen socket bound, so a plain TCP connect still
        /// succeeds and proves nothing — only an answered handshake shows the mock is serving.
        /// Tests use this to say whether a connection failure was the client's doing or the
        /// mock's, instead of blaming the client for a server that went deaf.
        /// </remarks>
        static public bool MockCompletesHandshake(int port, TimeSpan timeout)
        {
            try
            {
                using TcpClient probe = new TcpClient();
                if (!probe.ConnectAsync(IPAddress.Loopback, port).Wait(timeout))
                {
                    return false;
                }

                NetworkStream stream = probe.GetStream();
                stream.WriteTimeout = (int)timeout.TotalMilliseconds;
                stream.ReadTimeout = (int)timeout.TotalMilliseconds;

                // The mock reads the key by offset from "Sec-WebSocket-Key: ", so the header must
                // carry a full 24-character value like a real client sends.
                byte[] request = Encoding.ASCII.GetBytes(
                    "GET / HTTP/1.1\r\n" +
                    "Host: 127.0.0.1\r\n" +
                    "Upgrade: websocket\r\n" +
                    "Connection: Upgrade\r\n" +
                    "Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n" +
                    "Sec-WebSocket-Version: 13\r\n\r\n");
                stream.Write(request, 0, request.Length);

                byte[] buffer = new byte[256];
                int read = stream.Read(buffer, 0, buffer.Length);
                return read > 0 && Encoding.ASCII.GetString(buffer, 0, read).Contains("101");
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Whether <paramref name="port"/> can still be bound on loopback right now. Tests that
        /// hold a port across an await use this to fail fast with a clear reason instead of
        /// waiting out a connection timeout when something else took it.
        /// </summary>
        static public bool IsPortStillFree(int port)
        {
            try
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }
    }
}
