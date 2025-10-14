using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

using System.Runtime.Serialization;

namespace Xrpl.Models.Enums;

/// <summary>
/// LedgerEntryFilter is used to filter the types of ledger entries returned by certain API calls (account_objects request).
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum LedgerEntryFilter
{
    [EnumMember(Value = "account")]
    Account,

    [EnumMember(Value = "amendments")]
    Amendments,

    [EnumMember(Value = "amm")]
    Amm,

    [EnumMember(Value = "bridge")]
    Bridge,

    [EnumMember(Value = "check")]
    Check,

    [EnumMember(Value = "credential")]
    Credential,

    [EnumMember(Value = "delegate")]
    Delegate,

    [EnumMember(Value = "deposit_preauth")]
    DepositPreauth,

    [EnumMember(Value = "did")]
    Did,

    [EnumMember(Value = "directory")]
    Directory,

    [EnumMember(Value = "escrow")]
    Escrow,

    [EnumMember(Value = "fee")]
    Fee,

    [EnumMember(Value = "hashes")]
    Hashes,

    [EnumMember(Value = "mpt_issuance")]
    MptIssuance,

    [EnumMember(Value = "mptoken")]
    Mptoken,

    [EnumMember(Value = "nft_offer")]
    NftOffer,

    [EnumMember(Value = "nft_page")]
    NftPage,

    [EnumMember(Value = "offer")]
    Offer,

    [EnumMember(Value = "oracle")]
    Oracle,

    [EnumMember(Value = "payment_channel")]
    PaymentChannel,

    [EnumMember(Value = "permissioned_domain")]
    PermissionedDomain,

    [EnumMember(Value = "signer_list")]
    SignerList,

    [EnumMember(Value = "state")]
    State,

    [EnumMember(Value = "ticket")]
    Ticket,

    [EnumMember(Value = "vault")]
    Vault,

    [EnumMember(Value = "xchain_owned_create_account_claim_id")]
    XChainOwnedCreateAccountClaimId,

    [EnumMember(Value = "xchain_owned_claim_id")]
    XChainOwnedClaimId
}
