////See https://aka.ms/new-console-template for more information

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using System.Diagnostics;

using Xrpl.AddressCodec;
using Xrpl.BinaryCodec;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Subscriptions;
using Xrpl.Models.Transactions;
using Xrpl.Models.Utils;
using Xrpl.Sugar;
using Xrpl.Utils;
using Xrpl.Utils.Hashes;
using Xrpl.Wallet;

using Signer = Xrpl.Models.Transactions.Signer;

namespace MyApp;

internal class Program
{
    //private static IXrplClient client = new XrplClient("wss://s.altnet.rippletest.net:51233");
    private static IXrplClient client = new XrplClient("wss://s2.ripple.com");
    //private static IXrplClient client = new XrplClient("wss://s.devnet.rippletest.net:51233");
    private static async Task Main(string[] args)
    {
        var wallet = XrplWallet.FromNormalizedText("random text for get new wallet", "salt", true, null);
        try
        {
            client.connection.OnConnected += async () => { Console.WriteLine("CONNECTED"); };
            client.connection.OnWarning += (warning, message) =>
            {
                Console.WriteLine(warning);
                return Task.CompletedTask;
            };
            client.connection.OnServerWarning += (warning, message) =>
            {
                foreach (var responseWarning in warning)
                {
                    Console.WriteLine(responseWarning.Message);
                }

                return Task.CompletedTask;
            };
            await client.Connect();
            //await Simulate();
            await MultiSignTest();
            //await TestBatchSingle(); //Одно-аккаунтный Batch: у всех внутренних tx один владелец
            //await TestBatchSingleMultiSign(); //Одно-аккаунтный Batch с мультиподписью
            //await TestBatchMultiAccounts(); //Много-аккаунтный Batch: у каждого участника single-sig (через BatchSigners
            //await TestBatchMultiAccountsWithTopMultiSign(); //Много-аккаунтный Batch: внешняя подпись Multi-Sig
            //await TestBatchMultiAccountsWithInnerMultiSign(); //Много-аккаунтный Batch: внутри Multi-Sig

            await client.Disconnect();
        }
        catch (Exception e)
        {
            throw;
        }

        //await SampleClient();
        //WalletFromSeed();
        //WalletGenerate();
        //await SubmitTestTx();
        //await WebsocketTest();
        //await WebsocketChangeServerTest();
    }

    private static async Task Simulate()
    {
        var owner = XrplWallet.FromSeed("sEdTqY3295pcs14tHzHG3ZpLzR4VFND");
        // Подписанты (могут быть любые аккаунты/ключи)
        var dest = XrplWallet.FromSeed("sEdT5jzoGrDayKXtXsUHmg8X9ScGAwR");
        IPayment tx = new Payment()
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

    private static async Task SampleClient()
    {
        //using System.Diagnostics;
        //using Xrpl.Client;
        //var client = new XrplClient("wss://s.altnet.rippletest.net:51233");
        //client.OnConnected += async () =>
        //{
        //    Console.WriteLine("CONNECTED");
        //};
        //await client.Connect();
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
        //using Newtonsoft.Json;
        //using Xrpl.Client;
        //using Xrpl.Models.Methods;
        //using Xrpl.Models.Transactions;
        //using Xrpl.Wallet;

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
        var txResult = await client.SubmitAndWait(txJson, autofill: true, failHard: false, wallet);
        Console.WriteLine(txResult.Meta.TransactionResult);

        //Submit response = await client.Submit(txJson, wallet);
        //Console.WriteLine(response.EngineResult);
    }

    private static async Task TestAmm()
    {
        //using Newtonsoft.Json;
        //using Xrpl.Client;
        //using Xrpl.Models.Methods;
        //using Xrpl.Models.Transactions;
        //using Xrpl.Wallet;

        var seed = "spucWfdp2GUXmEkKSQkzzVfL78gaM";
        var wallet = XrplWallet.FromSeed(seed);
        var client = new XrplClient("wss://s.altnet.rippletest.net:51233");

        client.connection.OnConnected += async () => { Console.WriteLine("CONNECTED"); };

        await client.Connect();

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

    private static async Task TestBatchSingle()
    {
        //var seed = "sEd7jV7wWpVqav4srkGLa2Hj5CvBxeB";
        var seed = "spucWfdp2GUXmEkKSQkzzVfL78gaM";
        var wallet = XrplWallet.FromSeed(seed);
        //var client = new XrplClient("wss://batch.nerdnest.xyz");
        var client = new XrplClient("wss://s.devnet.rippletest.net:51233");
        //var client = new XrplClient("wss://s.altnet.rippletest.net:51233");

        client.connection.OnConnected += async () => { Console.WriteLine("CONNECTED"); };

        await client.Connect();

        Console.WriteLine("NEXT");

        var request = new AccountInfoRequest(wallet.ClassicAddress);
        var accountInfo = await client.AccountInfo(request);

        //var flags = BatchGlobalFlags.tfInnerBatchTxn;
        // Внутренний Payment #1
        var payment1 = new Payment
        {
            Sequence = accountInfo.AccountData.Sequence + 1,
            Account = wallet.ClassicAddress,
            Amount = new Currency
            {
                ValueAsXrp = 3m,
            },
            Destination = "rsWKbMAytbvShMJ5tWkiVhXt8xMsJq3wrA",

            // Fee внутри батча всегда должна быть "0" → проставим, но потом нормализуем
            Fee = 0,
        }.ToBatchTx();

        // Внутренний Payment #2
        var payment2 = new Payment
        {
            Sequence = accountInfo.AccountData.Sequence + 2,
            Account = wallet.ClassicAddress,
            Amount = new Currency
            {
                ValueAsXrp = 3m,
            },
            Destination = "rsWKbMAytbvShMJ5tWkiVhXt8xMsJq3wrA",

            // Fee внутри батча всегда должна быть "0" → проставим, но потом нормализуем
            Fee = 0,
        }.ToBatchTx();

        // Собираем внешний Batch
        var tx = new Batch
        {
            Account = wallet.ClassicAddress,
            Sequence = accountInfo.AccountData.Sequence,
            Flags = BatchFlags.tfAllOrNothing, // режим: или все выполняются, или ни одна
            RawTransactions = new List<RawTransactionWrapper>
            {
                payment1,
                payment2,
            },
            Fee = 70,
        };

        // sign and submit the transaction
        var response = await client.Submit(tx, wallet);

        //Dictionary<string, dynamic> txJson = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(tx.ToJson());
        //var response = await client.Submit(txJson, wallet);
        var txr = response.Transaction as BatchResponse;
        Console.WriteLine(response.EngineResult);
    }
    private static async Task TestBatchSingleMultiSign()
    {
        // Владелец мультисиг-аккаунта
        var owner = XrplWallet.FromSeed("sEdTqY3295pcs14tHzHG3ZpLzR4VFND");
        // Подписанты (могут быть любые аккаунты/ключи)
        var signer1 = XrplWallet.FromSeed("sEdT5jzoGrDayKXtXsUHmg8X9ScGAwR");
        var signer2 = XrplWallet.FromSeed("sEdVpoUUJrqnn2EhJBhieg6gKRP3Nax");

        //var client = new XrplClient("wss://batch.nerdnest.xyz");
        var client = new XrplClient("wss://s.devnet.rippletest.net:51233");
        //var client = new XrplClient("wss://s.altnet.rippletest.net:51233");

        client.connection.OnConnected += async () => { Console.WriteLine("CONNECTED"); };

        await client.Connect();

        Console.WriteLine("NEXT");

        var request = new AccountInfoRequest(owner.ClassicAddress);
        var accountInfo = await client.AccountInfo(request);

        //var flags = BatchGlobalFlags.tfInnerBatchTxn;
        // Внутренний Payment #1
        var payment1 = new Payment
        {
            Sequence = accountInfo.AccountData.Sequence + 1,
            Account = owner.ClassicAddress,
            Amount = new Currency
            {
                ValueAsXrp = 3m,
            },
            Destination = "rsWKbMAytbvShMJ5tWkiVhXt8xMsJq3wrA",

            // Fee внутри батча всегда должна быть "0" → проставим, но потом нормализуем
            Fee = 0,
        }.ToBatchTx();

        // Внутренний Payment #2
        var payment2 = new Payment
        {
            Sequence = accountInfo.AccountData.Sequence + 2,
            Account = owner.ClassicAddress,
            Amount = new Currency
            {
                ValueAsXrp = 3m,
            },
            Destination = "rsWKbMAytbvShMJ5tWkiVhXt8xMsJq3wrA",

            // Fee внутри батча всегда должна быть "0" → проставим, но потом нормализуем
            Fee = 0,
        }.ToBatchTx();

        // Собираем внешний Batch
        var tx = new Batch
        {
            Account = owner.ClassicAddress,
            Sequence = accountInfo.AccountData.Sequence,
            Flags = BatchFlags.tfAllOrNothing, // режим: или все выполняются, или ни одна
            RawTransactions = new List<RawTransactionWrapper>
            {
                payment1,
                payment2,
            },
            Fee = 70
        };

        // sign and submit the transaction
        var response = await client.SubmitMulti(tx, new List<XrplWallet>() { signer1, signer2 }, true);
        var txr = response.Transaction as BatchResponse;
        Console.WriteLine(response.EngineResult);
    }

    private static async Task TestBatchMultiAccounts()
    {
        var seed1 = "spucWfdp2GUXmEkKSQkzzVfL78gaM";
        var seed2 = "shfrkzgPQQ6kB4WMXwwu1UNSyQLeH";
        var seed3 = "sEdT5jzoGrDayKXtXsUHmg8X9ScGAwR";

        var w1 = XrplWallet.FromSeed(seed1);
        var w2 = XrplWallet.FromSeed(seed2);
        var w3 = XrplWallet.FromSeed(seed3);

        // Внутренний #1 — от w1 (seq = next для w1)
        var p1 = new Payment
        {
            Account = w1.ClassicAddress,
            Destination = w2.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1.1m },
            Fee = new Currency { Value = "0" },
        }.ToBatchTx();

        // Внутренний #2 — от w2 (seq = next для w2)
        var p2 = new Payment
        {
            Account = w2.ClassicAddress,
            Destination = w1.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1.2m },
            Fee = new Currency { Value = "0" },
        }.ToBatchTx();

        // Внутренний #3 — от w3 (seq = next для w3)
        var p3 = new Payment
        {
            Account = w3.ClassicAddress,
            Destination = w1.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1.3m },
            Fee = new Currency { Value = "0" },
        }.ToBatchTx();

        // Внешний Batch — платит комиссию w1 (может быть любой плательщик)
        var batch = new Batch
        {
            Account = w1.ClassicAddress,
            Flags = BatchFlags.tfAllOrNothing,
            RawTransactions = new List<RawTransactionWrapper> { p1, p2, p3 },
            Fee = new Currency() { Value = "70" }
            // Рекомендуется проставить LLS и Fee (не показано для краткости)
        };

        var submitRes = await client.SubmitMultiBatch(batch, new[] { w1, w2, w3 }, true);
        var txr = submitRes.Transaction as BatchResponse;
        Console.WriteLine($"{submitRes.EngineResult}: {submitRes.EngineResultMessage}");
    }

    private static async Task TestBatchMultiAccountsWithInnerMultiSign() //todo пока ошибка подписей от сервера
    {
        // Владелец мультисиг-аккаунта
        var owner = XrplWallet.FromSeed("sEdTqY3295pcs14tHzHG3ZpLzR4VFND");
        // Подписанты (могут быть любые аккаунты/ключи)
        var signer1 = XrplWallet.FromSeed("sEdT5jzoGrDayKXtXsUHmg8X9ScGAwR");
        var signer2 = XrplWallet.FromSeed("sEdVpoUUJrqnn2EhJBhieg6gKRP3Nax");


        var seed1 = "spucWfdp2GUXmEkKSQkzzVfL78gaM";
        var seed2 = "shfrkzgPQQ6kB4WMXwwu1UNSyQLeH";

        var w1 = XrplWallet.FromSeed(seed1);
        var w2 = XrplWallet.FromSeed(seed2);

        // Внутренний #1 — от w1 (seq = next для w1)
        var p1 = new Payment
        {
            Account = w1.ClassicAddress,
            Destination = w2.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1.1m },
            Fee = new Currency { Value = "0" },
        }.ToBatchTx();

        // Внутренний #2 — от w2 (seq = next для w2)
        var p2 = new Payment
        {
            Account = w2.ClassicAddress,
            Destination = w1.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1.2m },
            Fee = new Currency { Value = "0" },
        }.ToBatchTx();

        // Внутренний #3 — от w3 (seq = next для w3)
        var p3 = new Payment
        {
            Account = owner.ClassicAddress,
            Destination = w1.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1.3m },
            Fee = new Currency { Value = "0" },
        }.ToBatchTx();

        // Внешний Batch — платит комиссию w1 (может быть любой плательщик)
        var batch = new Batch
        {
            Account = w1.ClassicAddress,
            Flags = BatchFlags.tfAllOrNothing,
            RawTransactions = new List<RawTransactionWrapper> { p1, p2, p3 },
            Fee = new Currency() { Value = "70" }
        };

        var submitRes = await client.SubmitMultiBatch(batch, new[] { w1, w2, owner, signer1, signer2 }, true);
        var txr = submitRes.Transaction as BatchResponse;
        Console.WriteLine($"{submitRes.EngineResult}: {submitRes.EngineResultMessage}");
    }
    private static async Task TestBatchMultiAccountsWithTopMultiSign() //todo пока ошибка подписей от сервера
    {
        // Владелец мультисиг-аккаунта
        var owner = XrplWallet.FromSeed("sEdTqY3295pcs14tHzHG3ZpLzR4VFND");
        // Подписанты (могут быть любые аккаунты/ключи)
        var signer1 = XrplWallet.FromSeed("sEdT5jzoGrDayKXtXsUHmg8X9ScGAwR");
        var signer2 = XrplWallet.FromSeed("sEdVpoUUJrqnn2EhJBhieg6gKRP3Nax");


        var seed1 = "spucWfdp2GUXmEkKSQkzzVfL78gaM";
        var seed2 = "shfrkzgPQQ6kB4WMXwwu1UNSyQLeH";

        var w1 = XrplWallet.FromSeed(seed1);
        var w2 = XrplWallet.FromSeed(seed2);

        // Внутренний #1 — от w1 (seq = next для w1)
        var p1 = new Payment
        {
            Account = w1.ClassicAddress,
            Destination = w2.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1.1m },
            Fee = new Currency { Value = "0" },
        }.ToBatchTx();

        // Внутренний #2 — от w2 (seq = next для w2)
        var p2 = new Payment
        {
            Account = w2.ClassicAddress,
            Destination = w1.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1.2m },
            Fee = new Currency { Value = "0" },
        }.ToBatchTx();

        // Внутренний #3 — от w3 (seq = next для w3)
        var p3 = new Payment
        {
            Account = owner.ClassicAddress,
            Destination = w1.ClassicAddress,
            Amount = new Currency { ValueAsXrp = 1.3m },
            Fee = new Currency { Value = "0" },
        }.ToBatchTx();

        // Внешний Batch — корневой аккаунт = owner (мультисиг)
        var batch = new Batch
        {
            Account = owner.ClassicAddress,
            Flags = BatchFlags.tfAllOrNothing,
            RawTransactions = new List<RawTransactionWrapper> { p1, p2, p3 },
            Fee = new Currency() { Value = "70" }
            // Рекомендуется проставить LLS и Fee (не показано для краткости)
        };

        var submitRes = await client.SubmitMultiBatch(batch, new[] { w1, w2, owner,signer1,signer2 }, true);

        var txr = submitRes.Transaction as BatchResponse;
        Console.WriteLine($"{submitRes.EngineResult}: {submitRes.EngineResultMessage}");
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
        var owner = XrplWallet.FromSeed("sEdTqY3295pcs14tHzHG3ZpLzR4VFND");
        // Подписанты (могут быть любые аккаунты/ключи)
        var signer1 = XrplWallet.FromSeed("sEdT5jzoGrDayKXtXsUHmg8X9ScGAwR");
        var signer2 = XrplWallet.FromSeed("sEdVpoUUJrqnn2EhJBhieg6gKRP3Nax");

        //await SetSigners(owner, signer1, signer2);
        var acc = await client.AccountInfo(new AccountInfoRequest(owner.ClassicAddress));

        // Комиссия для мультиподписи: ≈ baseFee × (1 + N_signers).
        // Берём openLedgerFee и умножаем на (1 + 2) = 3 (немного округляя вверх).

        var pay = new Payment
        {
            Account = owner.ClassicAddress, // платит владелец
            Destination = "rsKbfunjbcP6u3BgFy6Nd3BFHSuND2hZLa", // получатель
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
                new SignerEntryWrapper{ SignerEntry = new SignerEntry { Account = signer1.ClassicAddress, SignerWeight = 1 }},
                new SignerEntryWrapper{ SignerEntry = new SignerEntry { Account = signer2.ClassicAddress, SignerWeight = 1, }},
            },
            Fee = new Currency { Value = "15" },              // нормальная комиссия
            Sequence = acc.AccountData.Sequence,
        };

        var slsSubmit = await client.Submit(sls, owner, true);
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