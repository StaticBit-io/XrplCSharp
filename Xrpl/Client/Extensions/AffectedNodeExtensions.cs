using System.Collections.Generic;
using System.Linq;

using Xrpl.Models.Ledger;
using Xrpl.Models.Transactions;

namespace Xrpl.Client.Extensions;

public static class AffectedNodeExtensions
{
    public static IEnumerable<T> GetModifiedFinals<T>(this IEnumerable<AffectedNode> nodes)
        where T : BaseLedgerEntry
    {
        return nodes
            .Select(x => x.ModifiedNode)
            .Where(x => x != null)
            .Select(x => x!)
            .Select(x => x.FinalFields)
            .OfType<T>();
    }
    public static IEnumerable<T> GetModifiedPrevious<T>(this IEnumerable<AffectedNode> nodes)
        where T : BaseLedgerEntry
    {
        return nodes
            .Select(x => x.ModifiedNode)
            .Where(x => x != null)
            .Select(x => x!)
            .Select(x => x.PreviousFields)
            .OfType<T>();
    }
    public static IEnumerable<T> GetDeletedFinals<T>(this IEnumerable<AffectedNode> nodes)
        where T : BaseLedgerEntry
    {
        return nodes
            .Select(x => x.DeletedNode)
            .Where(x => x != null)
            .Select(x => x!)
            .Select(x => x.FinalFields)
            .OfType<T>();
    }
    public static IEnumerable<T> GetDeletedPrevious<T>(this IEnumerable<AffectedNode> nodes)
        where T : BaseLedgerEntry
    {
        return nodes
            .Select(x => x.DeletedNode)
            .Where(x => x != null)
            .Select(x => x!)
            .Select(x => x.PreviousFields)
            .OfType<T>();
    }
    public static IEnumerable<T> GetCreatedNews<T>(this IEnumerable<AffectedNode> nodes)
        where T : BaseLedgerEntry
    {
        return nodes
            .Select(x => x.CreatedNode)
            .Where(x => x != null)
            .Select(x => x!)
            .Select(x => x.NewFields)
            .OfType<T>();
    }

    public static IEnumerable<T> GetModifiedFinals<T>(this ITransactionMetadata meta)
        where T : BaseLedgerEntry =>
        meta.AffectedNodes.GetModifiedFinals<T>();

    public static IEnumerable<T> GetModifiedPrevious<T>(this ITransactionMetadata meta)
        where T : BaseLedgerEntry =>
        meta.AffectedNodes.GetModifiedPrevious<T>();

    public static IEnumerable<T> GetDeletedFinals<T>(this ITransactionMetadata meta)
        where T : BaseLedgerEntry =>
        meta.AffectedNodes.GetDeletedFinals<T>();

    public static IEnumerable<T> GetDeletedPrevious<T>(this ITransactionMetadata meta)
        where T : BaseLedgerEntry =>
        meta.AffectedNodes.GetDeletedPrevious<T>();

    public static IEnumerable<T> GetCreatedNews<T>(this ITransactionMetadata meta)
        where T : BaseLedgerEntry =>
        meta.AffectedNodes.GetCreatedNews<T>();
}