// End-to-end test: ripple_path_find → Payment using discovered paths

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration
{
    [TestClass]
    [DoNotParallelize]
    public class TestIPathPayment
    {
        public TestContext TestContext { get; set; }
        public static TestNodeType nodeType = TestNodeType.Standalone;

        const string CurrencyCode = "PPT";

        [TestMethod]
        [Timeout(120000)]
        public async Task TestRipplePathFindThenPayment()
        {
            IXrplClient client = await IntegrationTestConfig.CreateClientAsync(nodeType);

            try
            {
                XrplWallet walletIssuer = XrplWallet.Generate();
                XrplWallet walletSender = XrplWallet.Generate();
                XrplWallet walletReceiver = XrplWallet.Generate();

                Console.WriteLine($"[PathPayment] Issuer:   {walletIssuer.ClassicAddress}");
                Console.WriteLine($"[PathPayment] Sender:   {walletSender.ClassicAddress}");
                Console.WriteLine($"[PathPayment] Receiver: {walletReceiver.ClassicAddress}");

                await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType,
                    walletIssuer, walletSender, walletReceiver);

                // 1. Enable DefaultRipple on issuer (required for A→issuer→B rippling)
                await SubmitTx(client, new AccountSet
                {
                    Account = walletIssuer.ClassicAddress,
                    SetFlag = AccountSetAsfFlags.asfDefaultRipple
                }, walletIssuer, "Enable DefaultRipple on issuer");

                // 2. Trust lines: sender and receiver trust issuer for PPT
                await SubmitTx(client, new TrustSet
                {
                    Account = walletSender.ClassicAddress,
                    LimitAmount = new Currency
                    {
                        CurrencyCode = CurrencyCode,
                        Issuer = walletIssuer.ClassicAddress,
                        Value = "10000000"
                    }
                }, walletSender, "TrustLine sender");

                await SubmitTx(client, new TrustSet
                {
                    Account = walletReceiver.ClassicAddress,
                    LimitAmount = new Currency
                    {
                        CurrencyCode = CurrencyCode,
                        Issuer = walletIssuer.ClassicAddress,
                        Value = "10000000"
                    }
                }, walletReceiver, "TrustLine receiver");

                // 3. Issue PPT to sender
                await SubmitTx(client, new Payment
                {
                    Account = walletIssuer.ClassicAddress,
                    Destination = walletSender.ClassicAddress,
                    Amount = new Currency
                    {
                        CurrencyCode = CurrencyCode,
                        Issuer = walletIssuer.ClassicAddress,
                        Value = "500"
                    }
                }, walletIssuer, "Issue 500 PPT to sender");

                // 4. ripple_path_find: sender wants to deliver 10 PPT to receiver
                Currency destinationAmount = new Currency
                {
                    CurrencyCode = CurrencyCode,
                    Issuer = walletIssuer.ClassicAddress,
                    Value = "10"
                };

                RipplePathFindRequest pathRequest = new RipplePathFindRequest(
                    sourceAccount: walletSender.ClassicAddress,
                    destinationAccount: walletReceiver.ClassicAddress,
                    destinationAmount: destinationAmount
                );

                RipplePathFindResponse pathResponse = await client.RipplePathFind(pathRequest);

                Assert.IsNotNull(pathResponse, "ripple_path_find response should not be null");
                Assert.IsNotNull(pathResponse.Alternatives, "Alternatives should not be null");
                Assert.IsNotNull(pathResponse.DestinationCurrencies, "DestinationCurrencies should not be null");
                Assert.AreEqual(walletSender.ClassicAddress, pathResponse.SourceAccount);
                Assert.AreEqual(walletReceiver.ClassicAddress, pathResponse.DestinationAccount);

                Console.WriteLine($"[PathPayment] Alternatives: {pathResponse.Alternatives.Count}");
                Console.WriteLine($"[PathPayment] DestCurrencies: {string.Join(", ", pathResponse.DestinationCurrencies)}");

                // 4. Build payment from pathfinding results
                Payment payment = new Payment
                {
                    Account = walletSender.ClassicAddress,
                    Destination = walletReceiver.ClassicAddress,
                    Amount = destinationAmount
                };

                if (pathResponse.Alternatives.Count > 0)
                {
                    PathAlternative best = pathResponse.Alternatives
                        .OrderByDescending(a => a.PathsComputed?.Count ?? 0)
                        .First();

                    foreach (PathAlternative alt in pathResponse.Alternatives)
                    {
                        string srcVal = alt.SourceAmount.Value ?? alt.SourceAmount.ValueAsXrp?.ToString() ?? "?";
                        string srcCur = alt.SourceAmount.CurrencyCode ?? "XRP";
                        Console.WriteLine($"[PathPayment]   alt: {srcVal} {srcCur}, paths={alt.PathsComputed?.Count ?? 0}");
                    }

                    bool isXrpSource = string.IsNullOrEmpty(best.SourceAmount.CurrencyCode)
                        || best.SourceAmount.CurrencyCode == "XRP";

                    if (isXrpSource)
                    {
                        decimal drops = best.SourceAmount.ValueAsXrp ?? 0;
                        payment.SendMax = new Currency { ValueAsXrp = drops * 1.5m };
                    }
                    else
                    {
                        payment.SendMax = new Currency
                        {
                            CurrencyCode = best.SourceAmount.CurrencyCode,
                            Issuer = best.SourceAmount.Issuer,
                            ValueAsNumber = best.SourceAmount.ValueAsNumber * 1.5m
                        };
                    }

                    if (best.PathsComputed is { Count: > 0 })
                    {
                        payment.Paths = best.PathsComputed;
                        Console.WriteLine($"[PathPayment] Using {best.PathsComputed.Count} computed path(s)");
                    }
                    else
                    {
                        Console.WriteLine("[PathPayment] Alternative found but paths_computed empty (direct path)");
                    }
                }
                else
                {
                    Console.WriteLine("[PathPayment] No alternatives — sender holds destination currency, direct payment");
                }

                if (payment.SendMax != null)
                    Console.WriteLine($"[PathPayment] SendMax: {payment.SendMax.Value ?? payment.SendMax.ValueAsXrp?.ToString()} {payment.SendMax.CurrencyCode ?? "XRP"}");

                var autofilled = await client.Autofill(payment);
                TransactionSummary result = await client.SubmitAndWait(autofilled, walletSender, true);

                string txResult = result.Meta?.TransactionResult;
                Console.WriteLine($"[PathPayment] Payment result: {txResult}");

                Assert.IsTrue(
                    txResult == "tesSUCCESS" || txResult == "terQUEUED",
                    $"Payment failed: {txResult}");
            }
            finally
            {
                client.Dispose();
            }
        }

        [TestMethod]
        [Timeout(120000)]
        public async Task TestCrossCurrencyPathPayment()
        {
            IXrplClient client = await IntegrationTestConfig.CreateClientAsync(nodeType);

            try
            {
                XrplWallet walletIssuer = XrplWallet.Generate();
                XrplWallet walletMaker = XrplWallet.Generate();
                XrplWallet walletSender = XrplWallet.Generate();
                XrplWallet walletReceiver = XrplWallet.Generate();

                Console.WriteLine($"[CrossCurrency] Issuer:   {walletIssuer.ClassicAddress}");
                Console.WriteLine($"[CrossCurrency] Maker:    {walletMaker.ClassicAddress}");
                Console.WriteLine($"[CrossCurrency] Sender:   {walletSender.ClassicAddress}");
                Console.WriteLine($"[CrossCurrency] Receiver: {walletReceiver.ClassicAddress}");

                await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType,
                    walletIssuer, walletMaker, walletSender, walletReceiver);

                // 1. Enable DefaultRipple on issuer
                await SubmitTx(client, new AccountSet
                {
                    Account = walletIssuer.ClassicAddress,
                    SetFlag = AccountSetAsfFlags.asfDefaultRipple
                }, walletIssuer, "DefaultRipple on issuer");

                // 2. Trust lines: maker and receiver trust issuer for PPT
                //    Sender does NOT get a trust line — pays XRP only
                await SubmitTx(client, new TrustSet
                {
                    Account = walletMaker.ClassicAddress,
                    LimitAmount = new Currency
                    {
                        CurrencyCode = CurrencyCode,
                        Issuer = walletIssuer.ClassicAddress,
                        Value = "10000000"
                    }
                }, walletMaker, "TrustLine maker");

                await SubmitTx(client, new TrustSet
                {
                    Account = walletReceiver.ClassicAddress,
                    LimitAmount = new Currency
                    {
                        CurrencyCode = CurrencyCode,
                        Issuer = walletIssuer.ClassicAddress,
                        Value = "10000000"
                    }
                }, walletReceiver, "TrustLine receiver");

                // 3. Issue PPT to maker (DEX liquidity provider)
                await SubmitTx(client, new Payment
                {
                    Account = walletIssuer.ClassicAddress,
                    Destination = walletMaker.ClassicAddress,
                    Amount = new Currency
                    {
                        CurrencyCode = CurrencyCode,
                        Issuer = walletIssuer.ClassicAddress,
                        Value = "1000"
                    }
                }, walletIssuer, "Issue 1000 PPT to maker");

                // 4. Maker creates offer: sell 100 PPT for 10 XRP (rate: 10 PPT per 1 XRP)
                await SubmitTx(client, new OfferCreate
                {
                    Account = walletMaker.ClassicAddress,
                    TakerGets = new Currency
                    {
                        CurrencyCode = CurrencyCode,
                        Issuer = walletIssuer.ClassicAddress,
                        Value = "100"
                    },
                    TakerPays = new Currency { ValueAsXrp = 10 }
                }, walletMaker, "Offer: sell 100 PPT for 10 XRP");

                // Verify offer exists
                AccountOffersRequest offersReq = new AccountOffersRequest(walletMaker.ClassicAddress);
                AccountOffers offersResp = await client.AccountOffers(offersReq);
                int offerCount = offersResp?.Offers?.Count ?? 0;
                Console.WriteLine($"[CrossCurrency] Maker has {offerCount} offer(s)");
                Assert.IsTrue(offerCount > 0, "Maker offer must exist before pathfinding");

                // 5. ripple_path_find: sender wants to deliver 10 PPT to receiver using XRP
                Currency destinationAmount = new Currency
                {
                    CurrencyCode = CurrencyCode,
                    Issuer = walletIssuer.ClassicAddress,
                    Value = "10"
                };

                RipplePathFindRequest pathRequest = new RipplePathFindRequest(
                    sourceAccount: walletSender.ClassicAddress,
                    destinationAccount: walletReceiver.ClassicAddress,
                    destinationAmount: destinationAmount
                )
                {
                    SourceCurrencies = new List<SourceCurrency>
                    {
                        new SourceCurrency { Currency = "XRP" }
                    }
                };

                RipplePathFindResponse pathResponse = await client.RipplePathFind(pathRequest);

                Assert.IsNotNull(pathResponse, "ripple_path_find response should not be null");
                Assert.IsNotNull(pathResponse.Alternatives, "Alternatives should not be null");

                Console.WriteLine($"[CrossCurrency] Alternatives: {pathResponse.Alternatives.Count}");

                foreach (PathAlternative alt in pathResponse.Alternatives)
                {
                    var srcVal = alt.SourceAmount.ValueAsXrp ?? alt.SourceAmount.ValueAsNumber;
                    string srcCur = alt.SourceAmount.CurrencyCode;
                    int pathCount = alt.PathsComputed?.Count ?? 0;
                    Console.WriteLine($"[CrossCurrency]   alt: {srcVal} {srcCur}, paths={pathCount}");

                    if (alt.PathsComputed != null)
                    {
                        for (int i = 0; i < alt.PathsComputed.Count; i++)
                        {
                            List<Path> steps = alt.PathsComputed[i];
                            foreach (Path step in steps)
                            {
                                Console.WriteLine($"[CrossCurrency]     step: type={step.Type} account={step.Account} currency={step.CurrencyCode} issuer={step.Issuer}");
                            }
                        }
                    }
                }

                Assert.IsTrue(pathResponse.Alternatives.Count > 0,
                    "Expected at least 1 alternative for XRP→PPT cross-currency path");

                PathAlternative best = pathResponse.Alternatives
                    .OrderByDescending(a => a.PathsComputed?.Count ?? 0)
                    .First();

                Assert.IsTrue(best.PathsComputed is { Count: > 0 },
                    "Expected non-empty paths_computed for cross-currency path");

                // 6. Build Payment using discovered paths
                bool isXrpSource = string.IsNullOrEmpty(best.SourceAmount.CurrencyCode)
                    || best.SourceAmount.CurrencyCode == "XRP";

                Currency sendMax;
                if (isXrpSource)
                {
                    decimal drops = best.SourceAmount.ValueAsXrp ?? 0;
                    sendMax = new Currency { ValueAsXrp = drops * 1.5m };
                }
                else
                {
                    sendMax = new Currency
                    {
                        CurrencyCode = best.SourceAmount.CurrencyCode,
                        Issuer = best.SourceAmount.Issuer,
                        ValueAsNumber = best.SourceAmount.ValueAsNumber * 1.5m
                    };
                }

                Payment payment = new Payment
                {
                    Account = walletSender.ClassicAddress,
                    Destination = walletReceiver.ClassicAddress,
                    Amount = destinationAmount,
                    SendMax = sendMax,
                    Paths = best.PathsComputed
                };

                Console.WriteLine($"[CrossCurrency] SendMax: {sendMax.Value ?? sendMax.ValueAsXrp?.ToString()} {sendMax.CurrencyCode ?? "XRP"}");
                Console.WriteLine($"[CrossCurrency] Using {best.PathsComputed.Count} computed path(s)");

                var autofilled = await client.Autofill(payment);
                TransactionSummary result = await client.SubmitAndWait(autofilled, walletSender, true);

                string txResult = result.Meta?.TransactionResult;
                Console.WriteLine($"[CrossCurrency] Payment result: {txResult}");

                Assert.IsTrue(
                    txResult == "tesSUCCESS" || txResult == "terQUEUED",
                    $"Cross-currency payment failed: {txResult}");
            }
            finally
            {
                client.Dispose();
            }
        }

        private static async Task SubmitTx(IXrplClient client, ITransactionRequest tx, XrplWallet wallet, string label)
        {
            try
            {
                var autofilled = await client.Autofill(tx);
                var res = await client.SubmitAndWait(autofilled, wallet, true);
                string result = res.Meta?.TransactionResult;
                Console.WriteLine($"[PathPayment] {label}: {result}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PathPayment] {label} exception: {ex.Message}");
            }
        }
    }
}
