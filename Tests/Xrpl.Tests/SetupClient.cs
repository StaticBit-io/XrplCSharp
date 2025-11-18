using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Timer = System.Timers.Timer;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/setupClient.ts

namespace Xrpl.Tests
{
    public class SetupUnitClient
    {
        public XrplClient client;
        public CreateMockRippled mockedRippled;
        public int _mockedServerPort;

        public async Task<SetupUnitClient> SetupClient()
        {
            int port = TestUtils.GetFreePort();
            mockedRippled = new CreateMockRippled(port);
            mockedRippled.AddResponse("server_info", new Dictionary<string, dynamic>
            {
                { "type", "response" },
                { "status", "success" },
                { "result", new Dictionary<string, dynamic>
                    {
                        { "info", new Dictionary<string, dynamic>
                            {
                                { "build_version", "test-mock" },
                                { "complete_ledgers", "1-1" },
                                { "server_state", "full" }
                            }
                        }
                    }
                }
            });
            var tcpListenerThread = new Thread(() =>
            {
                mockedRippled.Start();
                _mockedServerPort = port;
            });
            tcpListenerThread.Start();

            Timer timer = new Timer(25000);
            timer.Elapsed += (sender, e) => tcpListenerThread.Abort();
            client = new XrplClient($"ws://127.0.0.1:{port}");
            client.connection.OnConnected += () =>
            {
                Debug.WriteLine("SETUP CLIENT: CONECTED");
                return Task.CompletedTask;
            };
            client.connection.OnDisconnect += (code, description) =>
            {
                Console.WriteLine($"SSETUP CLIENT: DISCONECTED: {code}, description: {description}");
                return Task.CompletedTask;
            };
            client.connection.OnError += (e, em, m, d) =>
            {
                Debug.WriteLine($"SETUP CLIENT: ERROR: {e}");
                return Task.CompletedTask;
            };
            await client.Connect();
            return this;
        }
    }
}
