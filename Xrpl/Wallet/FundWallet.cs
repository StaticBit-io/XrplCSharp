using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Xrpl.AddressCodec;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/Wallet/fundWallet.ts

namespace Xrpl.Wallet
{
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

        public static async Task<Funded> FundWallet(this IXrplClient client, XrplWallet? wallet = null, string? faucetHost = null)
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
                startingBalance = Convert.ToDouble(await client.GetXrpBalance(walletToFund.ClassicAddress));
            }
            catch
            {
                /* startingBalance remains '0' */
            }

            // Create the POST request body

            Dictionary<string, object> json = new Dictionary<string, object>
            {
                { "destination", walletToFund.ClassicAddress },
            };
            string jsonData = JsonSerializer.Serialize(json, XrplJsonOptions.Default);
            byte[] postBody = Encoding.UTF8.GetBytes(jsonData);
            Dictionary<string, object> httpOptions = GetHTTPOptions(client, postBody, faucetHost);
            return await ReturnPromise(httpOptions, client, startingBalance, walletToFund, jsonData);
        }

        private static async Task<Funded> ReturnPromise(
              Dictionary<string, object> options,
              IXrplClient client,
              double startingBalance,
              XrplWallet walletToFund,
              string postBody
        )
        {
            HttpClient httpsClient = new HttpClient();
            httpsClient.BaseAddress = new Uri($"https://{(string)options["hostname"]}");
            httpsClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            StringContent contentData = new StringContent(postBody, Encoding.UTF8, "application/json");
            var response = await httpsClient.PostAsync((string)options["path"], contentData);
            var row = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine(response.StatusCode);
                Console.WriteLine(row);
            }
            HttpContent content = response.Content;
            byte[] chunks = await content.ReadAsByteArrayAsync();
            return await OnEnd(
                response,
                chunks,
                client,
                startingBalance,
                walletToFund
            );
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
            XrplWallet walletToFund
        )
        {
            // Get Content Headers
            string body = Encoding.UTF8.GetString(chunks);
            // "application/json; charset=utf-8"
            if (response.Content.Headers.GetValues("Content-Type").First().StartsWith("application/json"))
            {
                return await ProcessSuccessfulResponse(
                    client,
                    body,
                    startingBalance,
                    walletToFund
                );
            }
            else
            {
                Dictionary<string, object> errorResponse = new Dictionary<string, object>
                {
                    { "statusCode", response.StatusCode },
                    { "contentType", response.Content.Headers.GetValues("Content-Type").First() },
                    { "body", body },
                };
                return await Task.FromException<Funded>(new XRPLFaucetException($"Content type is not application json {errorResponse.ToString()}"));
            }
        }

        private static async Task<Funded> ProcessSuccessfulResponse(
              IXrplClient client,
              string body,
              double startingBalance,
              XrplWallet walletToFund
        )
        {
            FaucetWallet faucetWallet = JsonSerializer.Deserialize<FaucetWallet>(body, XrplJsonOptions.Default);
            string classicAddress = faucetWallet.Account.ClassicAddress;
            if (classicAddress == null)
            {
                return await Task.FromException<Funded>(new XRPLFaucetException("The faucet account is undefined"));
            }
            try
            {
                // Check at regular interval if the address is enabled on the XRPL and funded
                double updatedBalance = await GetUpdatedBalance(
                    client,
                    walletToFund.ClassicAddress,
                    startingBalance
                );
                if (updatedBalance > startingBalance)
                {
                    return new Funded(walletToFund, updatedBalance);
                }
                else
                {
                    throw new XRPLFaucetException($"Unable to fund address with faucet after waiting {INTERVAL_SECONDS} * {MAX_ATTEMPTS} seconds");
                }
            }
            catch (Exception err)
            {
                if (err is Exception)
                {
                    return await Task.FromException<Funded>(new XRPLFaucetException(err.Message));
                }
                return await Task.FromException<Funded>(err);
            }
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
            double originalBalance
        )
        {
            for (int attempt = 0; attempt < MAX_ATTEMPTS; attempt++)
            {
                // The faucet payment needs a ledger to close, so wait before the first read
                await Task.Delay(TimeSpan.FromSeconds(INTERVAL_SECONDS)).ConfigureAwait(false);

                double newBalance;
                try
                {
                    newBalance = Convert.ToDouble(await client.GetXrpBalance(address).ConfigureAwait(false));
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