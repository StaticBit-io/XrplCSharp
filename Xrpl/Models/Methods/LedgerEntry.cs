using System.Text.Json.Serialization;

using System.Collections.Generic;

using Xrpl.Client.Json.Converters;
using Xrpl.Models.Ledger;

//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/ledgerEntry.ts

namespace Xrpl.Models.Methods;

//https://xrpl.org/ledger_entry.html

/// <summary>
/// The `ledger_entry` method returns a single ledger object from the XRP Ledger  in its raw format.<br/>
/// Expects a response in the form of a <see cref="LedgerEntryResponse"/>.
/// </summary>
/// <code>
///  ```ts  const ledgerEntry: LedgerEntryRequest ={
///         command: "ledger_entry",
///         ledger_index: 60102302,
///         index: "7DB0788C020F02780A673DC74757F23823FA3014C1866E72CC4CD8B226CD6EF4"}
/// ```
/// </code>
public class LedgerEntryRequest : BaseLedgerRequest
{
    public LedgerEntryRequest() => Command = "ledger_entry";

    /// <summary>
    /// Only one of the following properties should be defined in a single request.<br/>
    /// org/ledger_entry.<br/>
    /// html.<br/>
    /// Retrieve any type of ledger object by its unique ID.
    /// </summary>
    [JsonPropertyName("index")]
    public string Index { get; set; }

    /// <summary>
    /// Retrieve an AccountRoot object by its address.<br/>
    /// This is roughly equivalent to the an {@link AccountInfoRequest}.
    /// </summary>
    [JsonPropertyName("account_root")]
    public string AccountRoot { get; set; }


    /// <summary>
    /// The ledger entry ID of the SignerList.
    /// </summary>
    [JsonPropertyName("signer_list")]
    public string SignerList { get; set; }

    /// <summary>
    /// Retrieve the Amendments entry, which contains a list of all enabled amendments on the network.<br/>
    /// The Amendments entry. This value must be 7DB0788C020F02780A673DC74757F23823FA3014C1866E72CC4CD8B226CD6EF4
    /// </summary>
    [JsonPropertyName("amendments")]
    public string Amendments { get; set; }

    /// <summary>
    /// Object specifying the RippleState (trust line) object to retrieve.<br/>
    /// The accounts and currency sub-fields are required to uniquely specify the rippleState entry to retrieve.
    /// </summary>
    [JsonPropertyName("ripple_state")]
    public RippleStateQuery RippleState { get; set; }

    /// <summary>
    /// If true, return the requested ledger object's contents as a hex string in the XRP Ledger's binary format.<br/>
    /// Otherwise, return data in JSON format.<br/>
    /// The default is false.
    /// </summary>
    [JsonPropertyName("binary")]
    public bool? Binary { get; set; }

    /// <summary>
    /// (Clio servers only) If set to true and the queried object has been deleted, return its complete data as it was prior to its deletion.<br/>
    /// If set to false or not provided, and the queried object has been deleted, return objectNotFound (current behavior).
    /// </summary>
    [JsonPropertyName("include_deleted")]
    public bool? IncludeDeleted { get; set; }

    /// <summary>
    /// Retrieve an Automated Market Maker (AMM) object from the ledger.
    /// This is similar to amm_info method, but the ledger_entry version returns only the ledger entry as stored.
    /// </summary>
    [JsonPropertyName("amm")]
    public AmmQuery? Amm { get; set; }

    /// <summary>
    /// Object specifying the MPToken object to retrieve.<br/>
    /// The mpt_issuance_id and account sub-fields are required.
    /// </summary>
    [JsonPropertyName("mptoken")]
    public MPTokenQuery? MPToken { get; set; }

    /// <summary>
    /// Retrieve a MPTokenIssuance object from the ledger.
    /// </summary>
    [JsonPropertyName("mpt_issuance")]
    public string? MptIssuance { get; set; }

    /// <summary>
    /// Retrieve a DID object by the account that owns it.
    /// </summary>
    [JsonPropertyName("did")]
    public string? DID { get; set; }

    /// <summary>
    /// Retrieve a PermissionedDomain object by its ID (hash).
    /// </summary>
    [JsonPropertyName("permissioned_domain")]
    public PermissionedDomainQuery? PermissionedDomain { get; set; }

    /// <summary>
    /// The object ID of a Check object to retrieve.
    /// </summary>
    [JsonPropertyName("check")]
    public string? Check { get; set; }

    /// <summary>
    /// The object ID of a Check object to retrieve.
    /// </summary>
    [JsonPropertyName("credential")]
    public CredentialQuery? Credential { get; set; }

    /// <summary>
    /// Specify the DepositPreauth to retrieve.
    /// If a string, must be the ledger entry ID of the DepositPreauth entry, as hexadecimal.
    /// If an object, requires owner sub-field and either authorized or authorize_credentials sub-field.
    /// </summary>
    [JsonPropertyName("deposit_preauth")]
    public DepositPreauthQuery? DepositPreauth { get; set; }

    /// <summary>
    /// The DirectoryNode to retrieve. If a string, must be the object ID of the
    /// directory, as hexadecimal.If an object, requires either `dir_root` o
    /// Owner as a sub-field, plus optionally a `sub_index` sub-field.
    /// </summary>
    [JsonPropertyName("directory")]
    public DirectoryQuery? Directory { get; set; }

    /// <summary>
    /// The Escrow object to retrieve. If a string, must be the object ID of the
    /// escrow, as hexadecimal. If an object, requires owner and seq sub-fields.
    /// </summary>
    [JsonPropertyName("escrow")]
    public EscrowQuery? Escrow { get; set; }

    /// <summary>
    /// The Offer object to retrieve. If a string, interpret as the unique object
    /// ID to the Offer.If an object, requires the sub-fields `account` and `seq`
    /// to uniquely identify the offer.
    /// </summary>
    [JsonPropertyName("offer")]
    public OfferQuery? Offer { get; set; }

    /// <summary>
    ///  The Ticket object to retrieve. If a string, must be the object ID of the
    ///  Ticket, as hexadecimal.If an object, the `owner` and `ticket_sequence`
    ///  sub-fields are required to uniquely specify the Ticket entry.
    /// </summary>
    [JsonPropertyName("ticket")]
    public TicketQuery? Ticket { get; set; }

    /// <summary>
    ///  The object ID of a PayChannel object to retrieve.
    /// </summary>
    [JsonPropertyName("payment_channel")]
    public string? PaymentChannel { get; set; }

    /// <summary>
    /// Must be the object ID of the NFToken page, as hexadecimal
    /// </summary>
    [JsonPropertyName("nft_page")]
    public string? NftPage { get; set; }

    /// <summary>
    /// The ledger entry ID of an NFT offer to retrieve.
    /// </summary>
    [JsonPropertyName("nft_offer")]
    public string? NftOffer { get; set; }

    /// <summary>
    /// 
    /// </summary>
    [JsonPropertyName("bridge_account")]
    public string? BridgeAccount { get; set; }

    /// <summary>
    /// 
    /// </summary>
    [JsonPropertyName("delegate")]
    public DelegateQuery? Delegate { get; set; }

    /// <summary>
    /// Specify the Loan to retrieve.
    /// If a string, must be the ledger entry ID of the Loan, as hexadecimal.
    /// If an object, requires loan_broker_id and loan_seq sub-fields.
    /// </summary>
    [JsonPropertyName("loan")]
    public LoanQuery? Loan { get; set; }
    /// <summary>
    /// Specify the LoanBroker to retrieve.
    /// If a string, must be the ledger entry ID of the LoanBroker, as hexadecimal.
    /// If an object, requires owner and seq sub-fields.
    /// </summary>
    [JsonPropertyName("loan_broker")]
    public LoanBrokerQuery? LoanBroker { get; set; }
    /// <summary>
    /// The oracle identifier.
    /// </summary>
    [JsonPropertyName("oracle")]
    public OracleQuery? Oracle { get; set; }
}

/// <summary>
/// Retrieve an Automated Market Maker (AMM) object from the ledger.
/// This is similar to amm_info method, but the ledger_entry version returns only the ledger entry as stored.
/// </summary>
public class AmmQuery
{
    /// <summary>
    /// Specifies one of the pool assets (XRP or token) of the AMM instance.<br/>
    /// Both asset and asset2 must be defined to specify an AMM instance.
    /// </summary>
    [JsonPropertyName("asset")]
    [JsonConverter(typeof(IssuedCurrencyConverter))]
    public Common.Common.IssuedCurrency Asset { get; set; }

    /// <summary>
    /// Specifies the other pool asset of the AMM instance.<br/>
    /// Both asset and asset2 must be defined to specify an AMM instance.
    /// </summary>
    [JsonPropertyName("asset2")]
    [JsonConverter(typeof(IssuedCurrencyConverter))]
    public Common.Common.IssuedCurrency Asset2 { get; set; }
}

/// <summary>
/// The oracle identifier.
/// </summary>
public class OracleQuery
{
    /// <summary>
    /// A unique identifier of the price oracle for the Account
    /// </summary>
    [JsonPropertyName("oracle_document_id")]
    public uint OracleDocumentId { get; set; }

    /// <summary>
    /// The account that controls the Oracle object.
    /// </summary>
    [JsonPropertyName("account")]
    public string Account { get; set; }
}

/// <summary>
/// Specify the PermissionedDomain to retrieve.
/// If a string, must be the ledger entry ID of the entry, as hexadecimal.
/// If an object, requires account and seq sub-fields.
/// </summary>
public class PermissionedDomainQuery
{
    /// <summary>
    /// The sequence number of the transaction that created the PermissionedDomain.
    /// </summary>
    [JsonPropertyName("seq")]
    public uint Seq { get; set; }

    /// <summary>
    /// The account that owns the PermissionedDomain.
    /// </summary>
    [JsonPropertyName("account")]
    public string Account { get; set; }
}

/// <summary>
/// Specify the LoanBroker to retrieve.
/// If a string, must be the ledger entry ID of the LoanBroker, as hexadecimal.
/// If an object, requires owner and seq sub-fields.
/// </summary>
public class LoanBrokerQuery
{
    /// <summary>
    /// The Sequence Number of the transaction that created the LoanBroker.
    /// </summary>
    [JsonPropertyName("seq")]
    public uint? Seq { get; set; }

    /// <summary>
    /// The account that controls the LoanBroker.
    /// </summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }
}

/// <summary>
/// Specify the Loan to retrieve.
/// If a string, must be the ledger entry ID of the Loan, as hexadecimal.
/// If an object, requires loan_broker_id and loan_seq sub-fields.
/// </summary>
public class LoanQuery
{
    /// <summary>
    /// The sequence number of the loan.
    /// </summary>
    [JsonPropertyName("loan_seq")]
    public uint? LoanSeq { get; set; }

    /// <summary>
    /// The ledger entry ID of the LoanBroker that created the loan, as hexadecimal.
    /// </summary>
    [JsonPropertyName("loan_broker_id")]
    public string? LoanBrokerId { get; set; }
}

/// <summary>
///  The Ticket object to retrieve. If a string, must be the object ID of the
///  Ticket, as hexadecimal.If an object, the `owner` and `ticket_sequence`
///  sub-fields are required to uniquely specify the Ticket entry.
/// </summary>
public class TicketQuery
{
    /// <summary>
    /// The Ticket Sequence number of the Ticket entry to retrieve.
    /// </summary>
    [JsonPropertyName("ticket_seq")]
    public uint TicketSequence { get; set; }

    /// <summary>
    /// The owner of the Ticket object.
    /// </summary>
    [JsonPropertyName("account")]
    public string Account { get; set; }
}

/// <summary>
/// The Escrow object to retrieve. If a string, must be the object ID of the
/// escrow, as hexadecimal. If an object, requires owner and seq sub-fields.
/// </summary>
public class OfferQuery
{
    /// <summary>
    /// Sequence Number of the transaction that created the Offer object.
    /// </summary>
    [JsonPropertyName("seq")]
    public uint Seq { get; set; }

    /// <summary>
    /// The account that placed the offer.
    /// </summary>
    [JsonPropertyName("account")]
    public string Account { get; set; }
}

/// <summary>
/// The Escrow object to retrieve. If a string, must be the object ID of the
/// escrow, as hexadecimal. If an object, requires owner and seq sub-fields.
/// </summary>
public class EscrowQuery
{
    /// <summary>
    /// Sequence Number of the transaction that created the Escrow object.
    /// </summary>
    [JsonPropertyName("seq")]
    public uint Seq { get; set; }

    /// <summary>
    /// The owner (sender) of the Escrow object.
    /// </summary>
    [JsonPropertyName("owner")]
    public string Owner { get; set; }
}

/// <summary>
/// The DirectoryNode to retrieve. If a string, must be the object ID of the
/// directory, as hexadecimal.If an object, requires either `dir_root` o
/// Owner as a sub-field, plus optionally a `sub_index` sub-field.
/// </summary>
public class DirectoryQuery
{
    /// <summary>
    /// If provided, jumps to a later "page" of the DirectoryNode.
    /// </summary>
    [JsonPropertyName("sub_index")]
    public uint? SubIndex { get; set; }

    /// <summary>
    /// Unique index identifying the directory to retrieve, as a hex string.
    /// </summary>
    [JsonPropertyName("dir_root")]
    public string? DirRoot { get; set; }

    /// <summary>
    /// Unique address of the account associated with this directory.
    /// </summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }
}

/// <summary>
/// Specify the DepositPreauth to retrieve.
/// If a string, must be the ledger entry ID of the DepositPreauth entry, as hexadecimal.
/// If an object, requires owner sub-field and either authorized or authorize_credentials sub-field.
/// </summary>
public class DepositPreauthQuery
{
    /// <summary>
    /// The account that provided the preauthorization.
    /// </summary>
    [JsonPropertyName("owner")]
    public string Owner { get; set; }

    /// <summary>
    /// The account that received the preauthorization.
    /// </summary>
    [JsonPropertyName("authorized")]
    public string? Authorized { get; set; }

    /// <summary>
    /// A set of credentials that received the preauthorization.
    /// </summary>
    [JsonPropertyName("authorized_credentials")]
    public List<AuthorizedCredential>? AuthorizedCredentials { get; set; }
}

public class AuthorizedCredential
{
    /// <summary>
    /// The address of the account that issued the credential.
    /// </summary>
    [JsonPropertyName("issuer")]
    public string Issuer { get; set; }

    /// <summary>
    /// The type of the credential, as issued.
    /// </summary>
    [JsonPropertyName("credential_type")]
    public string CredentialType { get; set; }
}

public class DelegateQuery
{
    /// <summary>
    /// The account that provided the preauthorization.
    /// </summary>
    [JsonPropertyName("account")]
    public string Account { get; set; }

    /// <summary>
    /// The account that received the preauthorization.
    /// </summary>
    [JsonPropertyName("authorize")]
    public string Authorize { get; set; }
}

/// <summary>
/// Specify the Credential to retrieve. If a string, must be the ledger entry ID of
/// the entry, as hexadecimal.If an object, requires subject, issuer, and
/// credential_type sub-fields.
/// </summary>
public class CredentialQuery
{
    /// <summary>
    /// The account that is the subject of the credential.
    /// </summary>
    [JsonPropertyName("subject")]
    public string Subject { get; set; }

    /// <summary>
    /// The account that issued the credential.
    /// </summary>
    [JsonPropertyName("issuer")]
    public string Issuer { get; set; }

    /// <summary>
    /// The type of the credential, as issued.
    /// </summary>
    [JsonPropertyName("credentialType")]
    public string CredentialType { get; set; }
}

/// <summary>
/// Object specifying the MPToken object to retrieve.<br/>
/// The mpt_issuance_id and account sub-fields are required.
/// </summary>
public class MPTokenQuery
{
    /// <summary>
    /// The MPTokenIssuanceID of the MPT.
    /// </summary>
    [JsonPropertyName("mpt_issuance_id")]
    public string MPTokenIssuanceID { get; set; }

    /// <summary>
    /// The account address of the MPT holder.
    /// </summary>
    [JsonPropertyName("account")]
    public string Account { get; set; }
}

/// <summary>
/// Object specifying the RippleState (trust line) object to retrieve.<br/>
/// The accounts and currency sub-fields are required to uniquely specify the rippleState entry to retrieve.
/// </summary>
public class RippleStateQuery
{
    /// <summary>
    /// 2-length array of account Addresses, defining the two accounts linked by  this RippleState object.
    /// </summary>
    [JsonPropertyName("accounts")]
    public string[] Addresses { get; set; }

    /// <summary>
    /// Currency Code of the RippleState object to retrieve.
    /// </summary>
    [JsonPropertyName("currency")]
    public string Currency { get; set; }
}