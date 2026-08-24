using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using Xrpl.AddressCodec;
using Xrpl.Client.Exceptions;
using Xrpl.Tests.MockRippled;
using IPAddress = System.Net.IPAddress;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/createMockRippled.ts

namespace Xrpl.Tests
{
    public static class Logger
    {
        static readonly TextWriter tw;

        static Logger()
        {
            string _filePath = Path.GetDirectoryName(System.AppDomain.CurrentDomain.BaseDirectory);
            tw = TextWriter.Synchronized(File.AppendText(_filePath + "/Log.txt"));
        }

        public static void Write(string logMessage)
        {
            try
            {
                Log(logMessage, tw);
            }
            catch (IOException e)
            {
                tw.Close();
            }
        }

        private static readonly object _syncObject = new object();

        public static void Log(string logMessage, TextWriter w)
        {
            // only one thread can own this lock, so other threads
            // entering this method will wait here until lock is
            // available.
            lock (_syncObject)
            {
                w.WriteLine("{0} {1}", DateTime.Now.ToLongTimeString(),
                    DateTime.Now.ToLongDateString());
                w.WriteLine("  :");
                w.WriteLine("  :{0}", logMessage);
                w.WriteLine("-------------------------------");
                // Update the underlying file.
                w.Flush();
            }
        }
    }

    public class CreateMockRippled
    {
        public int _port;
        private TcpListener _listener;
        private Dictionary<string, object> _responses = new Dictionary<string, object>();
        public bool suppressOutput = false;
        private Thread tcpListenerThread;
        private readonly object _serverLock = new object();
        private Server _server;
        private bool _stopped;

        public CreateMockRippled(int port)
        {
            this._port = port;
        }

        /// <summary>
        /// Stops the listen socket. Without this the server keeps accepting for the lifetime of the test
        /// process, so every test that starts a mock leaks a listener.
        /// Start() runs on a background thread, so shutdown is recorded here: a startup that finishes
        /// afterwards stops its listener instead of leaving it behind.
        /// </summary>
        public void Stop()
        {
            Server server;
            lock (_serverLock)
            {
                _stopped = true;
                server = _server;
                _server = null;
            }

            StopServer(server);
        }

        private static void StopServer(Server server)
        {
            if (server == null)
            {
                return;
            }

            try
            {
                server.Stop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MockRippled stop error: {ex.Message}");
            }
        }

        string CreateResponse(Dictionary<string, object> request, Dictionary<string, object> response)
        {
            var cloneResp = new Dictionary<string, object>(response);
            if (!cloneResp.ContainsKey("type") && !cloneResp.ContainsKey("error"))
            {
                throw new AddressCodecException($"Bad response format. Must contain `type` or `error`. {response}");
            }
            cloneResp["id"] = request["id"];
            return JsonSerializer.Serialize(cloneResp);
        }

        public void AddResponse(string command, Dictionary<string, object> response)
        {
            if (!response.ContainsKey("type") && !response.ContainsKey("error"))
            {
                throw new AddressCodecException($"Bad response format. Must contain `type` or `error`. {response}");
            }
            _responses[command] = response;
        }

        /// <summary>
        /// How long to sit on a command's answer before sending it, per command.
        /// </summary>
        private Dictionary<string, TimeSpan> _responseDelays = new Dictionary<string, TimeSpan>();

        /// <summary>
        /// Registers an answer that is sent only after <paramref name="delay"/>.
        /// </summary>
        /// <remarks>
        /// For tests that need a request to still be in flight while something else happens to the
        /// connection. Answered at once, that window is reachable only by luck - which is how the
        /// race in issue #122 stayed a CI-only flake through 37 local runs.
        /// </remarks>
        public void AddDelayedResponse(string command, Dictionary<string, object> response, TimeSpan delay)
        {
            AddResponse(command, response);
            _responseDelays[command] = delay;
        }

        Dictionary<string, object> GetResponse(Dictionary<string, object> request)
        {
            string command = request["command"]?.ToString();
            if (command == null)
            {
                throw new AddressCodecException($"No handler for {command}");
            }
            Dictionary<string, object> functionOrObject = (Dictionary<string, object>)this._responses[command];
            //if (functionOrObject is Func)
            //{
            //    return functionOrObject(request) as Dictionary<string, object>;
            //}
            return functionOrObject;
        }

        void TestCommand(MockClient client, Dictionary<string, object> request)
        {
            Dictionary<string, object> data;
            object rawData = request["data"];
            if (rawData is JsonElement je)
                data = JsonSerializer.Deserialize<Dictionary<string, object>>(je.GetRawText());
            else
                data = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(rawData));

            data.TryGetValue("disconnectIn", out var disconnectIn);
            data.TryGetValue("openOnOtherPort", out var openOnOtherPort);
            data.TryGetValue("closeServerAndReopen", out var closeServerAndReopen);
            data.TryGetValue("unrecognizedResponse", out var unrecognizedResponse);
            data.TryGetValue("closeServer", out var closeServer);
            data.TryGetValue("delayedResponseIn", out var delayedResponseIn);

            if (disconnectIn != null)
            {
                Dictionary<string, object> response = new Dictionary<string, object>
                {
                    { "result", new Dictionary<string, object>() {} },
                    { "status", "Success" },
                    { "type", "response" },
                };
                string responseString = CreateResponse(request, response);
                this.Send(client, responseString);
            }
            if (openOnOtherPort != null)
            {
                Dictionary<string, object> response = new Dictionary<string, object>
                {
                    { "result", new Dictionary<string, object>() {
                        { "port", 9999 }
                    } },
                    { "status", "Success" },
                    { "type", "response" },
                };
                string responseString = CreateResponse(request, response);
                this.Send(client, responseString);
            }
            if (closeServerAndReopen != null)
            {
                Dictionary<string, object> response = new Dictionary<string, object>
                {
                    { "result", new Dictionary<string, object>() {} },
                    { "status", "Success" },
                    { "type", "response" },
                };
                string responseString = CreateResponse(request, response);
                this.Send(client, responseString);
            }
            if (unrecognizedResponse != null)
            {
                Dictionary<string, object> response = new Dictionary<string, object>
                {
                    { "result", new Dictionary<string, object>() {} },
                    { "status", "Success" },
                    { "type", "response" },
                };
                string responseString = CreateResponse(request, response);
                this.Send(client, responseString);
            }
            if (closeServer != null)
            {
                client.GetSocket().Close();
                //this._listener.Stop();
                //client.Close();
                //netstr.Dispose();
            }
            if (delayedResponseIn != null)
            {
                Dictionary<string, object> response = new Dictionary<string, object>
                {
                    { "result", new Dictionary<string, object>() {} },
                    { "status", "Success" },
                    { "type", "response" },
                };
                string responseString = CreateResponse(request, response);
                this.Send(client, responseString);
            }
        }

        void Send(MockClient client, string message)
        {
            try
            {
                client.GetServer().SendMessage(client, message);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        void Ping(MockClient client, Dictionary<string, object> request)
        {
            Dictionary<string, object> response = new Dictionary<string, object>
            {
                { "result", null },
                { "status", "Success" },
                { "type", "response" },
            };
            Send(client, CreateResponse(request, response));
        }
        public void Start()
        {

            Server server = new Server(new IPEndPoint(IPAddress.Parse("127.0.0.1"), this._port));

            lock (_serverLock)
            {
                if (_stopped)
                {
                    // Stop() already ran - do not leave this listener accepting behind the test's back.
                    StopServer(server);
                    return;
                }

                _server = server;
            }

            // Bind the event for when a client connected
            server.OnClientConnected += (object sender, OnClientConnectedHandler e) =>
            {
                string clientGuid = e.GetClient().GetGuid();
            };

            // Bind the event for when a message is received
            server.OnMessageReceived += (object sender, OnMessageReceivedHandler e) =>
            {
                string jsonStr = e.GetMessage();
                Dictionary<string, object> request = null;
                try
                {
                    request = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonStr);
                    var _command = request.TryGetValue("command", out var command);
                    if (!request.ContainsKey("id"))
                    {
                        throw new XrplException($"Request has no id: {JsonSerializer.Serialize(request)}");
                    }
                    if (!_command)
                    {
                        throw new XrplException($"Request has no command: {JsonSerializer.Serialize(request)}");
                    }
                    string commandStr = command?.ToString();
                    if (commandStr == "ping")
                    {
                        Ping(e.GetClient(), request);
                    }
                    else if (commandStr == "test_command")
                    {
                        this.TestCommand(e.GetClient(), request);
                    }
                    else if (this._responses.ContainsKey(commandStr))
                    {
                        string answer = this.CreateResponse(request, this.GetResponse(request));
                        MockClient answerTo = e.GetClient();
                        if (this._responseDelays.TryGetValue(commandStr, out TimeSpan delay))
                        {
                            // Answered late, on a task of its own: the read loop has to keep
                            // running, or nothing else on this connection would happen while the
                            // answer is held back.
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(delay);
                                try
                                {
                                    this.Send(answerTo, answer);
                                }
                                catch
                                {
                                    // The socket may be gone by now - that is usually the point of
                                    // the delay, and it is not this mock's business to complain.
                                }
                            });
                        }
                        else
                        {
                            this.Send(answerTo, answer);
                        }
                    }
                    else
                    {
                        throw new XrplException($"No event handler registered in mock rippled for {commandStr}");
                    }
                }
                catch (XrplException err)
                {
                    if (!this.suppressOutput)
                    {
                        Debug.WriteLine($"{err}");
                    }
                    if (request != null)
                    {
                        Dictionary<string, object> errorResponse = new Dictionary<string, object>
                        {
                            { "type", "response" },
                            { "status", "error" },
                            { "error", err.Message.ToString() },
                        };
                        this.Send(e.GetClient(), CreateResponse(request, errorResponse));
                        return;
                    }
                }
                catch (Exception error)
                {
                    throw;
                }
            };

            // Bind the event for when a client connected
            server.OnSendMessage += (object sender, OnSendMessageHandler e) =>
            {
                string clientGuid = e.GetClient().GetGuid();
            };

            // Bind the event for when a client disconnected
            server.OnClientDisconnected += (object sender, OnClientDisconnectedHandler e) =>
            {
                //e.GetClient().GetSocket().Close();
                //e.GetClient().GetSocket().Dispose();
                //e.GetClient().GetServer().ClientDisconnect(e.GetClient());
                string clientGuid = e.GetClient().GetGuid();
            };
        }
    }
}