// https://xrpl.org/docs/references/http-websocket-apis/public-api-methods/path-and-order-book-methods/book_changes
// https://github.com/XRPLF/rippled/blob/develop/src/xrpld/rpc/BookChanges.h

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Subscriptions;

namespace Xrpl.Models.Methods;

/// <summary>
/// The <c>book_changes</c> method reports the changes to the order books of the DEX in one ledger
/// version: per order book, the volume traded and the high, low, open and close rates.
/// Returns a <see cref="BookChangesResponse"/>.
/// </summary>
/// <remarks>
/// The answer is the same summary the <c>book_changes</c> subscription stream sends for each validated
/// ledger (<see cref="BookChangesStream"/>), asked for one ledger chosen by
/// <see cref="BaseLedgerRequest.LedgerIndex"/> or <see cref="BaseLedgerRequest.LedgerHash"/>.
/// </remarks>
public class BookChangesRequest : BaseLedgerRequest
{
    /// <summary>
    /// Creates a request for the ledger set through <see cref="BaseLedgerRequest.LedgerIndex"/> /
    /// <see cref="BaseLedgerRequest.LedgerHash"/>. When neither is set, rippled answers for the open
    /// ledger (<c>current</c>), whose changes are not final; set <c>validated</c> or an index for final data.
    /// </summary>
    public BookChangesRequest() { Command = "book_changes"; }
}

/// <summary>
/// Response expected from a <see cref="BookChangesRequest"/>.
/// </summary>
public class BookChangesResponse : BaseMethodResult
{
    /// <summary>
    /// Always <c>bookChanges</c>.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; }

    /// <summary>
    /// The ledger index of the ledger the changes are from.
    /// </summary>
    [JsonPropertyName("ledger_index")]
    public uint LedgerIndex { get; set; }

    /// <summary>
    /// The hash of the ledger the changes are from.
    /// </summary>
    [JsonPropertyName("ledger_hash")]
    public string LedgerHash { get; set; }

    /// <summary>
    /// The close time of the ledger.
    /// </summary>
    [JsonPropertyName("ledger_time")]
    [JsonConverter(typeof(RippleDateTimeConverter))]
    public DateTime? LedgerTime { get; set; }

    /// <summary>
    /// True if the ledger is validated.
    /// </summary>
    [JsonPropertyName("validated")]
    public bool? Validated { get; set; }

    private List<BookChange> _changes = new List<BookChange>();

    /// <summary>
    /// One entry per order book that offers in the ledger traded against; empty when none did.
    /// </summary>
    [JsonPropertyName("changes")]
    public List<BookChange> Changes
    {
        get => _changes;
        set => _changes = value ?? new List<BookChange>();
    }
}
