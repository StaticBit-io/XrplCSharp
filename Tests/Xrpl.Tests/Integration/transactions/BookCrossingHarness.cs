using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Amounts;
using Xrpl.BinaryCodec.Numbers;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Utils;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// Builds books on the node and checks <see cref="OfferCreateCrossing"/> against it: the
/// crossing is computed from a <see cref="DexSnapshot"/> read just before the same
/// <c>OfferCreate</c> is submitted, and every offer, pool and balance the node changed is compared
/// with the prediction to the last digit.
/// </summary>
internal sealed class BookCrossingHarness
{
    internal static readonly IssuedCurrency Xrp = new IssuedCurrency { Currency = "XRP" };

    private readonly IXrplClient _client;
    private readonly TestNodeType _nodeType;

    internal BookCrossingHarness(IXrplClient client, TestNodeType nodeType)
    {
        _client = client;
        _nodeType = nodeType;
    }

    internal IXrplClient Client => _client;

    internal async Task<XrplWallet[]> Wallets(int count)
    {
        XrplWallet[] wallets = Enumerable.Range(0, count).Select(_ => XrplWallet.Generate()).ToArray();
        await IntegrationTestConfig.TryFundWalletsAsync(_client, _nodeType, wallets);
        return wallets;
    }

    internal static IssuedCurrency Iou(string currency, XrplWallet issuer) =>
        new IssuedCurrency { Currency = currency, Issuer = issuer.ClassicAddress };

    internal static XrplAmount Amount(IssuedCurrency asset, string value) => XrplAmount.Parse(asset, value);

    internal static XrplAmount Drops(long drops) => XrplAmount.FromNumber(Xrp, (XrplNumber)drops);

    internal async Task Submit<T>(T transaction, XrplWallet wallet, string expected = "tesSUCCESS")
        where T : ITransactionRequest
    {
        T filled = await _client.Autofill(transaction);
        TransactionSummary result = await _client.SubmitAndWait(filled, wallet, false);
        Assert.AreEqual(expected, result.Meta.TransactionResult, $"{typeof(T).Name} by {wallet.ClassicAddress}");
    }

    internal Task Issuer(XrplWallet issuer, uint? transferRate = null, uint? tickSize = null) =>
        Submit(
            new AccountSet
            {
                Account = issuer.ClassicAddress,
                SetFlag = AccountSetAsfFlags.asfDefaultRipple,
                TransferRate = transferRate,
                TickSize = tickSize,
            },
            issuer);

    internal Task Trust(XrplWallet holder, IssuedCurrency asset, string limit = "1000000000") =>
        Submit(
            new TrustSet
            {
                Account = holder.ClassicAddress,
                LimitAmount = new Currency { CurrencyCode = asset.Currency, Issuer = asset.Issuer, Value = limit },
            },
            holder);

    /// <summary>A trust line with its settings: <paramref name="flags"/> as TrustSet takes them, qualities in billionths.</summary>
    internal Task TrustWith(XrplWallet holder, IssuedCurrency asset, string limit, TrustSetFlags flags, uint? qualityIn = null, uint? qualityOut = null) =>
        Submit(
            new TrustSet
            {
                Account = holder.ClassicAddress,
                LimitAmount = new Currency { CurrencyCode = asset.Currency, Issuer = asset.Issuer, Value = limit },
                Flags = flags,
                QualityIn = qualityIn,
                QualityOut = qualityOut,
            },
            holder);

    internal Task SetFlag(XrplWallet wallet, AccountSetAsfFlags flag) =>
        Submit(new AccountSet { Account = wallet.ClassicAddress, SetFlag = flag }, wallet);

    internal Task Pay(XrplWallet from, string to, XrplAmount amount) =>
        Submit(new Payment { Account = from.ClassicAddress, Destination = to, Amount = amount.ToCurrency() }, from);

    internal async Task Fund(XrplWallet issuer, XrplWallet holder, IssuedCurrency asset, string amount)
    {
        await Trust(holder, asset);
        await Pay(issuer, holder.ClassicAddress, Amount(asset, amount));
    }

    /// <summary>Trust lines, one holder's in sequence and different holders' at once.</summary>
    internal Task Trusts(params (XrplWallet Holder, IssuedCurrency Asset)[] lines) =>
        Task.WhenAll(lines
            .GroupBy(line => line.Holder.ClassicAddress)
            .Select(async group =>
            {
                foreach ((XrplWallet holder, IssuedCurrency asset) in group)
                    await Trust(holder, asset);
            }));

    /// <summary>Issuer payments, one issuer's in sequence and different issuers' at once.</summary>
    internal Task Pays(params (XrplWallet From, XrplWallet To, XrplAmount Amount)[] payments) =>
        Task.WhenAll(payments
            .GroupBy(payment => payment.From.ClassicAddress)
            .Select(async group =>
            {
                foreach ((XrplWallet from, XrplWallet to, XrplAmount amount) in group)
                    await Pay(from, to.ClassicAddress, amount);
            }));

    /// <summary>Places an offer that gives <paramref name="gets"/> for <paramref name="pays"/>.</summary>
    internal Task Offer(XrplWallet maker, XrplAmount gets, XrplAmount pays, OfferCreateFlags flags = 0, uint? expiration = null) =>
        Submit(
            new OfferCreate
            {
                Account = maker.ClassicAddress,
                TakerGets = gets.ToCurrency(),
                TakerPays = pays.ToCurrency(),
                Flags = flags,
                Expiration = expiration is { } seconds ? RippleEpoch.AddSeconds(seconds) : null,
            },
            maker);

    /// <summary>The close time of the last validated ledger, in seconds since the Ripple epoch.</summary>
    internal async Task<uint> LastCloseTime()
    {
        LOLedger header = await _client.Ledger(new LedgerRequest { LedgerIndex = new LedgerIndex(LedgerIndexType.Validated) }).Typed();
        LedgerEntity ledger = (LedgerEntity)header.LedgerEntity;
        return (uint)(ledger.CloseTime.Value - RippleEpoch).TotalSeconds;
    }

    /// <summary>Waits until a validated ledger closed after <paramref name="rippleSeconds"/>.</summary>
    internal async Task WaitForCloseAfter(uint rippleSeconds)
    {
        for (int attempt = 0; attempt < 120; attempt++)
        {
            if (await LastCloseTime() > rippleSeconds)
                return;

            await Task.Delay(1000);
        }

        Assert.Fail("no ledger closed after the expiration");
    }

    internal static readonly DateTime RippleEpoch = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    internal Task CreateAmm(XrplWallet creator, XrplAmount amount, XrplAmount amount2, ushort tradingFee) =>
        Submit(
            new AMMCreate
            {
                Account = creator.ClassicAddress,
                Amount = amount.ToCurrency(),
                Amount2 = amount2.ToCurrency(),
                TradingFee = tradingFee,
            },
            creator);

    /// <summary>The LP token of the pool trading the two assets.</summary>
    internal async Task<IssuedCurrency> LpToken(IssuedCurrency asset, IssuedCurrency asset2)
    {
        AMMInfoResponse info = await _client.AmmInfo(new AMMInfoRequest { Asset = asset, Asset2 = asset2 }).Typed();
        return new IssuedCurrency { Currency = info.Amm.LPTokenBalance.CurrencyCode, Issuer = info.Amm.LPTokenBalance.Issuer };
    }

    /// <summary>
    /// Computes the crossing from a fresh snapshot, submits the same <c>OfferCreate</c>, and
    /// asserts the node did exactly what was predicted.
    /// </summary>
    internal async Task<OfferCrossingResult> CrossAndCompare(
        XrplWallet taker,
        XrplAmount takerPays,
        XrplAmount takerGets,
        OfferCreateFlags flags = 0)
    {
        OfferCreate transaction = await _client.Autofill(new OfferCreate
        {
            Account = taker.ClassicAddress,
            TakerPays = takerPays.ToCurrency(),
            TakerGets = takerGets.ToCurrency(),
            Flags = flags,
        });

        LedgerRules rules = await LedgerRules.FromNodeAsync(_client);
        DexSnapshot snapshot = await DexSnapshot.FromNodeAsync(_client, taker.ClassicAddress, takerPays.Asset, takerGets.Asset);
        ulong fee = ulong.Parse(transaction.Fee.Value, CultureInfo.InvariantCulture);
        OfferCrossingResult predicted = OfferCreateCrossing.Cross(snapshot, taker.ClassicAddress, takerPays, takerGets, fee, flags, rules);

        TransactionSummary result;
        try
        {
            result = await _client.SubmitAndWait(transaction, taker, false);
        }
        catch (TransactionFailedException failed) when (failed.EngineResult?.StartsWith("tec", StringComparison.Ordinal) == true)
        {
            // A tec is applied: wait for its ledger to read the metadata.
            result = failed.Result ?? await Validated(failed.Hash);
        }

        Assert.AreEqual(predicted.EngineResult, result.Meta.TransactionResult, "the engine result");

        AssertOffers(result.Meta, predicted, taker.ClassicAddress);
        await AssertPools(snapshot, predicted.Pools);
        AssertBalances(result.Meta, predicted.BalanceChanges, snapshot.Pools.Select(p => p.Account));
        return predicted;
    }

    private async Task<TransactionSummary> Validated(string hash)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                TransactionSummary tx = await _client.TxV2(new TxRequest(hash) { ApiVersion = 2 }).Typed();
                if (tx.Validated == true)
                    return tx;
            }
            catch (RippledException exception) when (exception.Response?.Error == XrplErrorCodes.TxnNotFound)
            {
            }

            await Task.Delay(1000);
        }

        Assert.Fail($"{hash} was not validated");
        return null;
    }

    /// <summary>
    /// Computes <paramref name="payment"/> from a fresh snapshot, submits it, and asserts the
    /// node did exactly what was predicted: result, delivered amount, offers, pools and balances.
    /// </summary>
    internal async Task<PaymentFlowResult> PayAndCompare(XrplWallet sender, Payment payment)
    {
        Payment transaction = await _client.Autofill(payment);
        LedgerRules rules = await LedgerRules.FromNodeAsync(_client);
        DexSnapshot snapshot = await DexSnapshot.FromNodeAsync(_client, transaction);
        PaymentFlowResult predicted = PaymentFlow.Evaluate(snapshot, transaction, rules);

        TransactionSummary result;
        try
        {
            result = await _client.SubmitAndWait(transaction, sender, false);
        }
        catch (TransactionFailedException failed) when (failed.EngineResult?.StartsWith("tec", StringComparison.Ordinal) == true)
        {
            result = failed.Result ?? await Validated(failed.Hash);
        }
        catch (TransactionFailedException failed)
        {
            // A tem, tel or tef never reaches a ledger.
            Assert.AreEqual(predicted.EngineResult, failed.EngineResult, "the engine result");
            Assert.IsFalse(predicted.Applied, "a result that never reaches a ledger");
            return predicted;
        }

        Assert.AreEqual(predicted.EngineResult, result.Meta.TransactionResult, "the engine result");
        if (predicted.EngineResult == "tesSUCCESS")
            Assert.AreEqual(predicted.DeliveredAmount.Value, result.Meta.ActuallyDeliveredAmount.ToXrplAmount(), "delivered_amount");

        AssertOffers(result.Meta, predicted.Offers, null, null, null);
        await AssertPools(snapshot, predicted.Pools);
        AssertBalances(result.Meta, predicted.BalanceChanges, snapshot.Pools.Select(p => p.Account));
        return predicted;
    }

    private static void AssertOffers(Meta meta, OfferCrossingResult predicted, string taker) =>
        AssertOffers(meta, predicted.Offers, predicted.PlacedTakerPays, predicted.PlacedTakerGets, taker);

    private static void AssertOffers(Meta meta, IReadOnlyList<OfferChange> predictedOffers, XrplAmount? placedPays, XrplAmount? placedGets, string taker)
    {
        Dictionary<string, OfferChange> expected = predictedOffers.ToDictionary(o => o.Index, StringComparer.OrdinalIgnoreCase);
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        LOOffer placed = null;
        foreach (AffectedNode node in meta.AffectedNodes)
        {
            if (node.CreatedNode != null && node.CreatedNode.TryGetNew(out LOOffer created))
            {
                placed = created;
                continue;
            }

            if (node.DeletedNode != null && node.DeletedNode.TryGetFinal(out LOOffer _))
            {
                string index = node.DeletedNode.LedgerIndex;
                Assert.IsTrue(expected.TryGetValue(index, out OfferChange change), $"offer {index} deleted but not predicted");
                Assert.IsTrue(change.Deleted, $"offer {index} deleted, predicted to remain {change.TakerPays} / {change.TakerGets}");
                seen.Add(index);
            }
            else if (node.ModifiedNode != null && node.ModifiedNode.TryGetFinal(out LOOffer final))
            {
                string index = node.ModifiedNode.LedgerIndex;
                Assert.IsTrue(expected.TryGetValue(index, out OfferChange change), $"offer {index} changed but not predicted");
                Assert.IsFalse(change.Deleted, $"offer {index} remains, predicted deleted");
                Assert.AreEqual(change.TakerPays.Value, final.TakerPays.ToXrplAmount(), $"offer {index}: what it still asks for");
                Assert.AreEqual(change.TakerGets.Value, final.TakerGets.ToXrplAmount(), $"offer {index}: what it still gives");
                seen.Add(index);
            }
        }

        foreach (OfferChange change in predictedOffers)
            Assert.IsTrue(seen.Contains(change.Index), $"offer {change.Index} predicted to change, untouched on the ledger");

        if (placedPays == null)
        {
            Assert.IsNull(placed, "no offer is placed");
            return;
        }

        Assert.IsNotNull(placed, "the remainder is placed");
        Assert.AreEqual(taker, placed.Account);
        Assert.AreEqual(placedPays.Value, placed.TakerPays.ToXrplAmount(), "the placed offer's TakerPays");
        Assert.AreEqual(placedGets.Value, placed.TakerGets.ToXrplAmount(), "the placed offer's TakerGets");
    }

    private async Task AssertPools(DexSnapshot snapshot, IReadOnlyList<AmmPoolChange> predictedPools)
    {
        foreach (DexAmmPool pool in snapshot.Pools)
        {
            AMMInfoResponse info = await _client.AmmInfo(new AMMInfoRequest { AmmAccount = pool.Account }).Typed();
            AmmPoolChange change = predictedPools.SingleOrDefault(p => p.Account == pool.Account);
            XrplAmount expected = change?.Balance ?? pool.Balance;
            XrplAmount expected2 = change?.Balance2 ?? pool.Balance2;
            XrplAmount actual = info.Amm.Amount.ToXrplAmount();
            XrplAmount actual2 = info.Amm.Amount2.ToXrplAmount();
            if (!XrplAmount.SameAsset(actual.Asset, expected.Asset))
                (actual, actual2) = (actual2, actual);

            Assert.AreEqual(expected, actual, $"pool {pool.Account}: first balance");
            Assert.AreEqual(expected2, actual2, $"pool {pool.Account}: second balance");
        }
    }

    /// <summary>
    /// Every account's balance changes, as the metadata records them, against the prediction -
    /// both ways, so an unpredicted change fails too. Pools are compared through amm_info.
    /// </summary>
    internal static void AssertBalances(Meta meta, IReadOnlyList<BalanceChange> predicted, IEnumerable<string> poolAccounts)
    {
        Dictionary<string, List<Currency>> changes = BalanceChanges.GetBalanceChanges(meta);
        HashSet<string> pools = poolAccounts.ToHashSet(StringComparer.Ordinal);
        List<(string Account, XrplAmount Change)> recorded = changes
            .Where(entry => !pools.Contains(entry.Key))
            .SelectMany(entry => entry.Value.Select(c => (entry.Key, c.ToXrplAmount())))
            .Where(entry => entry.Item2.Kind == AmountKind.Xrp || !pools.Contains(entry.Item2.Asset.Issuer))
            .Where(entry => !entry.Item2.IsZero)
            .ToList();

        foreach (BalanceChange change in predicted)
        {
            XrplAmount[] match = recorded
                .Where(r => r.Account == change.Account && XrplAmount.SameAsset(r.Change.Asset, change.Change.Asset))
                .Select(r => r.Change)
                .ToArray();
            Assert.HasCount(1, match, $"{change.Account}: {change.Change} predicted, {match.Length} changes on the ledger");
            Assert.AreEqual(change.Change, match[0], $"{change.Account}: {change.Change} predicted, {match[0]} on the ledger");
        }

        foreach ((string account, XrplAmount change) in recorded)
        {
            Assert.IsTrue(
                predicted.Any(c => c.Account == account && XrplAmount.SameAsset(c.Change.Asset, change.Asset)),
                $"{account}: {change} on the ledger, not predicted");
        }
    }
}
