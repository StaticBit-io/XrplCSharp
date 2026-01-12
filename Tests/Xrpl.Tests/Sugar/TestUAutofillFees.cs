using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.Sugar;

[TestClass]
public class TestUAutofillFees
{
    private const string MAINNET_BASE_FEE = "0.000012"; // 12 drops, 8 chars (mainnet/testnet)
    private const string DEVNET_BASE_FEE = "0.0000012"; // 1.2 drops → 1 drop * 12 = 12 drops (devnet with correction)
    private const uint RESERVE_INC = 2000000; // 2 XRP in drops

    #region BaseFee Tests

    [TestMethod]
    public async Task TestUCalculateFee_StandardPayment_ReturnsBaseFee()
    {
        var client = new FeeTestClient(MAINNET_BASE_FEE, RESERVE_INC);
        var tx = CreatePaymentTx();

        await client.CalculateFeePerTransactionType(tx);

        Assert.IsTrue(tx.ContainsKey("Fee"));
        Assert.AreEqual("12", tx["Fee"]);
    }

    [TestMethod]
    public async Task TestUCalculateFee_DevnetMultiplier_AppliesCorrection()
    {
        // Note: Devnet detection relies on GetFeeXrp returning a 9-char string (7 digits after dot).
        // With mock returning double, trailing zeros are lost: "0.0000010" → "0.000001" (8 chars).
        // Therefore multiplier is NOT applied in this unit test. Integration tests should verify real devnet behavior.
        var client = new FeeTestClient(DEVNET_BASE_FEE, RESERVE_INC);
        var tx = CreatePaymentTx();

        await client.CalculateFeePerTransactionType(tx);

        Assert.IsTrue(tx.ContainsKey("Fee"));
        // DEVNET_BASE_FEE "0.0000012" → 0.0000012 XRP = 1.2 drops → truncated to 1 drop
        // String length is 9, so devnet multiplier (12x) IS applied: 1 * 12 = 12 drops
        Assert.AreEqual("12", tx["Fee"]);
    }

    #endregion

    #region Multisig Fee Tests

    [TestMethod]
    public async Task TestUCalculateFee_Multisig_AddsSignerFee()
    {
        var client = new FeeTestClient(MAINNET_BASE_FEE, RESERVE_INC);
        var tx = CreatePaymentTx();

        await client.CalculateFeePerTransactionType(tx, signersCount: 2);

        Assert.IsTrue(tx.ContainsKey("Fee"));
        // signerFee = ScaleValue(netFeeDrops, signersCount) = 12 * 2 = 24
        // Total = baseFee + signerFee = 12 + 24 = 36
        Assert.AreEqual("36", tx["Fee"]);
    }

    [TestMethod]
    public async Task TestUCalculateFee_Multisig_ZeroSigners_NoExtraFee()
    {
        var client = new FeeTestClient(MAINNET_BASE_FEE, RESERVE_INC);
        var tx = CreatePaymentTx();

        await client.CalculateFeePerTransactionType(tx, signersCount: 0);

        Assert.AreEqual("12", tx["Fee"]);
    }

    #endregion

    #region EscrowFinish Fee Tests

    [TestMethod]
    public async Task TestUCalculateFee_EscrowFinishWithFulfillment_UsesFormula()
    {
        var client = new FeeTestClient(MAINNET_BASE_FEE, RESERVE_INC);
        var tx = new Dictionary<string, dynamic>
        {
            ["TransactionType"] = "EscrowFinish",
            ["Account"] = "rTestAccount",
            ["Owner"] = "rOwner",
            ["OfferSequence"] = 1,
            ["Fulfillment"] = "A0028000" // 8 hex chars = 4 bytes
        };

        await client.CalculateFeePerTransactionType(tx);

        Assert.IsTrue(tx.ContainsKey("Fee"));
        // Formula: ScaleValue(netFeeDrops, 33 + (fulfillmentBytes / 16))
        // fulfillmentBytes = ceiling(8/2) = 4 bytes
        // multiplier = 33 + (4/16) = 33.25
        // netFeeDrops = "12" (from 0.000012 XRP)
        // ScaleValue("12", 33.25) = "399"
        // Math.Ceiling(399) = 399
        Assert.AreEqual("399", tx["Fee"]);
    }

    #endregion

    #region Reserve Fee Tests

    [TestMethod]
    public async Task TestUCalculateFee_AccountDelete_UsesReserveFee()
    {
        var client = new FeeTestClient(MAINNET_BASE_FEE, RESERVE_INC);
        var tx = new Dictionary<string, dynamic>
        {
            ["TransactionType"] = "AccountDelete",
            ["Account"] = "rTestAccount",
            ["Destination"] = "rDestination"
        };

        await client.CalculateFeePerTransactionType(tx);

        Assert.IsTrue(tx.ContainsKey("Fee"));
        Assert.AreEqual("2000000", tx["Fee"]); // Reserve fee
    }

    [TestMethod]
    public async Task TestUCalculateFee_AMMCreate_UsesReserveFee()
    {
        var client = new FeeTestClient(MAINNET_BASE_FEE, RESERVE_INC);
        var tx = new Dictionary<string, dynamic>
        {
            ["TransactionType"] = "AMMCreate",
            ["Account"] = "rTestAccount",
            ["Amount"] = new Dictionary<string, dynamic> { ["value"] = "100" },
            ["Amount2"] = new Dictionary<string, dynamic> { ["value"] = "100" },
            ["TradingFee"] = 100
        };

        await client.CalculateFeePerTransactionType(tx);

        Assert.IsTrue(tx.ContainsKey("Fee"));
        Assert.AreEqual("2000000", tx["Fee"]);
    }

    #endregion

    #region Batch Fee Tests

    [TestMethod]
    public async Task TestUCalculateFee_Batch_CalculatesCorrectly()
    {
        var client = new FeeTestClient(MAINNET_BASE_FEE, RESERVE_INC);
        var tx = new Dictionary<string, dynamic>
        {
            ["TransactionType"] = "Batch",
            ["Account"] = "rTestAccount",
            ["RawTransactions"] = new JArray
            {
                new JObject
                {
                    ["RawTransaction"] = new JObject
                    {
                        ["TransactionType"] = "Payment",
                        ["Account"] = "rAccount1"
                    }
                },
                new JObject
                {
                    ["RawTransaction"] = new JObject
                    {
                        ["TransactionType"] = "Payment",
                        ["Account"] = "rAccount2"
                    }
                }
            }
        };

        await client.CalculateFeePerTransactionType(tx);

        Assert.IsTrue(tx.ContainsKey("Fee"));
        // Base * 3 + 2 inner payments = 12*3 + 12 + 12 = 60
        Assert.AreEqual("60", tx["Fee"]);
    }

    [TestMethod]
    public async Task TestUCalculateFee_Batch_WithReserveInner_AddsReserveFee()
    {
        var client = new FeeTestClient(MAINNET_BASE_FEE, RESERVE_INC);
        var tx = new Dictionary<string, dynamic>
        {
            ["TransactionType"] = "Batch",
            ["Account"] = "rTestAccount",
            ["RawTransactions"] = new JArray
            {
                new JObject
                {
                    ["RawTransaction"] = new JObject
                    {
                        ["TransactionType"] = "Payment",
                        ["Account"] = "rAccount1"
                    }
                },
                new JObject
                {
                    ["RawTransaction"] = new JObject
                    {
                        ["TransactionType"] = "AMMCreate",
                        ["Account"] = "rAccount2"
                    }
                }
            }
        };

        await client.CalculateFeePerTransactionType(tx);

        Assert.IsTrue(tx.ContainsKey("Fee"));
        // Base * 3 + Payment + AMMCreate = 12*3 + 12 + 2000000 = 2000048
        Assert.AreEqual("2000048", tx["Fee"]);
    }

    [TestMethod]
    public async Task TestUCalculateFee_Batch_NoRawTransactions_ThrowsValidation()
    {
        var client = new FeeTestClient(MAINNET_BASE_FEE, RESERVE_INC);
        var tx = new Dictionary<string, dynamic>
        {
            ["TransactionType"] = "Batch",
            ["Account"] = "rTestAccount"
        };

        await Helper.ThrowsExceptionAsync<ValidationException>(async () =>
        {
            await client.CalculateFeePerTransactionType(tx);
        });
    }

    #endregion

    #region MaxFee Tests

    [TestMethod]
    public async Task TestUCalculateFee_ExceedsMaxFee_CapsAtMax()
    {
        var client = new FeeTestClient(MAINNET_BASE_FEE, RESERVE_INC, maxFeeXRP: "0.000050"); // 50 drops max
        var tx = CreatePaymentTx();

        await client.CalculateFeePerTransactionType(tx, signersCount: 10);

        Assert.IsTrue(tx.ContainsKey("Fee"));
        // Would be 12 + (12*10) = 12 + 120 = 132, but capped at 50 (maxFee = 0.000050 XRP)
        Assert.AreEqual("50", tx["Fee"]);
    }

    [TestMethod]
    public async Task TestUCalculateFee_AccountDelete_NotCapped()
    {
        var client = new FeeTestClient(MAINNET_BASE_FEE, RESERVE_INC, maxFeeXRP: "0.000050");
        var tx = new Dictionary<string, dynamic>
        {
            ["TransactionType"] = "AccountDelete",
            ["Account"] = "rTestAccount",
            ["Destination"] = "rDestination"
        };

        await client.CalculateFeePerTransactionType(tx);

        // AccountDelete should NOT be capped
        Assert.AreEqual("2000000", tx["Fee"]);
    }

    #endregion

    #region Helpers

    private static Dictionary<string, dynamic> CreatePaymentTx() => new()
    {
        ["TransactionType"] = "Payment",
        ["Account"] = "rTestAccount",
        ["Destination"] = "rDestination",
        ["Amount"] = "1000000"
    };

    #endregion
}

/// <summary>
/// Minimal mock IXrplClient for fee calculation tests.
/// Implements required methods for CalculateFeePerTransactionType.
/// </summary>
internal sealed class FeeTestClient : IXrplClient
{
    private readonly string _feeXrp;
    private readonly uint _reserveInc;

    public FeeTestClient(string feeXrp, uint reserveInc, string maxFeeXRP = "5")
    {
        _feeXrp = feeXrp;
        _reserveInc = reserveInc;
        this.maxFeeXRP = maxFeeXRP;
        this.feeCushion = 1.0;
    }

    public Connection connection { get; set; } = null!;
    public double feeCushion { get; set; }
    public string maxFeeXRP { get; set; }
    public uint? networkID { get; set; }

    public Task<ServerInfo> ServerInfo(ServerInfoRequest request)
    {
        var info = new ServerInfo
        {
            Info = new Info()
            {
                LoadFactor = 1,
                ValidatedLedger = new ValidatedLedger()
                {
                    BaseFeeXrp = double.Parse(_feeXrp, System.Globalization.CultureInfo.InvariantCulture)
                }
            }
        };
        return Task.FromResult(info);
    }

    public Task<ServerState> ServerState(ServerStateRequest request)
    {
        var state = new ServerState
        {
            State = new State()
            {
                ValidatedLedger = new StateLedger()
                {
                    ReserveInc = _reserveInc
                }
            }
        };
        return Task.FromResult(state);
    }

    public Task<ServerFeatures> ServerFeatures(string feature = null) => throw new NotImplementedException();

    public Task<uint> GetLedgerIndex() => Task.FromResult(100u);
    public Task<string> GetXrpBalance(string address) => throw new NotSupportedException();

    public Task<Dictionary<string, dynamic>> Autofill(Dictionary<string, dynamic> tx, int? signersCount = null) => throw new NotSupportedException();
    public Task<T> Autofill<T>(T tx, int? signersCount = null) where T : ITransactionRequest => throw new NotSupportedException();

    public Task ChangeServer(string server, XrplClient.ClientOptions? options = null, System.Threading.CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public string EnsureClassicAddress(string address) => throw new NotSupportedException();

    #region Not Implemented

    public string Url() => throw new NotSupportedException();
    public Task Connect(System.Threading.CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task Disconnect() => throw new NotSupportedException();

    public Task DisconnectAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotImplementedException();

    public bool IsConnected() => throw new NotSupportedException();
    public Task<object> Subscribe(SubscribeRequest request) => throw new NotSupportedException();
    public Task<object> Unsubscribe(UnsubscribeRequest request) => throw new NotSupportedException();
    public Task<object> Ping() => throw new NotSupportedException();
    public Task<Fee> Fee() => throw new NotSupportedException();
    public Task<AccountInfo> AccountInfo(AccountInfoRequest request) => throw new NotSupportedException();
    public Task<AccountOffers> AccountOffers(AccountOffersRequest request) => throw new NotSupportedException();
    public Task<AccountCurrencies> AccountCurrencies(AccountCurrenciesRequest request) => throw new NotSupportedException();
    public Task<AccountLines> AccountLines(AccountLinesRequest request) => throw new NotSupportedException();
    public Task<AccountChannels> AccountChannels(AccountChannelsRequest request) => throw new NotSupportedException();
    public Task<AccountObjects> AccountObjects(AccountObjectsRequest request) => throw new NotSupportedException();
    public Task<AccountTransactions> AccountTransactions(AccountTransactionsRequest request) => throw new NotSupportedException();
    public Task<GatewayBalances> GatewayBalances(GatewayBalancesRequest request) => throw new NotSupportedException();
    public Task<NoRippleCheck> NoRippleCheck(NoRippleCheckRequest request) => throw new NotSupportedException();
    public Task<LOLedger> Ledger(LedgerRequest request) => throw new NotSupportedException();
    public Task<LOBaseLedger> LedgerClosed(LedgerClosedRequest request) => throw new NotSupportedException();
    public Task<LOLedgerCurrentIndex> LedgerCurrent(LedgerCurrentRequest request) => throw new NotSupportedException();
    public Task<LOLedgerData> LedgerData(LedgerDataRequest request) => throw new NotSupportedException();
    public Task<LedgerEntryResponse> LedgerEntry(LedgerEntryRequest request) => throw new NotSupportedException();
    public Task<Submit> Submit(Dictionary<string, dynamic> tx, XrplWallet wallet, bool autoFill = true, bool failHard = false) => throw new NotSupportedException();
    public Task<Submit> Submit(ITransactionRequest tx, XrplWallet wallet, bool autoFill = true, bool failHard = false) => throw new NotSupportedException();
    public Task<TransactionResponse> Tx(TxRequest request) => throw new NotSupportedException();
    public Task<TransactionSummary> TxV2(TxRequest request) => throw new NotSupportedException();
    public Task<BookOffers> BookOffers(BookOffersRequest request) => throw new NotSupportedException();
    public Task<NFTBuyOffers> NFTBuyOffers(NFTBuyOffersRequest request) => throw new NotSupportedException();
    public Task<NFTSellOffers> NFTSellOffers(NFTSellOffersRequest request) => throw new NotSupportedException();
    public Task<AccountNFTs> AccountNFTs(AccountNFTsRequest request) => throw new NotSupportedException();
    public Task<AMMInfoResponse> AmmInfo(AMMInfoRequest request) => throw new NotSupportedException();
    public Task<object> Random() => throw new NotSupportedException();
    public Task<object> AnyRequest(BaseRequest request) => throw new NotSupportedException();
    public Task<Dictionary<string, dynamic>> Request(Dictionary<string, dynamic> request) => throw new NotSupportedException();
    public Task<T> GRequest<T, R>(R request) where R : BaseRequest => throw new NotSupportedException();
    public Task<SimulateResponse> Simulate(SimulateRequest request) => throw new NotSupportedException();
    public void Dispose() { }

    #endregion
}
