using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

using Xrpl.AddressCodec;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/Wallet/fundWallet.ts

namespace Xrpl.Wallet
{
    public static class EasyTimer
    {
        public static IDisposable SetInterval(Action method, int delayInMilliseconds)
        {
            System.Timers.Timer timer = new System.Timers.Timer(delayInMilliseconds);
            timer.Elapsed += (source, e) =>
            {
                method();
            };

            timer.Enabled = true;
            timer.Start();

            // Returns a stop handle which can be used for stopping
            // the timer, if required
            return timer as IDisposable;
        }

        public static IDisposable SetTimeout(Action method, int delayInMilliseconds)
        {
            System.Timers.Timer timer = new System.Timers.Timer(delayInMilliseconds);
            timer.Elapsed += (source, e) =>
            {
                method();
            };

            timer.AutoReset = false;
            timer.Enabled = true;
            timer.Start();

            // Returns a stop handle which can be used for stopping
            // the timer, if required
            return timer as IDisposable;
        }
    }

    public static class WalletSugar
    {
        //Interval to check an account balance
        const int INTERVAL_SECONDS = 1;
        //Maximum attempts to retrieve a balance
        const int MAX_ATTEMPTS = 20;

        public class Funded
        {
            public XrplWallet Wallet;
            public double Balance;

            public Funded(XrplWallet wallet, double balance)
            {
                Wallet = wallet;
                Balance = balance;
            }
        }

        public class FaucetAccount
        {
            [JsonPropertyName("xAddress")]
            public string XAddress { get; set; }

            [JsonPropertyName("classicAddress")]
            public string ClassicAddress { get; set; }

            [JsonPropertyName("secret")]
            public string Secret { get; set; }

        }

        public class FaucetWallet
        {
            [JsonPropertyName("account")]
            public FaucetAccount Account { get; set; }

            [JsonPropertyName("amount")]
            public double Amount { get; set; }

            [JsonPropertyName("balance")]
            public double Balance { get; set; }

        }

        public static class FaucetNetwork
        {
            public static readonly string Testnet = "faucet.altnet.rippletest.net";
            public static readonly string Devnet = "faucet.devnet.rippletest.net";
            public static readonly string NFTDevnet = "faucet-nft.ripple.com";
        }

        public static Task<Funded> FundWallet(this IXrplClient client, XrplWallet? wallet = null, string? faucetHost = null)
            => FundWallet(client, wallet, faucetHost, CancellationToken.None);

        /// <summary>
        /// Funds a wallet from the network's faucet, giving up when <paramref name="cancellationToken"/>
        /// is cancelled. The wait for the faucet payment to be validated is tens of seconds, which is
        /// the reason this overload exists: a cancelled call reports cancellation rather than a
        /// faucet failure.
        /// </summary>
        public static async Task<Funded> FundWallet(this IXrplClient client, XrplWallet? wallet, string? faucetHost, CancellationToken cancellationToken)
        {
            //if (!client.IsConnected())
            //{
            //    throw new RippledError("Client not connected, cannot call faucet");
            //}
            // Generate a new Wallet if no existing Wallet is provided or its address is invalid to fund
            XrplWallet walletToFund = (wallet != null && XrplCodec.IsValidClassicAddress(wallet.ClassicAddress)) ? wallet : XrplWallet.Generate();

            double startingBalance = 0;
            try
            {
                startingBalance = Convert.ToDouble(await client.GetXrpBalance(walletToFund.ClassicAddress, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception err) when (err is not OperationCanceledException)
            {
                /* startingBalance remains '0': the account is usually not on the ledger yet */
            }

            // Create the POST request body

            Dictionary<string, object> json = new Dictionary<string, object>
            {
                { "destination", walletToFund.ClassicAddress },
            };
            string jsonData = JsonSerializer.Serialize(json, XrplJsonOptions.Default);
            byte[] postBody = Encoding.UTF8.GetBytes(jsonData);
            Dictionary<string, object> httpOptions = GetHTTPOptions(client, postBody, faucetHost);
            return await ReturnPromise(httpOptions, client, startingBalance, walletToFund, jsonData, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<Funded> ReturnPromise(
              Dictionary<string, object> options,
              IXrplClient client,
              double startingBalance,
              XrplWallet walletToFund,
              string postBody,
              CancellationToken cancellationToken
        )
        {
            string hostname = (string)options["hostname"];
            HttpClient httpsClient = new HttpClient();
            httpsClient.BaseAddress = new Uri($"https://{hostname}");
            httpsClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            StringContent contentData = new StringContent(postBody, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await httpsClient.PostAsync((string)options["path"], contentData, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException err)
            {
                throw new XRPLFaucetException($"The faucet at {hostname} could not be reached: {err.Message}", err);
            }

            byte[] chunks = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new XRPLFaucetException(
                    $"The faucet at {hostname} answered {(int)response.StatusCode} {response.StatusCode}: {Encoding.UTF8.GetString(chunks)}");
            }

            return await OnEnd(
                response,
                chunks,
                client,
                startingBalance,
                walletToFund,
                cancellationToken
            ).ConfigureAwait(false);
        }

        private static Dictionary<string, object> GetHTTPOptions(
              IXrplClient client,
              byte[] postBody,
              string hostname
        )
        {
            Dictionary<string, object> options = new Dictionary<string, object>
            {
                { "hostname", hostname ?? GetFaucetHost(client) },
                { "port", 443 },
                { "path", "/accounts" },
                { "method", "POST" },
                { "headers", new Dictionary<string, object> {
                    { "Content-Type", "application/json" },
                    { "Content-Length", postBody.Length }
                } }
            };
            return options;
        }
        private static async Task<Funded> OnEnd(
            HttpResponseMessage response,
            byte[] chunks,
            IXrplClient client,
            double startingBalance,
            XrplWallet walletToFund,
            CancellationToken cancellationToken
        )
        {
            string body = Encoding.UTF8.GetString(chunks);

            // TryGetValues, because a response without a Content-Type is still a response - a
            // proxy between here and the faucet can send one - and GetValues throws on a header
            // that is not there, which reports the wrong thing about the wrong party
            string contentType = response.Content.Headers.TryGetValues("Content-Type", out IEnumerable<string> values)
                ? values.FirstOrDefault()
                : null;

            // "application/json; charset=utf-8"
            if (contentType != null && contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
            {
                return await ProcessSuccessfulResponse(
                    client,
                    body,
                    startingBalance,
                    walletToFund,
                    cancellationToken
                ).ConfigureAwait(false);
            }

            throw new XRPLFaucetException(
                $"The faucet answered {(int)response.StatusCode} {response.StatusCode} with content type {contentType ?? "(none)"} rather than JSON: {body}");
        }

        /// <summary>
        /// The address the faucet says it funded. The body is a third party's HTTP response, so
        /// each way of being wrong is named here rather than surfacing further along as a
        /// <see cref="NullReferenceException"/> with nothing to say about the faucet.
        /// </summary>
        internal static string ReadFaucetAddress(string body)
        {
            FaucetWallet faucetWallet;
            try
            {
                faucetWallet = JsonSerializer.Deserialize<FaucetWallet>(body, XrplJsonOptions.Default);
            }
            catch (JsonException err)
            {
                throw new XRPLFaucetException($"The faucet response is not JSON this can read: {err.Message}", err);
            }

            string classicAddress = faucetWallet?.Account?.ClassicAddress;
            if (string.IsNullOrEmpty(classicAddress))
            {
                throw new XRPLFaucetException($"The faucet response carries no account address: {body}");
            }

            return classicAddress;
        }

        private static async Task<Funded> ProcessSuccessfulResponse(
              IXrplClient client,
              string body,
              double startingBalance,
              XrplWallet walletToFund,
              CancellationToken cancellationToken
        )
        {
            string fundedAddress = ReadFaucetAddress(body);

            double updatedBalance;
            try
            {
                // Check at regular interval if the address is enabled on the XRPL and funded
                updatedBalance = await GetUpdatedBalance(
                    client,
                    walletToFund.ClassicAddress,
                    startingBalance,
                    cancellationToken
                ).ConfigureAwait(false);
            }
            catch (Exception err) when (err is not OperationCanceledException and not XRPLFaucetException)
            {
                // The cause travels with it: reading a balance fails through the network, the node
                // or the JSON, and a caller handed only the sentence cannot tell which
                throw new XRPLFaucetException(
                    $"Could not read the balance of {walletToFund.ClassicAddress} while waiting for the faucet: {err.Message}", err);
            }

            if (updatedBalance <= startingBalance)
            {
                throw new XRPLFaucetException(
                    $"The faucet accepted the request for {fundedAddress}, but the balance of {walletToFund.ClassicAddress} did not rise above {startingBalance} within {INTERVAL_SECONDS} * {MAX_ATTEMPTS} seconds");
            }

            return new Funded(walletToFund, updatedBalance);
        }

        /// <summary>
        /// Polls until the funded account's balance rises above <paramref name="originalBalance"/>,
        /// and returns that balance; returns <paramref name="originalBalance"/> unchanged when the
        /// budget of <see cref="MAX_ATTEMPTS"/> polls runs out.
        /// </summary>
        /// <remarks>
        /// Every piece of state here belongs to the call. The previous implementation drove a
        /// <see cref="System.Timers.Timer"/> through static fields - the poll budget, the address,
        /// the balances and the result - which broke it two ways: the budget was never reset, so
        /// after roughly twenty polls every later call reported failure without polling at all,
        /// and two concurrent calls overwrote each other's address and result, so one wallet's
        /// balance could be reported for another.
        /// </remarks>
        internal static async Task<double> GetUpdatedBalance(
            IXrplClient client,
            string address,
            double originalBalance,
            CancellationToken cancellationToken = default
        )
        {
            for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
            {
                // The faucet payment needs a ledger to close, so wait before the first read
                await Task.Delay(TimeSpan.FromSeconds(INTERVAL_SECONDS), cancellationToken).ConfigureAwait(false);

                double newBalance;
                try
                {
                    newBalance = Convert.ToDouble(await client.GetXrpBalance(address, cancellationToken).ConfigureAwait(false));
                }
                catch (XrplException)
                {
                    // The account is not on the ledger yet: the faucet payment has not been validated
                    continue;
                }
                catch (RippleException)
                {
                    continue;
                }

                if (newBalance > originalBalance)
                {
                    return newBalance;
                }
            }

            return originalBalance;
        }

        public static string GetFaucetHost(IXrplClient client)
        {
            string connectionUrl = client.Url();
            // 'altnet' for Ripple Testnet server and 'testnet' for XRPL Labs Testnet server
            if (connectionUrl.Contains("altnet") || connectionUrl.Contains("testnet"))
            {
                return FaucetNetwork.Testnet;
            }

            if (connectionUrl.Contains("devnet"))
            {
                return FaucetNetwork.Devnet;
            }

            if (connectionUrl.Contains("xls20-sandbox"))
            {
                return FaucetNetwork.NFTDevnet;
            }

            throw new XRPLFaucetException("Faucet URL is not defined or inferrable.");
        }
    }
}