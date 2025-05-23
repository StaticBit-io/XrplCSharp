using System.Collections.Generic;
using System.Linq;

using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger; // Contains Balance, IssuedCurrencyAmount
using Xrpl.Models.Transactions;
// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/test/utils/getBalanceChanges.ts

namespace XrplTests.Xrpl.Utils;

/// <summary>
/// Utilities for computing balance changes (XRP and issued currencies)
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
        var newField = node.New is LOAccountRoot { } newNode ? newNode : null;
        var finalField = node.Final is LOAccountRoot { } finalNode ? finalNode : null;
        var previousField = node.Previous is LOAccountRoot { } previousNode ? previousNode : null;

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
        var newField = node.New is LORippleState { } newNode ? newNode : null;
        var finalField = node.Final is LORippleState { } finalNode ? finalNode : null;
        var previousField = node.Previous is LORippleState { } previousNode ? previousNode : null;

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
    /// <param name="metadata">Transaction metadata including affected nodes.</param>
    /// <returns>Dictionary mapping account addresses to balance changes (XRP string or IssuedCurrencyAmount).</returns>
    public static Dictionary<string, List<Currency>> GetBalanceChanges(ITransactionMetadata metadata)
    {
        var list = new List<BalanceChange>();

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
        }

        return GroupByAccount(list);
    }
}