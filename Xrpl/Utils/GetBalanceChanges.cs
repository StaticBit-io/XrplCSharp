using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Xrpl.Models;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Transactions;

// Contains Balance, IssuedCurrencyAmount

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/utils/getBalanceChanges.ts

namespace Xrpl.Utils;

/// <summary>
/// Utilities for computing balance changes (XRP, issued currencies and MPTs)
/// from transaction metadata's affected nodes.
/// </summary>
public static class BalanceChanges
{
    /// <summary>
    /// Internal representation of a single balance change for an account.
    /// </summary>
    private class BalanceChange
    {
        public string Account { get; set; } = string.Empty;

        public Currency Balance { get; set; } = null!;
    }

    /// <summary>
    /// Computes XRP balance change from a modified ledger node.
    /// </summary>
    private static BalanceChange GetXrpQuantity(NodeInfo node)
    {
        if (node.LedgerEntryType != LedgerEntryType.AccountRoot)
        {
            return null;
        }

        decimal? value = null;
        string account = null;
        var newField = node.NewFields is LOAccountRoot { } newNode ? newNode : null;
        var finalField = node.FinalFields is LOAccountRoot { } finalNode ? finalNode : null;
        var previousField = node.PreviousFields is LOAccountRoot { } previousNode ? previousNode : null;

        if (newField?.Balance is { } newBalance)
        {
            value = newBalance.ValueAsXrp;
            account = newField.Account;
        }
        else if (previousField?.Balance is { } previousBalance && finalField?.Balance is { } finalBalance)
        {
            value = finalBalance.ValueAsXrp - previousBalance.ValueAsXrp;
            account = finalField.Account;
        }

        if (value is not { } or 0)
        {
            return null;
        }

        return new BalanceChange
        {
            Account = account,
            Balance = new Currency()
            {
                ValueAsXrp = value,
            },
        };
    }

    /// <summary>
    /// Computes issued-currency (trustline) balance change from a modified node.
    /// Returns positive change for low limit issuer and negative for high limit issuer.
    /// </summary>
    private static List<BalanceChange> GetTrustlineQuantity(NodeInfo node)
    {
        if (node.LedgerEntryType != LedgerEntryType.RippleState)
        {
            return null;
        }

        decimal? value = null;
        string account = null;
        string code = null;
        string issuer = null;
        var newField = node.NewFields is LORippleState { } newNode ? newNode : null;
        var finalField = node.FinalFields is LORippleState { } finalNode ? finalNode : null;
        var previousField = node.PreviousFields is LORippleState { } previousNode ? previousNode : null;

        if (newField?.Balance is { } newBalance)
        {
            value = newBalance.ValueAsNumber;
            account = newField.LowLimit.Issuer;
            code = newBalance.CurrencyCode;
            issuer = newField.HighLimit.Issuer;
        }
        else if (previousField?.Balance is { } previousBalance && finalField?.Balance is { } finalBalance)
        {
            value = finalBalance.ValueAsNumber - previousBalance.ValueAsNumber;
            account = finalField.LowLimit.Issuer;
            code = finalBalance.CurrencyCode;
            issuer = finalField.HighLimit.Issuer;
        }

        if (value is not { } or 0)
        {
            return null;
        }

        // Create issued currency amount for difference
        var change = new Currency()
        {
            CurrencyCode = code,
            Issuer = issuer,
            ValueAsNumber = value.Value,
        };

        return new List<BalanceChange>
        {
            // Low limit issuer receives positive delta
            new BalanceChange
            {
                Account = account,
                Balance = change,
            },

            // High limit issuer receives negative delta
            new BalanceChange
            {
                Account = issuer,
                Balance = new Currency
                {
                    CurrencyCode = code,
                    Issuer = account,
                    ValueAsNumber = -value.Value,
                },
            },
        };
    }

    /// <summary>
    /// One holder's <c>MPToken</c> node. <see cref="Delta"/> is null when the metadata does not say
    /// whether the amount changed; <see cref="FinalAmount"/> is then the amount the holder ends with.
    /// </summary>
    private sealed class MptHolderChange
    {
        public string Account { get; set; } = string.Empty;

        public string IssuanceId { get; set; } = string.Empty;

        public decimal? Delta { get; set; }

        public decimal FinalAmount { get; set; }
    }

    /// <summary>
    /// Computes MPT balance changes: each holder's <c>MPToken</c> amount, and the issuer's side as
    /// the opposite of the change in the issuance's <c>OutstandingAmount</c>, the way a trust line
    /// reports its issuer.
    /// </summary>
    /// <remarks>
    /// <c>MPTAmount</c> is a default field: a holder with nothing does not carry it, and rippled
    /// lists in <c>PreviousFields</c> only the fields the node had before. A modified <c>MPToken</c>
    /// that ends with an amount but lists no previous one therefore either went from zero to that
    /// amount or did not change it. <c>OutstandingAmount</c> is a required field and is always
    /// listed when it changes - and when the issuance node is absent, it did not change - and the
    /// holders' changes of one issuance add up to its change, so a single such holder is resolved
    /// from that sum. More than one is left unchanged.
    /// </remarks>
    private static List<BalanceChange> GetMptQuantities(List<(NodeInfo Node, bool Deleted)> nodes)
    {
        List<MptHolderChange> holders = new List<MptHolderChange>();
        Dictionary<string, (string Issuer, decimal Delta)> issuances = new Dictionary<string, (string, decimal)>(StringComparer.Ordinal);

        foreach ((NodeInfo node, bool deleted) in nodes)
        {
            if (node.LedgerEntryType == LedgerEntryType.MPToken)
            {
                if (ReadMptHolder(node, deleted) is { } holder)
                    holders.Add(holder);
            }
            else if (node.LedgerEntryType == LedgerEntryType.MPTokenIssuance)
            {
                LOMPTokenIssuance created = node.NewFields as LOMPTokenIssuance;
                LOMPTokenIssuance final = node.FinalFields as LOMPTokenIssuance;
                LOMPTokenIssuance previous = node.PreviousFields as LOMPTokenIssuance;
                LOMPTokenIssuance state = created ?? final;
                if (state?.MPTokenIssuanceID is not { } id)
                    continue;

                // decimal before subtracting: OutstandingAmount is a ulong, and a decrease would wrap.
                decimal delta = created != null
                    ? created.OutstandingAmount ?? 0
                    : previous?.OutstandingAmount is { } before
                        ? (deleted ? 0m : (decimal)(final.OutstandingAmount ?? 0)) - before
                        : 0;
                issuances[id] = (state.Issuer, delta);
            }
        }

        foreach (IGrouping<string, MptHolderChange> issuance in holders.GroupBy(h => h.IssuanceId, StringComparer.Ordinal))
        {
            List<MptHolderChange> unknown = issuance.Where(h => h.Delta == null).ToList();
            if (unknown.Count == 1)
            {
                // No issuance node means OutstandingAmount did not change: a transfer between holders.
                decimal outstandingDelta = issuances.TryGetValue(issuance.Key, out (string Issuer, decimal Delta) outstanding)
                    ? outstanding.Delta
                    : 0;
                decimal rest = outstandingDelta - issuance.Where(h => h.Delta != null).Sum(h => h.Delta.Value);
                if (rest == 0 || rest == unknown[0].FinalAmount)
                    unknown[0].Delta = rest;
            }
        }

        List<BalanceChange> changes = new List<BalanceChange>();
        foreach (MptHolderChange holder in holders)
        {
            if (holder.Delta is { } delta and not 0)
                changes.Add(new BalanceChange { Account = holder.Account, Balance = MptAmount(holder.IssuanceId, delta) });
        }

        foreach (KeyValuePair<string, (string Issuer, decimal Delta)> issuance in issuances)
        {
            if (issuance.Value.Delta != 0 && !string.IsNullOrEmpty(issuance.Value.Issuer))
                changes.Add(new BalanceChange { Account = issuance.Value.Issuer, Balance = MptAmount(issuance.Key, -issuance.Value.Delta) });
        }

        return changes;
    }

    private static MptHolderChange ReadMptHolder(NodeInfo node, bool deleted)
    {
        LOMPToken created = node.NewFields as LOMPToken;
        LOMPToken final = node.FinalFields as LOMPToken;
        LOMPToken previous = node.PreviousFields as LOMPToken;

        if (created != null)
        {
            return new MptHolderChange
            {
                Account = created.Account,
                IssuanceId = created.MPTokenIssuanceID,
                Delta = created.MPTAmount ?? 0,
                FinalAmount = created.MPTAmount ?? 0,
            };
        }

        if (final == null)
            return null;

        decimal finalAmount = deleted ? 0 : final.MPTAmount ?? 0;
        decimal? delta;
        if (previous?.MPTAmount is { } before)
            delta = finalAmount - before;
        else if (deleted)
            delta = -(decimal)(final.MPTAmount ?? 0);
        else
            delta = finalAmount == 0 ? 0 : null;

        return new MptHolderChange
        {
            Account = final.Account,
            IssuanceId = final.MPTokenIssuanceID,
            Delta = delta,
            FinalAmount = finalAmount,
        };
    }

    private static Currency MptAmount(string issuanceId, decimal value) => new Currency
    {
        MPTokenIssuanceID = issuanceId,
        Value = value.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Groups per-account balance changes and sums them.
    /// </summary>
    private static Dictionary<string, List<Currency>> GroupByAccount(IEnumerable<BalanceChange> changes)
    {
        return changes
            .GroupBy(node => node.Account)
            .ToDictionary(keySelector: c => c.Key, elementSelector: c => c.Select(v => v.Balance).ToList());
    }

    /// <summary>
    /// Computes balance changes per account from transaction metadata.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This walks every affected node and computes a delta for every account on the payment path,
    /// not only the one the caller has in mind. The amounts it reads are therefore untrusted: it
    /// is enough for a payment to route through an offer in somebody's own token for a value
    /// beyond <see cref="decimal"/> to reach this code, and then
    /// <see cref="AmountOutOfRangeException"/> comes out - see issue #148.
    /// </para>
    /// <para>
    /// That matters most to anything that re-reads history. A monitor catching up over a ledger
    /// range, an indexer or a reconciler meets the same transaction on every pass, so an unguarded
    /// call does not fail once, it stops there permanently. Catch it and decide what an
    /// unrepresentable balance means for you; issue #150 tracks representing it instead.
    /// </para>
    /// </remarks>
    /// <param name="metadata">Transaction metadata including affected nodes.</param>
    /// <returns>Dictionary mapping account addresses to balance changes (XRP string or IssuedCurrencyAmount).</returns>
    /// <exception cref="AmountOutOfRangeException">An amount in the metadata exceeds what <see cref="decimal"/> can hold.</exception>
    public static Dictionary<string, List<Currency>> GetBalanceChanges(ITransactionMetadata metadata)
    {
        var list = new List<BalanceChange>();
        var mptNodes = new List<(NodeInfo Node, bool Deleted)>();

        foreach (var n in metadata.AffectedNodes)
        {
            var node = n.ModifiedNode != null
                ? new NodeInfo()
                {
                    LedgerEntryType = n.ModifiedNode.LedgerEntryType,
                    FinalFields = n.ModifiedNode.FinalFields,
                    PreviousFields = n.ModifiedNode.PreviousFields,
                    LedgerIndex = n.ModifiedNode.LedgerIndex,
                    PreviousTxnID = n.ModifiedNode.PreviousTxnID,
                    PreviousTxnLgrSeq = n.ModifiedNode.PreviousTxnLgrSeq,
                }
                : n.CreatedNode != null
                    ? new NodeInfo
                    {
                        LedgerEntryType = n.CreatedNode.LedgerEntryType,
                        NewFields = n.CreatedNode.NewFields,
                        LedgerIndex = n.CreatedNode.LedgerIndex,
                    }
                    : n.DeletedNode != null
                        ? new NodeInfo
                        {
                            LedgerEntryType = n.DeletedNode.LedgerEntryType,
                            LedgerIndex = n.DeletedNode.LedgerIndex,
                            FinalFields = n.DeletedNode.FinalFields,
                            PreviousFields = n.DeletedNode.PreviousFields,
                        }
                        : null;
            if (node == null)
            {
                continue;
            }

            if (node.LedgerEntryType == LedgerEntryType.AccountRoot)
            {
                if (GetXrpQuantity(node) is { } value)
                {
                    list.Add(value);
                }
            }
            else if (node.LedgerEntryType == LedgerEntryType.RippleState)
            {
                if (GetTrustlineQuantity(node) is { } values)
                {
                    list.AddRange(values);
                }
            }
            else if (node.LedgerEntryType is LedgerEntryType.MPToken or LedgerEntryType.MPTokenIssuance)
            {
                mptNodes.Add((node, n.DeletedNode != null));
            }
        }

        list.AddRange(GetMptQuantities(mptNodes));

        return GroupByAccount(list);
    }
}