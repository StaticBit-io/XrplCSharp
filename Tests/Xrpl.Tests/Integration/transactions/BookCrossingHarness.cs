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
using Xrpl.Models;
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
        where T : ITransactionRequest =>
        await SubmitWithMeta(transaction, wallet, expected);

    internal async Task<TransactionSummary> SubmitWithMeta<T>(T transaction, XrplWallet wallet, string expected = "tesSUCCESS")
        where T : ITransactionRequest
    {
        T filled = await _client.Autofill(transaction);
        TransactionSummary result = await _client.SubmitAndWait(filled, wallet, false);
        Assert.AreEqual(expected, result.Meta.TransactionResult, $"{typeof(T).Name} by {wallet.ClassicAddress}");
        return result;
    }

    /// <summary>A credential of <paramref name="type"/> for <paramref name="subject"/>, accepted by it unless told otherwise.</summary>
    internal async Task Credential(XrplWallet issuer, XrplWallet subject, string type, uint? expiration = null, bool accept = true)
    {
        await Submit(
            new CredentialCreate
            {
                Account = issuer.ClassicAddress,
                Subject = subject.ClassicAddress,
                CredentialType = type,
                Expiration = expiration is { } seconds ? RippleEpoch.AddSeconds(seconds) : null,
            },
            issuer);
        if (accept)
            await Submit(new CredentialAccept { Account = subject.ClassicAddress, Issuer = issuer.ClassicAddress, CredentialType = type }, subject);
    }

    /// <summary>A permissioned domain owned by <paramref name="owner"/>, accepting the credentials given; returns its DomainID.</summary>
    internal async Task<string> Domain(XrplWallet owner, params (XrplWallet Issuer, string Type)[] accepted)
    {
        TransactionSummary result = await SubmitWithMeta(
            new PermissionedDomainSet
            {
                Account = owner.ClassicAddress,
                AcceptedCredentials = accepted
                    .Select(a => new AcceptedCredentialWrapper { Credential = new AcceptedCredential { Issuer = a.Issuer.ClassicAddress, CredentialType = a.Type } })
                    .ToList(),
            },
            owner);
        AffectedNode created = result.Meta.AffectedNodes.Single(n => n.CreatedNode?.LedgerEntryType == LedgerEntryType.PermissionedDomain);
        return created.CreatedNode.LedgerIndex;
    }

    /// <summary>The ledger index of the credential of <paramref name="type"/> that <paramref name="issuer"/> gave <paramref name="subject"/>.</summary>
    internal static string CredentialId(XrplWallet subject, XrplWallet issuer, string type) =>
        global::Xrpl.Utils.Hashes.Hashes.HashCredential(subject.ClassicAddress, issuer.ClassicAddress, HexType(type));

    /// <summary>A credential type as the ledger stores it: its bytes in upper-case hex.</summary>
    internal static string HexType(string type) =>
        string.Concat(System.Text.Encoding.UTF8.GetBytes(type).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

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

    /// <summary>Places an offer that gives <paramref name="gets"/> for <paramref name="pays"/>, in a domain when one is named.</summary>
    internal Task Offer(XrplWallet maker, XrplAmount gets, XrplAmount pays, OfferCreateFlags flags = 0, uint? expiration = null, string domainId = null) =>
        Submit(
            new OfferCreate
            {
                Account = maker.ClassicAddress,
                TakerGets = gets.ToCurrency(),
                TakerPays = pays.ToCurrency(),
                Flags = flags,
                Expiration = expiration is { } seconds ? RippleEpoch.AddSeconds(seconds) : null,
                DomainID = domainId,
            },
            maker);

    /// <summary>Places an offer and returns its sequence, for a later <c>OfferSequence</c>.</summary>
    internal async Task<uint> OfferWithSequence(XrplWallet maker, XrplAmount gets, XrplAmount pays)
    {
        OfferCreate filled = await _client.Autofill(new OfferCreate
        {
            Account = maker.ClassicAddress,
            TakerGets = gets.ToCurrency(),
            TakerPays = pays.ToCurrency(),
        });
        TransactionSummary result = await _client.SubmitAndWait(filled, maker, false);
        Assert.AreEqual("tesSUCCESS", result.Meta.TransactionResult, "the offer to cancel later");
        return filled.Sequence.Value;
    }

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
        // Bounded by the clock, not by attempts: under a parallel run a standalone node closes
        // ledgers more slowly, and each read takes longer.
        System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
        while (waited.Elapsed < TimeSpan.FromMinutes(5))
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
        OfferCreateFlags flags = 0,
        DexSnapshotOptions options = null)
    {
        OfferCreate transaction = await _client.Autofill(new OfferCreate
        {
            Account = taker.ClassicAddress,
            TakerPays = takerPays.ToCurrency(),
            TakerGets = takerGets.ToCurrency(),
            Flags = flags,
        });

        LedgerRules rules = await LedgerRules.FromNodeAsync(_client);
        DexSnapshot snapshot = await DexSnapshot.FromNodeAsync(_client, taker.ClassicAddress, takerPays.Asset, takerGets.Asset, options);
        ulong fee = ulong.Parse(transaction.Fee.Value, CultureInfo.InvariantCulture);
        OfferCrossingResult predicted = OfferCreateCrossing.Cross(snapshot, taker.ClassicAddress, takerPays, takerGets, fee, flags, rules);
        return await SubmitAndCompare(taker, transaction, snapshot, predicted);
    }

    /// <summary>
    /// Computes <paramref name="offer"/> from a fresh snapshot read for it, submits it, and
    /// asserts the node did exactly what was predicted - including the codes the offer's own
    /// checks return.
    /// </summary>
    internal async Task<OfferCrossingResult> CrossAndCompare(XrplWallet taker, OfferCreate offer, DexSnapshotOptions options = null)
    {
        OfferCreate transaction = await _client.Autofill(offer);
        LedgerRules rules = await LedgerRules.FromNodeAsync(_client);
        DexSnapshot snapshot = await DexSnapshot.FromNodeAsync(_client, transaction, options);
        OfferCrossingResult predicted = OfferCreateCrossing.Cross(snapshot, transaction, rules);
        return await SubmitAndCompare(taker, transaction, snapshot, predicted);
    }

    private async Task<OfferCrossingResult> SubmitAndCompare(XrplWallet taker, OfferCreate transaction, DexSnapshot snapshot, OfferCrossingResult predicted)
    {

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
        catch (TransactionFailedException failed)
        {
            // A tem, tel or tef never reaches a ledger.
            Assert.AreEqual(predicted.EngineResult, failed.EngineResult, "the engine result");
            Assert.IsFalse(predicted.Applied, "a result that never reaches a ledger");
            return predicted;
        }

        Assert.AreEqual(predicted.EngineResult, result.Meta.TransactionResult, "the engine result");
        Assert.IsTrue(predicted.Applied, "a tes or tec result is applied");

        AssertOffers(result.Meta, snapshot, predicted, transaction.Account);
        await AssertPools(snapshot, predicted.Pools);
        AssertFills(snapshot, predicted.Fills, predicted.Offers, predicted.Pools);
        AssertBalances(result.Meta, predicted.BalanceChanges, snapshot.Pools.Select(p => p.Account));
        AssertTrustLines(result.Meta, predicted.TrustLines);
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
    internal async Task<PaymentFlowResult> PayAndCompare(XrplWallet sender, Payment payment, DexSnapshotOptions options = null)
    {
        Payment transaction = await _client.Autofill(payment);
        LedgerRules rules = await LedgerRules.FromNodeAsync(_client);
        DexSnapshot snapshot = await DexSnapshot.FromNodeAsync(_client, transaction, options);
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

        AssertOffers(result.Meta, snapshot, predicted.Offers, predicted.Fills, null, null, null);
        await AssertPools(snapshot, predicted.Pools);
        AssertFills(snapshot, predicted.Fills, predicted.Offers, predicted.Pools);
        AssertBalances(result.Meta, predicted.BalanceChanges, snapshot.Pools.Select(p => p.Account));
        AssertTrustLines(result.Meta, predicted.TrustLines);
        return predicted;
    }

    /// <summary>The trust lines the metadata created and deleted, against the prediction, both ways.</summary>
    internal static void AssertTrustLines(Meta meta, IReadOnlyList<TrustLineChange> predicted)
    {
        static (string, string, string) Key(string a, string b, string currency) =>
            string.CompareOrdinal(a, b) <= 0 ? (a, b, currency) : (b, a, currency);

        HashSet<(string, string, string, bool)> recorded = new HashSet<(string, string, string, bool)>();
        foreach (AffectedNode node in meta.AffectedNodes)
        {
            if (node.CreatedNode != null && node.CreatedNode.TryGetNew(out LORippleState created))
            {
                (string a, string b, string c) = Key(created.LowLimit.Issuer, created.HighLimit.Issuer, created.Balance.CurrencyCode);
                recorded.Add((a, b, c, true));
            }
            else if (node.DeletedNode != null && node.DeletedNode.TryGetFinal(out LORippleState deleted))
            {
                (string a, string b, string c) = Key(deleted.LowLimit.Issuer, deleted.HighLimit.Issuer, deleted.Balance.CurrencyCode);
                recorded.Add((a, b, c, false));
            }
        }

        HashSet<(string, string, string, bool)> expected = predicted
            .Select(l => (l.Account, l.Peer, l.Currency, l.Created))
            .ToHashSet();
        Assert.IsTrue(
            recorded.SetEquals(expected),
            $"trust lines: predicted [{string.Join(", ", expected)}], on the ledger [{string.Join(", ", recorded)}]");
    }

    private static void AssertOffers(Meta meta, DexSnapshot snapshot, OfferCrossingResult predicted, string taker) =>
        AssertOffers(meta, snapshot, predicted.Offers, predicted.Fills, predicted.PlacedTakerPays, predicted.PlacedTakerGets, taker);

    private static void AssertOffers(
        Meta meta,
        DexSnapshot snapshot,
        IReadOnlyList<OfferChange> predictedOffers,
        IReadOnlyList<OfferFill> fills,
        XrplAmount? placedPays,
        XrplAmount? placedGets,
        string taker)
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

            if (node.DeletedNode != null && node.DeletedNode.TryGetFinal(out LOOffer last))
            {
                string index = node.DeletedNode.LedgerIndex;
                Assert.IsTrue(expected.TryGetValue(index, out OfferChange change), $"offer {index} deleted but not predicted");
                Assert.IsTrue(change.Deleted, $"offer {index} deleted, predicted to remain {change.TakerPays} / {change.TakerGets}");
                Assert.IsNotNull(change.Reason, $"offer {index}: deleted without a reason");
                AssertFilled(index, snapshot, fills, last);
                if (change.Reason == OfferRemovalReason.Consumed)
                {
                    Assert.IsTrue(
                        last.TakerPays.ToXrplAmount().IsZero || last.TakerGets.ToXrplAmount().IsZero,
                        $"offer {index}: predicted consumed, deleted with {last.TakerPays.ToXrplAmount()} / {last.TakerGets.ToXrplAmount()} left");
                }

                seen.Add(index);
            }
            else if (node.ModifiedNode != null && node.ModifiedNode.TryGetFinal(out LOOffer final))
            {
                string index = node.ModifiedNode.LedgerIndex;
                Assert.IsTrue(expected.TryGetValue(index, out OfferChange change), $"offer {index} changed but not predicted");
                Assert.IsFalse(change.Deleted, $"offer {index} remains, predicted deleted");
                Assert.IsNull(change.Reason, $"offer {index}: remains, with a removal reason");
                Assert.AreEqual(change.TakerPays.Value, final.TakerPays.ToXrplAmount(), $"offer {index}: what it still asks for");
                Assert.AreEqual(change.TakerGets.Value, final.TakerGets.ToXrplAmount(), $"offer {index}: what it still gives");
                AssertFilled(index, snapshot, fills, final);
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

    /// <summary>
    /// The offer's fills, replayed from its amounts in the snapshot - each subtraction rounded, as
    /// the ledger stores it - leave what the metadata's final fields show.
    /// </summary>
    private static void AssertFilled(string index, DexSnapshot snapshot, IReadOnlyList<OfferFill> fills, LOOffer after)
    {
        DexOffer original = snapshot.Offers.Single(o => string.Equals(o.Index, index, StringComparison.OrdinalIgnoreCase));
        XrplAmount pays = original.TakerPays;
        XrplAmount gets = original.TakerGets;
        foreach (OfferFill fill in fills.Where(f => string.Equals(f.OfferIndex, index, StringComparison.OrdinalIgnoreCase)))
        {
            pays = XrplAmountMath.Subtract(pays, fill.In);
            gets = XrplAmountMath.Subtract(gets, fill.Out);
        }

        Assert.AreEqual(after.TakerPays.ToXrplAmount(), pays, $"offer {index}: its fills replayed leave what it asks for");
        Assert.AreEqual(after.TakerGets.ToXrplAmount(), gets, $"offer {index}: its fills replayed leave what it gives");
    }

    /// <summary>The fills add up to what was taken of each offer and to each pool's change, and run pass by pass.</summary>
    private static void AssertFills(DexSnapshot snapshot, IReadOnlyList<OfferFill> fills, IReadOnlyList<OfferChange> offers, IReadOnlyList<AmmPoolChange> pools)
    {
        for (int i = 1; i < fills.Count; i++)
            Assert.IsTrue(fills[i - 1].Pass <= fills[i].Pass, "the fills run pass by pass");

        foreach (OfferChange change in offers)
        {
            List<OfferFill> taken = fills.Where(f => string.Equals(f.OfferIndex, change.Index, StringComparison.OrdinalIgnoreCase)).ToList();
            XrplAmount received = taken.Aggregate(XrplAmount.Zero(change.FilledTakerPays.Asset), (sum, f) => XrplAmountMath.Add(sum, f.In));
            XrplAmount gave = taken.Aggregate(XrplAmount.Zero(change.FilledTakerGets.Asset), (sum, f) => XrplAmountMath.Add(sum, f.Out));
            Assert.AreEqual(change.FilledTakerPays, received, $"offer {change.Index}: its fills received");
            Assert.AreEqual(change.FilledTakerGets, gave, $"offer {change.Index}: its fills gave");
        }

        foreach (DexAmmPool pool in snapshot.Pools)
        {
            AmmPoolChange change = pools.SingleOrDefault(p => p.Account == pool.Account);
            foreach ((XrplAmount before, XrplAmount after) in new[] { (pool.Balance, change?.Balance ?? pool.Balance), (pool.Balance2, change?.Balance2 ?? pool.Balance2) })
            {
                // Replayed fill by fill, as the engine moves the balance, each step rounded.
                XrplAmount balance = before;
                foreach (OfferFill fill in fills.Where(f => f.IsPool && f.Owner == pool.Account))
                {
                    if (XrplAmount.SameAsset(fill.In.Asset, before.Asset))
                        balance = XrplAmountMath.Add(balance, fill.In);
                    if (XrplAmount.SameAsset(fill.Out.Asset, before.Asset))
                        balance = XrplAmountMath.Subtract(balance, fill.Out);
                }

                Assert.AreEqual(after, balance, $"pool {pool.Account}: its fills replayed give its balance of {before.Asset.Currency}");
            }
        }
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
