using System.Runtime.Serialization;
using System.Text.Json.Serialization;

using Xrpl.Client.Json.Converters;

namespace Xrpl.Models;

[JsonConverter(typeof(EnumMemberValueConverter<StreamType>))]
public enum StreamType
{
    [EnumMember(Value = "book_changes")]
    BookChanges,

    [EnumMember(Value = "consensus")]
    Consensus,

    [EnumMember(Value = "ledger")]
    Ledger,

    [EnumMember(Value = "manifests")]
    Manifests,

    [EnumMember(Value = "peer_status")]
    PeerStatus,

    [EnumMember(Value = "transactions")]
    Transactions,

    [EnumMember(Value = "transactions_proposed")]
    TransactionsProposed,

    [EnumMember(Value = "server")]
    Server,

    [EnumMember(Value = "validations")]
    Validations
}