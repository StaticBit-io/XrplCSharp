using Microsoft.AspNetCore.Components;

using Newtonsoft.Json;

using System.Globalization;

using Xrpl.Models.Common;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Models.Utils;

using static Xrpl.Models.Common.Common;

using Offer = Xrpl.Models.Transactions.Offer;

namespace Blazor_WebAssembly.Pages
{
    public partial class Swap
    {
        string AmountInput { get; set; } = "";
        string CurrencyCode { get; set; } = "524C555344000000000000000000000000000000";
        string IssuerAddress { get; set; } = "rMxCKbEDwqr76QuheSUMdEGf4B9xJ8m5De";
        bool xrpOnTop { get; set; } = true;
        bool isCalculating { get; set; }
        string errorMessage { get; set; }
        string simulateError { get; set; }

        TradeSimulationResult tradeResult { get; set; }
        SimulateResponse simulateResponse { get; set; }

        List<OrderBookRow> askRows { get; set; }
        List<OrderBookRow> bidRows { get; set; }
        bool isLoadingOrderBook { get; set; }

        System.Threading.Timer _debounceTimer;
        System.Threading.Timer _orderBookDebounceTimer;

        class OrderBookRow
        {
            public decimal Price { get; set; }
            public decimal Amount { get; set; }
            public decimal Total { get; set; }
            public Offer SourceOffer { get; set; }
        }

        void ToggleDirection()
        {
            xrpOnTop = !xrpOnTop;
            tradeResult = null;
            simulateResponse = null;
            simulateError = null;
            errorMessage = null;
            TriggerDebounce();
        }

        void OnAmountChanged(ChangeEventArgs e)
        {
            AmountInput = e.Value?.ToString() ?? "";
            TriggerDebounce();
        }

        void OnCurrencyChanged(ChangeEventArgs e)
        {
            CurrencyCode = e.Value?.ToString() ?? "";
            TriggerDebounce();
        }

        void OnIssuerChanged(ChangeEventArgs e)
        {
            IssuerAddress = e.Value?.ToString() ?? "";
            TriggerDebounce();
        }

        void TriggerOrderBookDebounce()
        {
            _orderBookDebounceTimer?.Dispose();
            if (string.IsNullOrWhiteSpace(CurrencyCode) || string.IsNullOrWhiteSpace(IssuerAddress))
                return;

            _orderBookDebounceTimer = new System.Threading.Timer(
                async _ => await InvokeAsync(async () => await LoadOrderBook()),
                null,
                TimeSpan.FromSeconds(2),
                Timeout.InfiniteTimeSpan);
        }

        void TriggerDebounce()
        {
            _debounceTimer?.Dispose();
            if (string.IsNullOrWhiteSpace(CurrencyCode) || string.IsNullOrWhiteSpace(IssuerAddress))
                return;

            bool hasAmount = decimal.TryParse(AmountInput, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) && parsed > 0;

            if (!hasAmount)
            {
                TriggerOrderBookDebounce();
                return;
            }

            _debounceTimer = new System.Threading.Timer(
                async _ => await InvokeAsync(async () => await RunCalculation()),
                null,
                TimeSpan.FromSeconds(2),
                Timeout.InfiniteTimeSpan);
        }

        async Task LoadOrderBook()
        {
            if (!client.connection.IsConnected())
                return;

            if (string.IsNullOrWhiteSpace(CurrencyCode) || string.IsNullOrWhiteSpace(IssuerAddress))
                return;

            isLoadingOrderBook = true;
            StateHasChanged();

            try
            {
                var asksTask = client.BookOffers(new BookOffersRequest
                {
                    TakerGets = new TakerAmount { Currency = CurrencyCode, Issuer = IssuerAddress },
                    TakerPays = new TakerAmount { Currency = "XRP" },
                    Limit = 20
                });

                var bidsTask = client.BookOffers(new BookOffersRequest
                {
                    TakerGets = new TakerAmount { Currency = "XRP" },
                    TakerPays = new TakerAmount { Currency = CurrencyCode, Issuer = IssuerAddress },
                    Limit = 20
                });

                BookOffers asksResponse = null;
                BookOffers bidsResponse = null;

                try { asksResponse = await asksTask; } catch { }
                try { bidsResponse = await bidsTask; } catch { }

                ProcessOrderBook(asksResponse?.Offers, bidsResponse?.Offers);
            }
            catch { }
            finally
            {
                isLoadingOrderBook = false;
                await InvokeAsync(() => StateHasChanged());
            }
        }

        void ProcessOrderBook(List<Offer> askOffers, List<Offer> bidOffers)
        {
            askRows = new List<OrderBookRow>();
            bidRows = new List<OrderBookRow>();

            if (askOffers != null)
            {
                foreach (var offer in askOffers)
                {
                    var gets = offer.TakerGetsFunded ?? offer.TakerGets;
                    var pays = offer.TakerPaysFunded ?? offer.TakerPays;
                    var xrpAmount = pays.ValueAsXrp ?? pays.ValueAsNumber;
                    var tokenAmount = gets.ValueAsNumber;
                    if (xrpAmount <= 0 || tokenAmount <= 0) continue;
                    var price = xrpAmount / tokenAmount;
                    askRows.Add(new OrderBookRow { Price = price, Amount = tokenAmount, SourceOffer = offer });
                }
                askRows = askRows.OrderBy(r => r.Price).ToList();

                decimal cumTotal = 0;
                for (int i = 0; i < askRows.Count; i++)
                {
                    cumTotal += askRows[i].Price * askRows[i].Amount;
                    askRows[i].Total = cumTotal;
                }
            }

            if (bidOffers != null)
            {
                foreach (var offer in bidOffers)
                {
                    var gets = offer.TakerGetsFunded ?? offer.TakerGets;
                    var pays = offer.TakerPaysFunded ?? offer.TakerPays;
                    var xrpAmount = gets.ValueAsXrp ?? gets.ValueAsNumber;
                    var tokenAmount = pays.ValueAsNumber;
                    if (xrpAmount <= 0 || tokenAmount <= 0) continue;
                    var price = xrpAmount / tokenAmount;
                    bidRows.Add(new OrderBookRow { Price = price, Amount = tokenAmount, SourceOffer = offer });
                }
                bidRows = bidRows.OrderByDescending(r => r.Price).ToList();

                decimal cumTotal = 0;
                for (int i = 0; i < bidRows.Count; i++)
                {
                    cumTotal += bidRows[i].Price * bidRows[i].Amount;
                    bidRows[i].Total = cumTotal;
                }
            }
        }

        async Task RunCalculation()
        {
            if (!client.connection.IsConnected())
            {
                errorMessage = "Not connected to XRPL. Please connect first on the Connection Test page.";
                StateHasChanged();
                return;
            }

            if (!decimal.TryParse(AmountInput, NumberStyles.Any, CultureInfo.InvariantCulture, out var amountDecimal) || amountDecimal <= 0)
            {
                errorMessage = "Please enter a valid positive amount.";
                StateHasChanged();
                return;
            }

            isCalculating = true;
            isLoadingOrderBook = true;
            errorMessage = null;
            simulateError = null;
            tradeResult = null;
            simulateResponse = null;
            StateHasChanged();

            try
            {
                TakerAmount takerGets;
                TakerAmount takerPays;
                IssuedCurrency ammAsset;
                IssuedCurrency ammAsset2;

                if (xrpOnTop)
                {
                    takerGets = new TakerAmount { Currency = "XRP" };
                    takerPays = new TakerAmount { Currency = CurrencyCode, Issuer = IssuerAddress };
                    ammAsset = new IssuedCurrency { Currency = "XRP" };
                    ammAsset2 = new IssuedCurrency { Currency = CurrencyCode, Issuer = IssuerAddress };
                }
                else
                {
                    takerGets = new TakerAmount { Currency = CurrencyCode, Issuer = IssuerAddress };
                    takerPays = new TakerAmount { Currency = "XRP" };
                    ammAsset = new IssuedCurrency { Currency = CurrencyCode, Issuer = IssuerAddress };
                    ammAsset2 = new IssuedCurrency { Currency = "XRP" };
                }

                var bookOffersTask = client.BookOffers(new BookOffersRequest
                {
                    TakerGets = takerGets,
                    TakerPays = takerPays,
                    Limit = 50
                });

                var obAsksTask = client.BookOffers(new BookOffersRequest
                {
                    TakerGets = new TakerAmount { Currency = CurrencyCode, Issuer = IssuerAddress },
                    TakerPays = new TakerAmount { Currency = "XRP" },
                    Limit = 20
                });

                var obBidsTask = client.BookOffers(new BookOffersRequest
                {
                    TakerGets = new TakerAmount { Currency = "XRP" },
                    TakerPays = new TakerAmount { Currency = CurrencyCode, Issuer = IssuerAddress },
                    Limit = 20
                });

                Task<AMMInfoResponse> ammInfoTask = null;
                AMMInfo ammInfo = null;
                try
                {
                    ammInfoTask = client.AmmInfo(new AMMInfoRequest
                    {
                        Asset = ammAsset,
                        Asset2 = ammAsset2
                    });
                }
                catch { }

                BookOffers bookOffersResponse = null;
                try
                {
                    bookOffersResponse = await bookOffersTask;
                }
                catch (Exception ex)
                {
                    errorMessage = $"BookOffers error: {ex.Message}";
                }

                BookOffers obAsksResponse = null;
                BookOffers obBidsResponse = null;
                try { obAsksResponse = await obAsksTask; } catch { }
                try { obBidsResponse = await obBidsTask; } catch { }

                ProcessOrderBook(obAsksResponse?.Offers, obBidsResponse?.Offers);
                isLoadingOrderBook = false;

                if (ammInfoTask != null)
                {
                    try
                    {
                        var ammResponse = await ammInfoTask;
                        ammInfo = ammResponse?.Amm;
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message?.ToLower() ?? "";
                        if (!msg.Contains("actnotfound") && !msg.Contains("not found") && !msg.Contains("actNotFound"))
                        {
                            Console.WriteLine($"AMM info warning: {ex.Message}");
                        }
                    }
                }

                var offers = bookOffersResponse?.Offers ?? new List<Offer>();

                decimal tradeAmount;
                bool isTakerGetsXrp;
                if (xrpOnTop)
                {
                    tradeAmount = amountDecimal * 1000000m;
                    isTakerGetsXrp = true;
                }
                else
                {
                    tradeAmount = amountDecimal;
                    isTakerGetsXrp = false;
                }

                tradeResult = TradeSimulator.SimulateTrade(offers, ammInfo, tradeAmount, isTakerGetsXrp);

                await InvokeAsync(() => StateHasChanged());

                try
                {
                    var realAccount = offers.FirstOrDefault()?.Account
                        ?? obAsksResponse?.Offers?.FirstOrDefault()?.Account
                        ?? obBidsResponse?.Offers?.FirstOrDefault()?.Account;
                    var payment = BuildPaymentForSimulate(amountDecimal, realAccount);
                    var simResponse = await client.Simulate(new SimulateRequest { Transaction = payment });
                    simulateResponse = simResponse;
                }
                catch (Exception ex)
                {
                    simulateError = ex.Message;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Calculation error: {ex.Message}";
            }
            finally
            {
                isCalculating = false;
                isLoadingOrderBook = false;
                await InvokeAsync(() => StateHasChanged());
            }
        }

        Payment BuildPaymentForSimulate(decimal userAmount, string sourceAccount)
        {
            var account = sourceAccount ?? "rrrrrrrrrrrrrrrrrrrrrhoLvTp";
            Currency sendMax;
            Currency amount;

            if (xrpOnTop)
            {
                var dropsString = ((long)(userAmount * 1000000m)).ToString();
                sendMax = new Currency { CurrencyCode = "XRP", Value = dropsString };
                amount = new Currency { CurrencyCode = CurrencyCode, Issuer = IssuerAddress, Value = "999999999" };
            }
            else
            {
                sendMax = new Currency { CurrencyCode = CurrencyCode, Issuer = IssuerAddress, Value = userAmount.ToString(CultureInfo.InvariantCulture) };
                amount = new Currency { CurrencyCode = "XRP", Value = "999999000000" };
            }

            return new Payment
            {
                Account = account,
                Destination = account,
                Amount = amount,
                SendMax = sendMax,
                Fee = new Currency { Value = "0" },
                Flags = PaymentFlags.tfPartialPayment,
            };
        }

        string GetDeliveredAmount()
        {
            if (simulateResponse?.Meta == null)
                return "N/A";

            var delivered = simulateResponse.Meta.ActuallyDeliveredAmount ?? simulateResponse.Meta.PartialDeliveredAmount;
            if (delivered == null)
                return "N/A";

            return delivered.ToString();
        }

        string GetMetaJson()
        {
            if (simulateResponse?.Meta == null)
                return "{}";

            try
            {
                var metaObj = new
                {
                    TransactionResult = simulateResponse.Meta.TransactionResult,
                    DeliveredAmount = simulateResponse.Meta.ActuallyDeliveredAmount?.ToString(),
                    PartialDeliveredAmount = simulateResponse.Meta.PartialDeliveredAmount?.ToString(),
                    AffectedNodesCount = simulateResponse.Meta.AffectedNodes?.Count ?? 0,
                    TransactionIndex = simulateResponse.Meta.TransactionIndex
                };
                return JsonConvert.SerializeObject(metaObj, Formatting.Indented);
            }
            catch
            {
                return "{ \"error\": \"Could not serialize metadata\" }";
            }
        }

        string FormatDecimal(decimal value)
        {
            if (value == 0) return "0";
            if (Math.Abs(value) >= 1)
                return value.ToString("N6", CultureInfo.InvariantCulture);
            return value.ToString("G10", CultureInfo.InvariantCulture);
        }

        string FormatPrice(decimal value)
        {
            if (value == 0) return "0";
            if (Math.Abs(value) >= 1)
                return value.ToString("N8", CultureInfo.InvariantCulture);
            return value.ToString("G8", CultureInfo.InvariantCulture);
        }
    }
}
