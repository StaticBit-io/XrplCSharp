namespace Xrpl.Models;

/// <summary>
/// Each ledger version's state data is a set of ledger objects, sometimes called ledger entries,
/// which collectively represent all settings, balances, and relationships at a given point in time.<br/>
/// To store or retrieve an object in the state data, the protocol uses that object's unique Ledger Object ID.<br/>
/// In the peer protocol, ledger objects have a canonical binary format.In rippled APIs, ledger objects are represented as JSON objects.
/// </summary>
public enum LedgerEntryType
{
    /// <summary>
    /// The settings, XRP balance, and other metadata for one account.
    /// </summary>
    AccountRoot,

    /// <summary>
    /// The status of enabled and pending amendments.
    /// </summary>
    Amendments,

    /// <summary>
    /// The definition and details of an Automated Market Maker (AMM) instance.
    /// </summary>
    AMM,

    /// <summary>
    /// A single cross-chain bridge that connects and enables value to move efficiently between two blockchains.
    /// </summary>
    Bridge,

    /// <summary>
    /// A check that can be redeemed for money by its destination.
    /// </summary>
    Check,

    /// <summary>
    /// A credential, which can be used to preauthorize payments or gain access to specific permissioned domains.
    /// </summary>
    Credential,

    /// <summary>
    /// A record of which permissions have been granted to another account.
    /// </summary>
    Delegate,

    /// <summary>
    /// A record of preauthorization for sending payments to an account that requires authorization.
    /// </summary>
    DepositPreauth,

    /// <summary>
    /// A Decentralized Identifier (DID).
    /// </summary>
    DID,

    /// <summary>
    /// A set of links to other ledger entries, either objects owned by an account or trades in the decentralized exchange.
    /// </summary>
    DirectoryNode,

    /// <summary>
    /// An escrow, which holds funds to be released when certain conditions are met.
    /// </summary>
    Escrow,

    /// <summary>
    /// The current base transaction cost and reserve requirements.
    /// </summary>
    FeeSettings,

    /// <summary>
    /// Lists of prior ledger versions' hashes for history lookup.
    /// </summary>
    LedgerHashes,

    /// <summary>
    /// Multi-Purpose Tokens (MPT) of one issuance held by a specific account.
    /// </summary>
    MPToken,

    /// <summary>
    /// Definition of a Multi-Purpose Token (MPT) issuance.
    /// </summary>
    MPTokenIssuance,

    /// <summary>
    /// List of validators currently believed to be offline.
    /// </summary>
    NegativeUNL,

    /// <summary>
    /// An offer to buy or sell an NFT.
    /// </summary>
    NFTokenOffer,

    /// <summary>
    /// A group of up to 32 NFTs, stored together for efficiency.
    /// </summary>
    NFTokenPage,

    /// <summary>
    /// An offer (order) to trade currencies in the decentralized exchange.
    /// </summary>
    Offer,

    /// <summary>
    /// A record of price information about currency pairs from an outside source.
    /// </summary>
    Oracle,

    /// <summary>
    /// A payment channel, which allows for rapid, asynchronous payments.
    /// </summary>
    PayChannel,

    /// <summary>
    /// A permissioned domain, which is used to limit access to other features.
    /// </summary>
    PermissionedDomain,

    /// <summary>
    /// A trust line, which tracks the net balance of fungible tokens between two accounts.
    /// </summary>
    RippleState,

    /// <summary>
    /// A list of addresses for multi-signing transactions.
    /// </summary>
    SignerList,

    /// <summary>
    /// A ticket, which sets aside a sequence number for use in a future transaction.
    /// </summary>
    Ticket,

    /// <summary>
    /// Vault object (XLS-xx).
    /// </summary>
    Vault,

    /// <summary>
    /// A cross-chain transfer of value.
    /// </summary>
    XChainOwnedClaimID,

    /// <summary>
    /// A record of attestations for creating an account via a cross-chain transfer.
    /// </summary>
    XChainOwnedCreateAccountClaimID,

    /// <summary>
    /// Unknown ledger entry type (for forward compatibility with future ledger objects).
    /// </summary>
    Unknown,
}