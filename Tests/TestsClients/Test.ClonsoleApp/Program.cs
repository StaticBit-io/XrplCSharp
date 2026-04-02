////See https://aka.ms/new-console-template for more information

using Newtonsoft.Json;

using System.Diagnostics;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Subscriptions;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Utils;
using Xrpl.Wallet;

using Currency = Xrpl.Models.Common.Currency;

namespace MyApp;

internal class Program
{
    private static IXrplClient client;

    private enum TestDataType
    {
        testNet,
        devNet,
        mainNet,
        standalone,
    }
    static XrplWallet walletPrimary = XrplWallet.FromNormalizedText("primary test account");
    static XrplWallet walletSecondary_1 = XrplWallet.FromNormalizedText("secondary test account 1");
    static XrplWallet walletSecondary_2 = XrplWallet.FromNormalizedText("secondary test account 2");
    static XrplWallet walletMultiSign = XrplWallet.FromNormalizedText("multi sign test account");
    static XrplWallet walletMultiSigner_1 = XrplWallet.FromNormalizedText("multi sign test account 1");
    static XrplWallet walletMultiSigner_2 = XrplWallet.FromNormalizedText("multi sign test account 2");
    static XrplWallet walletRegularKey = XrplWallet.FromNormalizedText("regular key test account");
    static XrplWallet walletRegularKey_signer = XrplWallet.FromNormalizedText("regular key test account signer");

    private static async Task Main(string[] args)
    {
        //TestWalletFromText();
        await InitTestData(TestDataType.devNet);

        try
        {
            //await InitForDataForTest();

            //await SetSigners(walletMultiSign, walletMultiSigner_1, walletMultiSigner_2);

            var features = await client.ServerFeatures();
            var canBe = features.GetActivated();
            var mpts = features.GetByNameContains("mpt");
            foreach (var mpt in canBe)
            {
                Console.WriteLine(mpt.Value.Name);
            }

            //await Simulate();
            //await MultiSignTest();

            await client.Disconnect();
        }
        catch (Xrpl.Client.Exceptions.RippledException e)
        {
            var info = XrplErrorClassifier.Classify(e);
            throw e;
        }
        catch (Xrpl.Client.Exceptions.XrplException e)
        {
            throw e;
        }
        catch (Exception e)
        {
            throw e;
        }

        //await SampleClient();
        //WalletFromSeed();
        //WalletGenerate();
        //await SubmitTestTx();
        //await WebsocketTest();
        //await WebsocketChangeServerTest();
    }

    private static async Task InitForDataForTest()
    {
        await new TestAccountBuilder(client, TestNodeType.Standalone)
            .AddPrimaryAccount(walletPrimary)       // ваш кошелёк - владелец всех объектов
            .AddTrustlines("USD", "EUR", "BTC")
            .AddNFTs(3)
            .AddOffers(5)
            .AddIssuerOffers(5)
            .AddTickets(5)
            .AddChecks(2)
            .AddEscrows()
            .AddSignerList()
            .BuildAsync();
        // Теперь можно использовать готовые аккаунты для тестов:
        Console.WriteLine($"Issuer: {TestAccountBuilder.IssuerAccount.ClassicAddress}");
        // Пример: получить NFT созданные builder-ом
        var nfts = await client.AccountNFTs(new AccountNFTsRequest(
            walletPrimary.ClassicAddress));
    }

    private static async Task TestReconnection()
    {
        var options = new XrplClient.ClientOptions()
        {
            ApiVersion = 2,
            MaxReconnectAttempts = 3,
            RequestPolicy = RequestFailurePolicy.ImmediateFail,
            UseCustomPing = false,
            StopAfterMaxAttempts = true,
            UseCheckHealth = true,
        };
        var servers = new List<string>
        {
            "wss://s1.ripple.com/",
            "wss://s2.ripple.com/",
            "wss://xrplcluster.com/",
            "wss://s.altnet.rippletest.net:51233"
        };
        IXrplClient client = new XrplClient(servers[0], options);
        var watch = Stopwatch.StartNew();
        client.connection.OnConnectionStatus += async info =>
        {
            if (info.Reconnect != null)
            {
                Console.WriteLine($"{info.Severity.ToString()} {info.ConnectionState.ToString()} {info.Reconnect.CurrentAttempt}/{info.Reconnect.MaxAttempts} {info.Message}");
            }
            else
            {
                Console.WriteLine($"{info.Severity.ToString()} {info.ConnectionState.ToString()} {info.Message}");
            }

            if (info.ConnectionState == XrpConnectionState.Connected)
            {
                watch.Restart();
                //try
                //{
                //    var subscribe = await client.Subscribe(
                //        new SubscribeRequest()
                //        {
                //            Streams = new List<StreamType>(
                //                new[]
                //                {
                //                    StreamType.Ledger,
                //                    StreamType.Transactions,
                //                }),
                //        });
                //    Console.WriteLine(subscribe);
                //}
                //catch (Exception e)
                //{
                //    Console.WriteLine(e);
                //}
            }
        };
        client.connection.OnDisconnect += (code, description) =>
        {
            var time = watch.Elapsed;
            Console.Title = $"{time:g}";
            Debug.WriteLine($"Session time: {time:g}");
            Console.WriteLine($"D {code} {description}");
            return Task.CompletedTask;
        };
        client.connection.OnPing += ping =>
        {
            Console.WriteLine(ping);
            return Task.CompletedTask;
        };
        client.connection.OnLedgerClosed += r =>
        {
            Console.WriteLine($"LEDGER {r.LedgerIndex}");
            return Task.CompletedTask;
        };
        client.connection.OnTransaction += OnTransaction;

        await client.Connect();
        var task1 = Task.Run(
            async () =>
            {
                while (true)
                {
                    await Task.Delay(1000);
                    Console.WriteLine($"[{DateTime.UtcNow}]{client.connection.State()} - {watch.Elapsed.TotalSeconds}c");
                    //if (Math.Round(watch.Elapsed.TotalSeconds) % 140 == 0)
                    //{
                    //    Console.WriteLine(await client.Ping());
                    //    ;
                    //}
                }
            });

        await task1;

        var task = Task.Run(async () =>
        {
            while (true)
            {
                try
                {

                    var server = servers[new Random().Next(servers.Count)];
                    if (server == client.Url())
                    {
                        continue;
                    }

                    //await client.Subscribe(
                    //    new SubscribeRequest()
                    //    {
                    //        Streams = new List<StreamType>()
                    //        {
                    //            StreamType.Ledger
                    //        }
                    //    });
                    //Console.WriteLine("ledger subscribe successful");

                    Console.WriteLine($"next change: {server}");
                    await Task.Run(Console.ReadLine);

                    await client.ChangeServer(server);
                    ServerState? serverInfo = await client.ServerState(new ServerStateRequest());
                    string lineReserveFee = serverInfo.State.ValidatedLedger.ReserveInc.ToString();
                    string accReserveFee = serverInfo.State.ValidatedLedger.ReserveBase.ToString();
                    var _lineReserveFee = new Currency
                    {
                        Value = lineReserveFee,
                    };
                    var _accReserveFee = new Currency
                    {
                        Value = accReserveFee,
                    };
                    Console.WriteLine($"{nameof(lineReserveFee)} - {_lineReserveFee}");
                    Console.WriteLine($"{nameof(accReserveFee)} - {_accReserveFee}");
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    continue;
                }
            }
        });

        await task;
        Console.WriteLine("END");
    }

    private static async Task InitTestData(TestDataType serverType, bool withAccounts = true)
    {
        client = serverType switch
        {
            TestDataType.testNet => new XrplClient("wss://s.altnet.rippletest.net:51233"),
            TestDataType.devNet => new XrplClient("wss://s.devnet.rippletest.net:51233"),
            TestDataType.mainNet => new XrplClient("wss://xrplcluster.com"),
            //TestDataType.mainNet => new XrplClient("wss://s1.ripple.com"),
            TestDataType.standalone => new XrplClient($"ws://localhost:6006"),
            _ => throw new ArgumentOutOfRangeException(nameof(serverType), serverType, null)
        };

        client.connection.OnConnected += async () => { Console.WriteLine("CONNECTED"); };
        client.connection.OnWarning += (warning, message) =>
        {
            Console.WriteLine(warning);
            return Task.CompletedTask;
        };
        client.connection.OnServerWarning += (warning, message) =>
        {
            foreach (RippleResponseWarning? responseWarning in warning)
            {
                Console.WriteLine(responseWarning.Message);
            }

            return Task.CompletedTask;
        };
        client.connection.OnError += (error, message, s, data) =>
        {
            Console.WriteLine(error);
            Console.WriteLine(message);
            Console.WriteLine(s);
            Console.WriteLine(data);
            return Task.CompletedTask;
        };
        await client.Connect();

        if (!withAccounts)
        {
            return;
        }
        if (serverType == TestDataType.standalone)
        {
            await StandAloneUtils.FundAccount(client, walletPrimary, walletSecondary_1, walletSecondary_2, walletMultiSign, walletMultiSigner_1, walletMultiSigner_2, walletRegularKey, walletRegularKey_signer);
        }
        else if (client.Url().Contains("test"))
        {
            await TryFillAccounts(walletPrimary, walletSecondary_1, walletSecondary_2, walletMultiSign, walletMultiSigner_1, walletMultiSigner_2, walletRegularKey, walletRegularKey_signer);
        }
    }

    private static async Task TryFillAccounts(params XrplWallet[] wallets)
    {
        foreach (var xrplWallet in wallets)
        {
            try
            {
                var info = await client.GetXrpFreeBalance(xrplWallet.ClassicAddress);
                Console.WriteLine($"Balance {xrplWallet.ClassicAddress} - {info} XRP");

                if (info <= 10)
                {
                    var addFunds = await client.FundWallet(xrplWallet);
                    Console.WriteLine($"Fund {xrplWallet.ClassicAddress} - {addFunds.Balance} XRP");
                }
                continue;
            }
            catch (Exception e)
            {

            }
            var funded = await client.FundWallet(xrplWallet);
            Console.WriteLine($"Fund {xrplWallet.ClassicAddress} - {funded.Balance} XRP");
        }

    }

    private static void TestWalletFromText()
    {
        var wallet = XrplWallet.FromNormalizedText("random text for get new wallet", "salt", caseInsensitive: true, algorithm: XrplWallet.DEFAULT_ALGORITHM, masterAddress: null, kdf: TextWalletKdf.Sha256);
        Console.WriteLine(wallet.ClassicAddress);
        var wallet1 = XrplWallet.FromNormalizedText("random text for get new wallet", salt: null, caseInsensitive: true, algorithm: XrplWallet.DEFAULT_ALGORITHM, masterAddress: null, kdf: TextWalletKdf.Sha256);
        Console.WriteLine(wallet1.ClassicAddress);
        var wallet2 = XrplWallet.FromNormalizedText("random text for get new wallet", "salt", caseInsensitive: true, algorithm: XrplWallet.DEFAULT_ALGORITHM, masterAddress: null, kdf: TextWalletKdf.Pbkdf2);
        Console.WriteLine(wallet2.ClassicAddress);
        var wallet3 = XrplWallet.FromNormalizedText("random text for get new wallet", salt: null, caseInsensitive: true, algorithm: XrplWallet.DEFAULT_ALGORITHM, masterAddress: null, kdf: TextWalletKdf.Pbkdf2);
        Console.WriteLine(wallet3.ClassicAddress);
    }

    private static async Task Simulate()
    {
        var owner = walletPrimary;
        // Подписанты (могут быть любые аккаунты/ключи)
        var dest = walletSecondary_1;
        var tx = new Payment()
        {
            Account = owner.ClassicAddress,
            Destination = dest.ClassicAddress,
            Amount = new Currency
            {
                ValueAsXrp = 1,
            },
        };

        var result = await client.Simulate(
            new SimulateRequest()
            {
                Transaction = tx
            });
        if (result.TxJson is Payment { } payment)
        {

        }
        Console.WriteLine(result.EngineResult);
    }

    private static void WalletFromSeed()
    {
        //using System.Diagnostics;
        //using Xrpl.Wallet;
        var seed = "sEdSuqBPSQaood2DmNYVkwWTn1oQTj2";
        var wallet = XrplWallet.FromSeed(seed);
        Console.WriteLine(wallet.ClassicAddress);
        Console.WriteLine(wallet.PrivateKey);
        Console.WriteLine(wallet.PublicKey);
        Console.WriteLine(wallet.Seed);
    }

    private static void WalletGenerate()
    {
        //using System.Diagnostics;
        //using Xrpl.Wallet;
        var wallet = XrplWallet.Generate();
        Console.WriteLine(wallet.ClassicAddress);
        Console.WriteLine(wallet.PrivateKey);
        Console.WriteLine(wallet.PublicKey);
        Console.WriteLine(wallet.Seed);
    }

    private static async Task SubmitTestTx()
    {
        var client = new XrplClient("wss://s.altnet.rippletest.net:51233");

        client.connection.OnConnected += async () => { Console.WriteLine("CONNECTED"); };

        await client.Connect();

        Console.WriteLine("NEXT");

        var seed = "sEdSuqBPSQaood2DmNYVkwWTn1oQTj2";
        var wallet = XrplWallet.FromSeed(seed);

        var request = new AccountInfoRequest(wallet.ClassicAddress);
        var accountInfo = await client.AccountInfo(request);

        // prepare the transaction
        // the amount is expressed in drops, not XRP
        // see https://xrpl.org/basic-data-types.html#specifying-currency-amounts
        IPayment tx = new Payment()
        {
            Account = wallet.ClassicAddress,
            Destination = "rEqtEHKbinqm18wQSQGstmqg9SFpUELasT",
            Amount = new Currency
            {
                ValueAsXrp = 1,
            },
            Sequence = accountInfo.AccountData.Sequence,
        };

        // sign and submit the transaction
        Dictionary<string, dynamic> txJson = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(tx.ToJson());
        var txResult = await client.SubmitAndWait(txJson, wallet, autofill: true, failHard: false);
        Console.WriteLine(txResult.Meta.TransactionResult);

        //Submit response = await client.Submit(txJson, wallet);
        //Console.WriteLine(response.EngineResult);
    }

    private static async Task TestAmm()
    {
        var seed = "spucWfdp2GUXmEkKSQkzzVfL78gaM";
        var wallet = XrplWallet.FromSeed(seed);
        await TryFillAccounts(wallet);

        Console.WriteLine("NEXT");

        var request = new AccountInfoRequest(wallet.ClassicAddress);
        var accountInfo = await client.AccountInfo(request);

        // prepare the transaction
        // the amount is expressed in drops, not XRP
        // see https://xrpl.org/basic-data-types.html#specifying-currency-amounts
        //IPayment tx = new Payment()
        //{
        //    Sequence = accountInfo.AccountData.Sequence,
        //    Account = wallet.ClassicAddress,
        //    Amount = new Currency()
        //    {
        //        ValueAsXrp = 10
        //    },
        //    Destination = "rsWKbMAytbvShMJ5tWkiVhXt8xMsJq3wrA",
        //    Fee = new Currency() { ValueAsXrp = 0.2m },
        //};
        //IAMMCreate tx = new AMMCreate()
        //{
        //    Sequence = accountInfo.AccountData.Sequence,
        //    Account = wallet.ClassicAddress,
        //    Amount2 = new Currency()
        //    {
        //        Value = "2000000"
        //    },
        //    Amount = new Currency()
        //    {
        //        CurrencyCode = "5354533200000000000000000000000000000000",
        //        Issuer = "rsWKbMAytbvShMJ5tWkiVhXt8xMsJq3wrA",
        //        Value = "100",
        //    },
        //    Fee = new Currency() { Value = "200000" },
        //    TradingFee = 500,
        //    Flags = 0,
        //};
        //IAMMDeposit tx = new AMMDeposit()
        //{
        //    Sequence = accountInfo.AccountData.Sequence,
        //    Account = wallet.ClassicAddress,
        //    Amount = new Currency()
        //    {
        //        CurrencyCode = "5354533200000000000000000000000000000000",
        //        Issuer = "rsWKbMAytbvShMJ5tWkiVhXt8xMsJq3wrA",
        //        ValueAsNumber = 2
        //    },
        //    Amount2 = new Currency()
        //    {
        //        ValueAsXrp = 0.2m
        //    },
        //    Asset2 = new Common.IssuedCurrency()
        //    {
        //        Currency = "XRP",
        //    },
        //    Asset = new Common.IssuedCurrency()
        //    {
        //        Currency = "5354533200000000000000000000000000000000",
        //        Issuer = "rsWKbMAytbvShMJ5tWkiVhXt8xMsJq3wrA",
        //    },
        //    Fee = new Currency() {ValueAsXrp = 0.00002m},
        //    Flags = 1048576,
        //};
        //IAMMWithdraw tx = new AMMWithdraw()
        //{
        //    Sequence = accountInfo.AccountData.Sequence,
        //    Account = wallet.ClassicAddress,
        //    Amount = new Currency()
        //    {
        //        CurrencyCode = "5354533200000000000000000000000000000000",
        //        Issuer = "rsWKbMAytbvShMJ5tWkiVhXt8xMsJq3wrA",
        //        ValueAsNumber = 2
        //    },
        //    Amount2 = new Currency()
        //    {
        //        ValueAsXrp = 0.2m
        //    },
        //    Asset2 = new Common.IssuedCurrency()
        //    {
        //        Currency = "XRP",
        //    },
        //    Asset = new Common.IssuedCurrency()
        //    {
        //        Currency = "5354533200000000000000000000000000000000",
        //        Issuer = "rsWKbMAytbvShMJ5tWkiVhXt8xMsJq3wrA",
        //    },
        //    Fee = new Currency() {ValueAsXrp = 0.00002m},
        //    Flags = (uint)AMMWithdrawFlags.tfTwoAsset,
        //};

        ICheckCreate tx = new CheckCreate()
        {
            Sequence = accountInfo.AccountData.Sequence,
            Account = wallet.ClassicAddress,
            Destination = "rsWKbMAytbvShMJ5tWkiVhXt8xMsJq3wrA",
            SendMax = new Currency()
            {
                CurrencyCode = "5354533200000000000000000000000000000000",
                Issuer = "rsWKbMAytbvShMJ5tWkiVhXt8xMsJq3wrA",
                ValueAsNumber = 2,
            },
            Fee = new Currency()
            {
                ValueAsXrp = 0.00002m,
            },
        };
        Dictionary<string, dynamic> txJson = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(tx.ToJson());
        var response = await client.Submit(txJson, wallet);
        Console.WriteLine(response.EngineResult);
    }

    private static async Task WebsocketTest()
    {
        var isFinished = false;

        var server = "wss://s.altnet.rippletest.net:51233";

        var client = new XrplClient(server);
        client.connection.OnConnected += async () =>
        {
            Console.WriteLine("CONNECTED");
            try
            {
                var subscribe = await client.Subscribe(
                    new SubscribeRequest()
                    {
                        Streams = new List<StreamType>(
                            new[]
                            {
                                StreamType.Ledger,
                                StreamType.Transactions,
                            }),
                    });
                Console.WriteLine(subscribe);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        };

        client.connection.OnDisconnect += OnDisconnect;

        client.connection.OnError += OnError;

        client.connection.OnTransaction += OnTransaction;

        client.connection.OnLedgerClosed += r =>
        {
            Console.WriteLine($"MESSAGE RECEIVED: {r}");
            isFinished = true;
            return Task.CompletedTask;
        };

        await client.Connect();

        var task = Task.Run(
            async () =>
            {
                while (!isFinished)
                {
                    Debug.WriteLine($"WAITING: {DateTime.Now}");
                    await Task.Delay(1000);
                }
            });

        await task;

        await client.Disconnect();
    }

    private static Task OnTransaction(TransactionStream response)
    {
        Console.WriteLine(response.Transaction.TransactionType.ToString());
        return Task.CompletedTask;
    }

    private static Task OnError(string errorCode, string errorMessage, string error, dynamic data)
    {
        Console.WriteLine(errorCode);
        Console.WriteLine(errorMessage);
        Console.WriteLine(data);
        return Task.CompletedTask;
    }

    private static Task OnDisconnect(int? code, string? description)
    {
        Console.WriteLine($"Disconnected from XRPL with code: {code}, description: {description}");
        return Task.CompletedTask;
    }

    private static async Task WebsocketChangeServerTest()
    {
        var isFinished = false;
        var server1 = "wss://s1.ripple.com/";
        var server2 = "wss://s2.ripple.com/";
        var server3 = "wss://xrplcluster.com/";

        var client = new XrplClient(server1);

        client.connection.OnConnected += async () =>
        {
            Console.WriteLine("CONNECTED");
            var subscribe = await client.Subscribe(
                new SubscribeRequest()
                {
                    Streams = new List<StreamType>(
                        new[]
                        {
                            StreamType.Ledger,
                        }),
                });
        };

        client.connection.OnDisconnect += OnDisconnect;

        client.connection.OnError += OnError;

        client.connection.OnTransaction += OnTransaction;

        client.connection.OnLedgerClosed += r =>
        {
            Console.WriteLine($"MESSAGE RECEIVED: {r}");
            isFinished = true;
            return Task.CompletedTask;
        };

        await client.Connect();

        while (!isFinished)
        {
            Debug.WriteLine($"WAITING: {DateTime.Now}");
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }

        await Task.Delay(3000);
        isFinished = false;

        await client.connection.ChangeServer(server2);
        while (!isFinished)
        {
            Debug.WriteLine($"WAITING: {DateTime.Now}");
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }

        await Task.Delay(3000);
        isFinished = false;

        await client.connection.ChangeServer(server3);
        while (!isFinished)
        {
            Debug.WriteLine($"WAITING: {DateTime.Now}");
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }

        await Task.Delay(3000);
        isFinished = false;

        await client.connection.ChangeServer(server1);
        while (!isFinished)
        {
            Debug.WriteLine($"WAITING: {DateTime.Now}");
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }

        await Task.Delay(3000);
        isFinished = false;

        await client.connection.ChangeServer(server2);
        while (!isFinished)
        {
            Debug.WriteLine($"WAITING: {DateTime.Now}");
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }

        await Task.Delay(3000);
        isFinished = false;

        await client.connection.ChangeServer(server3);
        while (!isFinished)
        {
            Debug.WriteLine($"WAITING: {DateTime.Now}");
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }

        await Task.Delay(3000);
        isFinished = false;

        await client.connection.ChangeServer(server1);
        while (!isFinished)
        {
            Debug.WriteLine($"WAITING: {DateTime.Now}");
            Thread.Sleep(TimeSpan.FromSeconds(1));
        }

        await Task.Delay(3000);

        await client.Disconnect();
    }


    private static async Task MultiSignTest()
    {
        // Владелец мультисиг-аккаунта
        var owner = walletMultiSign;
        // Подписанты (могут быть любые аккаунты/ключи)
        var signer1 = walletMultiSigner_1;
        var signer2 = walletMultiSigner_2;

        await SetSigners(owner, signer1, signer2);
        var acc = await client.AccountInfo(new AccountInfoRequest(owner.ClassicAddress));

        // Комиссия для мультиподписи: ≈ baseFee × (1 + N_signers).
        // Берём openLedgerFee и умножаем на (1 + 2) = 3 (немного округляя вверх).

        var pay = new Payment
        {
            Account = owner.ClassicAddress, // платит владелец
            Destination = walletPrimary.ClassicAddress, // получатель
            Amount = new Currency
            {
                ValueAsXrp = 1
            },
            //Sequence = acc.AccountData.Sequence,
            //Fee = new Currency(){Value = "100"},
            //TransactionSignature = null,
            //SigningPublicKey = "",
        };
        //var partial1 = signer1.Sign(pay, multisign: true, signingFor: owner.ClassicAddress);
        //var partial2 = signer2.Sign(pay, multisign: true, signingFor: owner.ClassicAddress);
        var res = await client.SubmitMulti(pay, new List<XrplWallet>() { signer1, signer2 }, true);
        if (res is not { EngineResult: "tesSUCCESS" or "terQUEUED" })
        {
            throw new RippleException($"Invalid result, {res.EngineResult}");
        }
        //string combinedBlob = XrplWallet.CombineMultiSigners(partial1.TxBlob, partial2.TxBlob);
        //SubmitRequest request = new SubmitRequest { Command = "submit", TxBlob = combinedBlob, FailHard = false };
        //var response = await client.GRequest<Submit, SubmitRequest>(request);


        await DisableMaster(owner);
    }

    private static async Task SetSigners(XrplWallet owner, XrplWallet signer1, XrplWallet signer2)
    {
        // Проверьте: у owner достаточно резерва на SignerList (≈ +2 XRP * на подпись).
        var acc = await client.AccountInfo(new AccountInfoRequest(owner.ClassicAddress));

        // Создаём/обновляем список подписантов (2 из 2)
        var sls = new SignerListSet
        {
            Account = owner.ClassicAddress,
            SignerQuorum = 2,
            SignerEntries = new()
            {
                new SignerEntryWrapper{ SignerEntry = new SignerEntry { Account = signer1.ClassicAddress, SignerWeight = 1, WalletLocator = "test wallet sig 1"}},
                new SignerEntryWrapper{ SignerEntry = new SignerEntry { Account = signer2.ClassicAddress, SignerWeight = 1, WalletLocator = "test wallet sig 2"}},
            },
            Fee = new Currency { Value = "15" },              // нормальная комиссия
            Sequence = acc.AccountData.Sequence,
        };

        var slsSubmit = await client.SubmitAndWait(sls, owner, true, true);
    }

    private static async Task DisableMaster(XrplWallet owner)
    {
        AccountInfo acc;
        acc = await client.AccountInfo(new AccountInfoRequest(owner.ClassicAddress));

        var disableMaster = new AccountSet
        {
            Account = owner.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfDisableMaster,
            Fee = new Currency { Value = "15" },              // нормальная комиссия
            Sequence = acc.AccountData.Sequence,
        };
        await client.Submit(disableMaster, owner, true);
    }

}