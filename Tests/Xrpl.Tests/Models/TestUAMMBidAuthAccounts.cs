using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec;
using Xrpl.Client.Json;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Transactions;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.Models;

/// <summary>
/// <c>AMMBid.AuthAccounts</c> is an array of <c>AuthAccount</c> inner objects, while
/// <c>amm_info</c> reports the same accounts as <c>auth_accounts: [{"account": ...}]</c>; each
/// side keeps its own shape.
/// </summary>
[TestClass]
public class TestUAMMBidAuthAccounts
{
    private const string Seed = "snGHNrPbHrdUcszeuDEigMdC1Lyyd";
    private const string Authorized = "rGWrZyQqhTp9Xu7G5Pkayo7bXjH4k4QYpf";

    [TestMethod]
    public void AMMBid_WithAuthAccounts_SignsInTheInnerObjectShape()
    {
        XrplWallet wallet = XrplWallet.FromSeed(Seed);
        AMMBid bid = new AMMBid
        {
            Account = wallet.ClassicAddress,
            Asset = new IssuedCurrency { Currency = "XRP" },
            Asset2 = new IssuedCurrency { Currency = "USD", Issuer = "rhub8VRN55s94qWKDv6jmDy1pUykJzF3wq" },
            AuthAccounts = new List<AuthAccountWrapper> { new AuthAccountWrapper(Authorized) },
            Fee = new Currency { CurrencyCode = "XRP", Value = "12" },
            Sequence = 1,
            LastLedgerSequence = 100,
        };

        SignatureResult signed = wallet.Sign(bid);

        JsonObject decoded = XrplBinaryCodec.Decode(signed.TxBlob).AsObject();
        JsonArray authAccounts = decoded["AuthAccounts"]!.AsArray();
        Assert.HasCount(1, authAccounts);
        Assert.AreEqual(Authorized, authAccounts[0]!["AuthAccount"]!["Account"]!.GetValue<string>());
    }

    [TestMethod]
    public void AMMBidResponse_ReadsAuthAccountsFromATransaction()
    {
        const string json = """
            {
              "TransactionType": "AMMBid",
              "Account": "rPEPPER7kfTD9w2To4CQk6UCfuHM9c6GDY",
              "Asset": { "currency": "XRP" },
              "Asset2": { "currency": "USD", "issuer": "rhub8VRN55s94qWKDv6jmDy1pUykJzF3wq" },
              "AuthAccounts": [ { "AuthAccount": { "Account": "rGWrZyQqhTp9Xu7G5Pkayo7bXjH4k4QYpf" } } ]
            }
            """;

        AMMBidResponse bid = JsonSerializer.Deserialize<AMMBidResponse>(json, XrplJsonOptions.Default)!;

        Assert.HasCount(1, bid.AuthAccounts!);
        Assert.AreEqual(Authorized, bid.AuthAccounts![0].AuthAccount.Account);
    }

    [TestMethod]
    public void AuctionSlot_KeepsTheAmmInfoShape()
    {
        const string json = """
            {
              "account": "rPEPPER7kfTD9w2To4CQk6UCfuHM9c6GDY",
              "auth_accounts": [ { "account": "rGWrZyQqhTp9Xu7G5Pkayo7bXjH4k4QYpf" } ],
              "discounted_fee": 50,
              "expiration": "2026-09-27T12:00:00+0000",
              "price": { "currency": "039C99CD9AB0B70B32ECDA51EAAE471625608EA2", "issuer": "rE54zDvgnghAoPopCgvtiqWNq3dU5y836S", "value": "0" },
              "time_interval": 0
            }
            """;

        AuctionSlot slot = JsonSerializer.Deserialize<AuctionSlot>(json, XrplJsonOptions.Default)!;

        Assert.HasCount(1, slot.AuthAccounts);
        Assert.AreEqual(Authorized, slot.AuthAccounts[0].Account);
    }
}
