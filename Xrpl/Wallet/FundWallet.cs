using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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

        /// <summary>
        /// One client for the process. A new <see cref="HttpClient"/> per call holds its socket
        /// open past disposal and exhausts the pool under any load, and the faucet host varies,
        /// so the address goes on the request rather than on the client.
        /// </summary>
        private static readonly HttpClient FaucetClient = CreateFaucetClient();

        private static HttpClient CreateFaucetClient()
        {
            HttpClient httpsClient = new HttpClient();
            httpsClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return httpsClient;
        }

        /// <summary>
        /// Whether <paramref name="err"/> is the transport failing rather than the caller giving
        /// up. <see cref="HttpClient"/> reports its own <see cref="HttpClient.Timeout"/> as a
        /// <see cref="TaskCanceledException"/>, which is an <see cref="OperationCanceledException"/>
        /// and so is indistinguishable by type from a cancelled call - the token is what tells
        /// them apart, and only the caller's own cancellation is allowed through untouched.
        /// </summary>
        private static bool IsTransportFailure(Exception err, CancellationToken cancellationToken)
        {
            if (err is OperationCanceledException)
            {
                return !cancellationToken.IsCancellationRequested;
            }

            return err is HttpRequestException || err is IOException;
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
            Uri endpoint = new Uri($"https://{hostname}{(string)options["path"]}");

            using StringContent contentData = new StringContent(postBody, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await FaucetClient.PostAsync(endpoint, contentData, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception err) when (IsTransportFailure(err, cancellationToken))
            {
                throw new XRPLFaucetException($"The faucet at {hostname} could not be reached: {err.Message}", err);
            }

            using (response)
            {
                byte[] chunks;
                try
                {
                    chunks = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception err) when (IsTransportFailure(err, cancellationToken))
                {
                    throw new XRPLFaucetException(
                        $"The faucet at {hostname} answered {(int)response.StatusCode} {response.StatusCode} but the body could not be read: {err.Message}", err);
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new XRPLFaucetException(
                        $"The faucet at {hostname} answered {(int)response.StatusCode} {response.StatusCode}: {Redact(Encoding.UTF8.GetString(chunks))}");
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
                $"The faucet answered {(int)response.StatusCode} {response.StatusCode} with content type {contentType ?? "(none)"} rather than JSON: {Redact(body)}");
        }

        /// <summary>
        /// What may be repeated back from a faucet body. A successful response carries the funded
        /// wallet's seed in <c>account.secret</c>, and an exception message is the one thing a
        /// caller is certain to log, so the value of anything that names a secret is masked and
        /// the rest is capped. Quoting the body is still worth it: a rate limit says so in it.
        /// </summary>
        internal static string Redact(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return body;
            }

            string masked = SecretValue.Replace(body, "$1\"***\"");
            return masked.Length <= MaxQuotedBody ? masked : masked.Substring(0, MaxQuotedBody) + "...";
        }

        private const int MaxQuotedBody = 512;

        private static readonly Regex SecretValue = new Regex(
            "(\"(?:secret|seed|master_seed|master_seed_hex|private_key|passphrase|xAddress)\"\\s*:\\s*)\"[^\"]*\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
                throw new XRPLFaucetException($"The faucet response carries no account address: {Redact(body)}");
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

            PollOutcome poll;
            try
            {
                // Check at regular interval if the address is enabled on the XRPL and funded
                poll = await PollForFundedBalance(
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

            if (poll.Balance <= startingBalance)
            {
                string waited = $"within {INTERVAL_SECONDS} * {MAX_ATTEMPTS} seconds";
                // The poll swallows a failed read and tries again, because at first there is
                // nothing to read. If the balance never rose, the last such failure is the whole
                // account of why, and without it this blames the faucet for a client-side outage
                throw poll.LastReadFailure is null
                    ? new XRPLFaucetException(
                        $"The faucet accepted the request for {fundedAddress}, but the balance of {walletToFund.ClassicAddress} did not rise above {startingBalance} {waited}")
                    : new XRPLFaucetException(
                        $"The faucet accepted the request for {fundedAddress}, but the balance of {walletToFund.ClassicAddress} could not be read {waited}: {poll.LastReadFailure.Message}",
                        poll.LastReadFailure);
            }

            return new Funded(walletToFund, poll.Balance);
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
            return (await PollForFundedBalance(client, address, originalBalance, cancellationToken).ConfigureAwait(false)).Balance;
        }

        /// <summary>
        /// The balance the poll ended on, and the last reason it could not read one. Not being
        /// able to read is the normal case at first - the account is not on the ledger until the
        /// faucet payment validates - so the loop keeps going; but if the balance never rises,
        /// that last failure is the only account of why, and dropping it leaves the caller with
        /// a message blaming the faucet for what may have been a disconnected client.
        /// </summary>
        internal readonly struct PollOutcome
        {
            public PollOutcome(double balance, Exception lastReadFailure)
            {
                Balance = balance;
                LastReadFailure = lastReadFailure;
            }

            public double Balance { get; }

            public Exception LastReadFailure { get; }
        }

        internal static async Task<PollOutcome> PollForFundedBalance(
            IXrplClient client,
            string address,
            double originalBalance,
            CancellationToken cancellationToken = default,
            int attempts = MAX_ATTEMPTS,
            int intervalSeconds = INTERVAL_SECONDS
        )
        {
            Exception lastReadFailure = null;

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                // The faucet payment needs a ledger to close, so wait before the first read
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken).ConfigureAwait(false);

                double newBalance;
                try
                {
                    newBalance = Convert.ToDouble(await client.GetXrpBalance(address, cancellationToken).ConfigureAwait(false));
                }
                catch (XrplException err)
                {
                    // The account is not on the ledger yet: the faucet payment has not been validated
                    lastReadFailure = err;
                    continue;
                }
                catch (RippleException err)
                {
                    lastReadFailure = err;
                    continue;
                }

                if (newBalance > originalBalance)
                {
                    return new PollOutcome(newBalance, null);
                }

                // A reading that did not rise is not a failure to read
                lastReadFailure = null;
            }

            return new PollOutcome(originalBalance, lastReadFailure);
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