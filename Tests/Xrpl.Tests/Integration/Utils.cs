using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Models.Methods;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;
using Xrpl.Utils.Hashes;
using Xrpl.Wallet;
using Xrpl.Sugar;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/integration/utils.ts

namespace XrplTests.Xrpl.ClientLib.Integration
{
    /// <summary>
    /// Shared lock for standalone master account operations.
    /// Prevents sequence number conflicts when parallel test classes
    /// fund wallets from the same master account simultaneously.
    /// </summary>
    internal static class StandaloneLock
    {
        internal static readonly SemaphoreSlim MasterFunding = new(1, 1);
        internal static readonly SemaphoreSlim FaucetFunding = new(1, 1);
    }

    /// <summary>
    /// Specifies the type of XRPL node to connect to for integration tests.
    /// </summary>
    public enum TestNodeType
    {
        /// <summary>
        /// XRPL Testnet - public test network with faucet funding.
        /// </summary>
        TestNet,

        /// <summary>
        /// XRPL Devnet - development network with faucet funding.
        /// </summary>
        DevNet,

        /// <summary>
        /// Local standalone rippled node - uses master account for funding.
        /// Requires running: docker run -p 6006:6006 -it xrpllabsofficial/xrpld:1.12.0
        /// </summary>
        Standalone,

        /// <summary>
        /// XRPL Mainnet - production network, use with caution.
        /// </summary>
        MainNet
    }

    /// <summary>
    /// Configuration and utilities for integration tests supporting multiple node types.
    /// Automatically handles client creation and wallet funding based on the node type.
    /// </summary>
    public static class IntegrationTestConfig
    {
        /// <summary>
        /// Current node type for integration tests.
        /// Can be set via environment variable XRPL_TEST_NODE or directly.
        /// Default is TestNet.
        /// </summary>
        public static TestNodeType CurrentNodeType { get; set; } = GetNodeTypeFromEnvironment();

        /// <summary>
        /// Master account address for standalone node funding.
        /// </summary>
        public const string MasterAccount = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";

        /// <summary>
        /// Master account secret for standalone node funding.
        /// </summary>
        public const string MasterSecret = "snoPBrXtMeMyMHUVTgbuqAfg1SUTb";

        /// <summary>
        /// Minimum XRP balance threshold for funding check.
        /// </summary>
        public const decimal MinBalanceThreshold = 50m;

        /// <summary>
        /// Gets the WebSocket URL for the specified node type.
        /// </summary>
        /// <param name="nodeType">The type of node to connect to.</param>
        /// <returns>WebSocket URL string.</returns>
        public static string GetNodeUrl(TestNodeType nodeType)
        {
            return nodeType switch
            {
                TestNodeType.TestNet => "wss://s.altnet.rippletest.net:51233",
                TestNodeType.DevNet => "wss://s.devnet.rippletest.net:51233",
                TestNodeType.Standalone => "ws://localhost:6006",
                TestNodeType.MainNet => "wss://xrplcluster.com",
                _ => throw new ArgumentOutOfRangeException(nameof(nodeType), nodeType, null)
            };
        }

        /// <summary>
        /// Gets the node type from the XRPL_TEST_NODE environment variable.
        /// Defaults to TestNet if not set or invalid.
        /// </summary>
        private static TestNodeType GetNodeTypeFromEnvironment()
        {
            var envValue = Environment.GetEnvironmentVariable("XRPL_TEST_NODE");
            if (string.IsNullOrEmpty(envValue))
                return TestNodeType.Standalone;

            return envValue.ToLowerInvariant() switch
            {
                "testnet" or "test" => TestNodeType.TestNet,
                "devnet" or "dev" => TestNodeType.DevNet,
                "standalone" or "local" => TestNodeType.Standalone,
                "mainnet" or "main" => TestNodeType.MainNet,
                _ => TestNodeType.TestNet
            };
        }

        /// <summary>
        /// Creates and connects an XRPL client for the current node type.
        /// </summary>
        /// <param name="nodeType">Optional node type override. Uses CurrentNodeType if null.</param>
        /// <returns>Connected IXrplClient instance.</returns>
        public static async Task<IXrplClient> CreateClientAsync(TestNodeType? nodeType = null)
        {
            var type = nodeType ?? CurrentNodeType;
            var url = GetNodeUrl(type);
            var client = new XrplClient(url);

            client.connection.OnConnected += () =>
            {
                Console.WriteLine($"[IntegrationTest] Connected to {type} at {url}");
                return Task.CompletedTask;
            };

            client.connection.OnDisconnect += (code, description) =>
            {
                Console.WriteLine($"[IntegrationTest] Disconnected: {code}, {description}");
                return Task.CompletedTask;
            };

            client.connection.OnError += (error, errorMessage, message, data) =>
            {
                Console.WriteLine($"[IntegrationTest] Error: {message}");
                return Task.CompletedTask;
            };

            await client.Connect();
            return client;
        }

        /// <summary>
        /// Funds a wallet using the appropriate method for the current node type.
        /// For testnet/devnet: uses the faucet API.
        /// For standalone: uses the master account with ledger_accept.
        /// </summary>
        /// <param name="client">Connected XRPL client.</param>
        /// <param name="wallet">Wallet to fund.</param>
        /// <param name="nodeType">Optional node type override.</param>
        public static async Task FundWalletAsync(IXrplClient client, XrplWallet wallet, TestNodeType? nodeType = null)
        {
            var type = nodeType ?? client.Url() switch
            {
                { } url when url.Contains("altnet") => TestNodeType.TestNet,
                { } url when url.Contains("devnet") => TestNodeType.DevNet,
                { } url when url.Contains("localhost") => TestNodeType.Standalone,
                _ => TestNodeType.MainNet,
            };

            if (type == TestNodeType.Standalone)
            {
                await FundFromMasterAsync(client, wallet);
            }
            else if (type == TestNodeType.TestNet || type == TestNodeType.DevNet)
            {
                await FundFromFaucetAsync(client, wallet);
            }
            else
            {
                throw new InvalidOperationException($"Cannot fund wallet on {type}");
            }
        }

        /// <summary>
        /// Attempts to fund a wallet only if the balance is below threshold.
        /// </summary>
        /// <param name="client">Connected XRPL client.</param>
        /// <param name="wallet">Wallet to check and potentially fund.</param>
        /// <param name="nodeType">Optional node type override.</param>
        public static async Task TryFundWalletAsync(IXrplClient client, XrplWallet wallet, TestNodeType? nodeType = null)
        {
            try
            {
                var balance = await client.GetXrpFreeBalance(wallet.ClassicAddress);
                Console.WriteLine($"[IntegrationTest] Balance {wallet.ClassicAddress}: {balance} XRP");

                if (balance <= MinBalanceThreshold)
                {
                    await FundWalletAsync(client, wallet, nodeType);
                    Console.WriteLine($"[IntegrationTest] Funded {wallet.ClassicAddress}");
                }
            }
            catch (Exception)
            {
                await FundWalletAsync(client, wallet, nodeType);
                Console.WriteLine($"[IntegrationTest] Funded new account {wallet.ClassicAddress}");
            }
        }

        /// <summary>
        /// Funds multiple wallets, checking balance before each.
        /// </summary>
        /// <param name="client">Connected XRPL client.</param>
        /// <param name="nodeType">Optional node type override.</param>
        /// <param name="wallets">Wallets to fund.</param>
        public static async Task TryFundWalletsAsync(IXrplClient client, TestNodeType? nodeType, params XrplWallet[] wallets)
        {
            foreach (var wallet in wallets)
            {
                await TryFundWalletAsync(client, wallet, nodeType);
            }
        }

        private static XrplWallet FaucetFiller = null;

        /// <summary>
        /// Funds a wallet from the testnet/devnet faucet.
        /// Serialized via StandaloneLock to prevent FaucetFiller sequence conflicts.
        /// </summary>
        private static async Task FundFromFaucetAsync(IXrplClient client, XrplWallet wallet)
        {
            await StandaloneLock.FaucetFunding.WaitAsync();
            try
            {
                if (FaucetFiller is null)
                {
                    FaucetFiller = XrplWallet.Generate();
                    Console.WriteLine($"[IntegrationTest] FaucetFiller generated {FaucetFiller.ClassicAddress}");
                    var result = await client.FundWallet(FaucetFiller);
                    Console.WriteLine($"[IntegrationTest] FaucetFiller funded {FaucetFiller.ClassicAddress}: {result.Balance} XRP");
                }

                if (await client.GetXrpFreeBalance(FaucetFiller.ClassicAddress) is { } balance and > 50)
                {
                    await FundFromFaucetFillerAsync(client, wallet, 10);
                }
                else
                {
                    var result = await client.FundWallet(FaucetFiller);
                    Console.WriteLine($"[IntegrationTest] FaucetFiller funded {FaucetFiller.ClassicAddress}: {result.Balance} XRP");
                    await FundFromFaucetFillerAsync(client, wallet, 10);
                }
            }
            finally
            {
                StandaloneLock.FaucetFunding.Release();
            }
        }

        /// <summary>
        /// Funds a wallet from the faucetFiller.
        /// </summary>
        private static async Task FundFromFaucetFillerAsync(IXrplClient client, XrplWallet wallet, decimal xrpSize)
        {
            Payment payment = new Payment
            {
                Account = FaucetFiller.ClassicAddress,
                Destination = wallet.ClassicAddress,
                Amount = new Currency { ValueAsXrp = xrpSize, CurrencyCode = "XRP" }
            };
            
            var values = JsonSerializer.Deserialize<Dictionary<string, object>>(payment.ToJson(), global::Xrpl.Client.Json.XrplJsonOptions.Default);
            var response = await client.SubmitAndWait(values, FaucetFiller, autofill:true);

            if (response.Meta.TransactionResult != "tesSUCCESS")
            {
                throw new Exception($"Filler funding failed: {response.Meta.TransactionResult}");
            }

            Console.WriteLine($"[IntegrationTest] Filler funded {wallet.ClassicAddress}");
        }

        /// <summary>
        /// Funds a wallet from the standalone master account.
        /// Uses Submit + LedgerAccept instead of SubmitAndWait to avoid
        /// LastLedgerSequence expiration when parallel tests advance the ledger.
        /// Serialized via StandaloneLock to prevent sequence number conflicts.
        /// </summary>
        private static async Task FundFromMasterAsync(IXrplClient client, XrplWallet wallet)
        {
            await StandaloneLock.MasterFunding.WaitAsync();
            try
            {
                Payment payment = new Payment
                {
                    Account = MasterAccount,
                    Destination = wallet.ClassicAddress,
                    Amount = new Currency { Value = "400000000", CurrencyCode = "XRP" }
                };
                var values = JsonSerializer.Deserialize<Dictionary<string, object>>(payment.ToJson(), global::Xrpl.Client.Json.XrplJsonOptions.Default);
                var master = XrplWallet.FromSeed(MasterSecret);
                Submit response = await client.Submit(values, master);

                if (response.EngineResult != "tesSUCCESS")
                {
                    throw new Exception($"Master funding failed: {response.EngineResult}");
                }

                await LedgerAcceptAsync(client);
                Console.WriteLine($"[IntegrationTest] Master funded {wallet.ClassicAddress}");
            }
            finally
            {
                StandaloneLock.MasterFunding.Release();
            }
        }

        /// <summary>
        /// Advances the ledger on standalone node.
        /// No-op on public networks.
        /// </summary>
        /// <param name="client">Connected XRPL client.</param>
        /// <param name="nodeType">Optional node type override.</param>
        public static async Task LedgerAcceptAsync(IXrplClient client, TestNodeType? nodeType = null)
        {
            var type = nodeType ?? CurrentNodeType;
            if (type != TestNodeType.Standalone)
                return;

            var request = new BaseRequest { Command = "ledger_accept" };
            await client.AnyRequest(request);
        }

        /// <summary>
        /// Returns true if running on a public network with faucet support.
        /// </summary>
        public static bool IsFaucetNetwork(TestNodeType? nodeType = null)
        {
            var type = nodeType ?? CurrentNodeType;
            return type == TestNodeType.TestNet || type == TestNodeType.DevNet;
        }

        /// <summary>
        /// Returns true if running on standalone node.
        /// </summary>
        public static bool IsStandalone(TestNodeType? nodeType = null)
        {
            var type = nodeType ?? CurrentNodeType;
            return type == TestNodeType.Standalone;
        }
    }

    /// <summary>
    /// Legacy utilities for standalone integration tests.
    /// Consider using IntegrationTestConfig for new tests.
    /// </summary>
    public class Utils
    {
        private static string masterAccount = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";
        private static string masterSecret = "snoPBrXtMeMyMHUVTgbuqAfg1SUTb";

        public static async Task LedgerAccept(IXrplClient client)
        {
            var request = new BaseRequest { Command = "ledger_accept" };
            await client.AnyRequest(request);
        }

        public static async Task FundAccount(IXrplClient client, XrplWallet wallet)
        {
            await StandaloneLock.MasterFunding.WaitAsync();
            try
            {
                Payment payment = new Payment
                {
                    Account = masterAccount,
                    Destination = wallet.ClassicAddress,
                    Amount = new Currency { Value = "400000000", CurrencyCode = "XRP" }
                };
                var values = JsonSerializer.Deserialize<Dictionary<string, object>>(payment.ToJson(), global::Xrpl.Client.Json.XrplJsonOptions.Default);
                var master = XrplWallet.FromSeed(masterSecret);
                Submit response = await client.Submit(values, master);
                if (response.EngineResult != "tesSUCCESS")
                {
                    throw new XrplException($"Response not successful, {response.EngineResult}");
                }
                await LedgerAccept(client);
            }
            finally
            {
                StandaloneLock.MasterFunding.Release();
            }
        }

        public static async Task<XrplWallet> GenerateFundedWallet(IXrplClient client)
        {
            XrplWallet wallet = XrplWallet.Generate();
            await FundAccount(client, wallet);
            return wallet;
        }

        public static async Task VerifySubmittedTransaction(IXrplClient client, object tx, string? hashTx = null)
        {
            string hash;
            if (hashTx != null)
                hash = hashTx;
            else if (tx is string txStr)
                hash = HashLedger.HashSignedTx(txStr);
            else if (tx is JsonNode txNode)
                hash = HashLedger.HashSignedTx(txNode);
            else
                hash = HashLedger.HashSignedTx(JsonNode.Parse(
                    JsonSerializer.Serialize(tx, global::Xrpl.Client.Json.XrplJsonOptions.Default)));
            TxRequest request = new TxRequest(hash);
            TransactionResponse data = await client.Tx(request);
        }

        public static async Task TestTransaction(IXrplClient client, Dictionary<string, object> transaction, XrplWallet wallet)
        {
            await LedgerAccept(client);
            Submit response = await client.Submit(transaction, wallet);
            Assert.AreEqual("tesSUCCESS", response.EngineResult);
            if (response.TxJson is JsonObject txJsonObj)
                txJsonObj.Remove("hash");
            await LedgerAccept(client);
            await VerifySubmittedTransaction(client, response.TxJson);
        }

        public static async Task TestTransaction(IXrplClient client, ITransactionRequest transaction, XrplWallet wallet)
        {
            await TestTransaction(client, transaction.ToDictionary(), wallet);
        }
    }
}
