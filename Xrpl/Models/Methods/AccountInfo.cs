using System.Text.Json.Serialization;

using System.Collections.Generic;

using Xrpl.Models.Ledger;

//https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/accountInfo.ts

namespace Xrpl.Models.Methods
{
    /// <summary>
    /// Response expected from an <see cref="AccountInfoRequest"/>.
    /// </summary>
    public class AccountInfo //todo rename to AccountInfoResponse
    {
        /// <summary>
        /// The AccountRoot ledger object with this account's information, as stored in the ledger.
        /// </summary>
        [JsonPropertyName("account_data")]
        public LOAccountRoot AccountData { get; set; }
        /// <summary>
        /// A map of account flags parsed out.  This will only be available for rippled nodes 1.11.0 and higher.
        /// </summary>
        [JsonPropertyName("account_flags")]
        public AccountInfoAccountFlags AccountFlags { get; set; }
        /// <summary>
        /// If requested, array of SignerList ledger objects associated with this account for Multi-Signing.
        ///Since an account can own at most one SignerList, this array must have exactly one
        /// member if it is present.
        /// </summary>
        [JsonPropertyName("signer_lists")]
        public LOSignerList[]? SignerLists { get; set; }
        /// <summary>
        /// The ledger index of the current in-progress ledger, which was used when retrieving this information.
        /// </summary>
        [JsonPropertyName("ledger_current_index")]
        public int LedgerCurrentIndex { get; set; }
        /// <summary>
        /// The ledger index of the ledger version used when retrieving this
        ///information.The information does not contain any changes from ledger
        /// versions newer than this one.
        /// </summary>
        [JsonPropertyName("ledger_index")]
        public int LedgerIndex { get; set; }
        /// <summary>
        /// Information about queued transactions sent by this account.<br/>
        /// This information describes the state of the local rippled server, which may be different from other servers in the peer-to-peer XRP Ledger network.<br/>
        /// Some fields may be omitted because the values are calculated "lazily" by the queuing mechanism.
        /// </summary>
        [JsonPropertyName("queue_data")]
        public AccountQueueData? QueueData { get; set; }

        /// <summary>
        /// Set or get the pseudo account type of this account. This field is only present if the account is a pseudo account, and is null otherwise.<br/>
        /// </summary>
        [JsonPropertyName("pseudo_account")]
        public PseudoAccountInfo? PseudoAccount { get; set; }
        /// <summary>
        /// True if this data is from a validated ledger version;<br/>
        /// if omitted or set to false, this data is not final.
        /// </summary>
        [JsonPropertyName("validated")]
        public bool Validated { get; set; }
    }


    public class PseudoAccountInfo
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }
    }

    //https://github.com/XRPLF/xrpl.js/blob/b20c05c3680d80344006d20c44b4ae1c3b0ffcac/packages/xrpl/src/models/methods/accountInfo.ts#L42
    /// <summary>
    /// Information about each queued transaction from address.
    /// </summary>
    public class AccountQueueTransaction 
    {
        /// <summary>
        /// Whether this transaction changes this address's ways of authorizing transactions.
        /// </summary>
        [JsonPropertyName("auth_change")]
        public bool AuthChange { get; set; }
        /// <summary>
        /// The Transaction Cost of this transaction, in drops of XRP.
        /// </summary>
        [JsonPropertyName("fee")]
        public string Fee { get; set; }
        /// <summary>
        /// The transaction cost of this transaction, relative to the minimum cost for this type of transaction, in fee levels.
        /// </summary>
        [JsonPropertyName("fee_level")]
        public string FeeLevel { get; set; }
        /// <summary>
        /// The maximum amount of XRP, in drops, this transaction could send or destroy. 
        /// </summary>
        [JsonPropertyName("max_spend_drops")]
        public string MaxSpendDrops { get; set; }
        /// <summary>
        /// The Sequence Number of this transaction.
        /// </summary>
        [JsonPropertyName("seq")]
        public int Sequence { get; set; }
    }
    public sealed class AccountInfoAccountFlags
    {
        /// <summary>
        /// Enable rippling on this address's trust lines by default. Required for issuing addresses; discouraged for others.
        /// </summary>
        [JsonPropertyName("defaultRipple")]
        public bool DefaultRipple { get; set; }

        /// <summary>
        /// This account can only receive funds from transactions it sends, and from preauthorized accounts. (DepositAuth enabled.)
        /// </summary>
        [JsonPropertyName("depositAuth")]
        public bool DepositAuth { get; set; }

        /// <summary>
        /// Disallows use of the master key to sign transactions for this account.
        /// </summary>
        [JsonPropertyName("disableMasterKey")]
        public bool DisableMasterKey { get; set; }

        /// <summary>
        /// Disallow incoming Checks from other accounts. (DisallowIncoming amendment)
        /// </summary>
        [JsonPropertyName("disallowIncomingCheck")]
        public bool? DisallowIncomingCheck { get; set; }

        /// <summary>
        /// Disallow incoming NFTOffers from other accounts. (DisallowIncoming amendment)
        /// </summary>
        [JsonPropertyName("disallowIncomingNFTokenOffer")]
        public bool? DisallowIncomingNFTokenOffer { get; set; }

        /// <summary>
        /// Disallow incoming PayChannels from other accounts. (DisallowIncoming amendment)
        /// </summary>
        [JsonPropertyName("disallowIncomingPayChan")]
        public bool? DisallowIncomingPayChan { get; set; }

        /// <summary>
        /// Disallow incoming Trustlines from other accounts. (DisallowIncoming amendment)
        /// </summary>
        [JsonPropertyName("disallowIncomingTrustline")]
        public bool? DisallowIncomingTrustline { get; set; }

        /// <summary>
        /// Client applications should not send XRP to this account. Not enforced by rippled.
        /// </summary>
        [JsonPropertyName("disallowIncomingXRP")]
        public bool DisallowIncomingXRP { get; set; }

        /// <summary>
        /// All assets issued by this address are frozen.
        /// </summary>
        [JsonPropertyName("globalFreeze")]
        public bool GlobalFreeze { get; set; }

        /// <summary>
        /// This address cannot freeze trust lines connected to it. Once enabled, cannot be disabled.
        /// </summary>
        [JsonPropertyName("noFreeze")]
        public bool NoFreeze { get; set; }

        /// <summary>
        /// The account has used its free SetRegularKey transaction.
        /// </summary>
        [JsonPropertyName("passwordSpent")]
        public bool PasswordSpent { get; set; }

        /// <summary>
        /// This account must individually approve other users for those users to hold this account's issued currencies.
        /// </summary>
        [JsonPropertyName("requireAuthorization")]
        public bool RequireAuthorization { get; set; }

        /// <summary>
        /// Requires incoming payments to specify a Destination Tag.
        /// </summary>
        [JsonPropertyName("requireDestinationTag")]
        public bool RequireDestinationTag { get; set; }

        /// <summary>
        /// This address can claw back issued IOUs. Once enabled, cannot be disabled.
        /// </summary>
        [JsonPropertyName("allowTrustLineClawback")]
        public bool AllowTrustLineClawback { get; set; }

        [JsonPropertyName("allowTrustLineLocking")]
        public bool AllowTrustLineLocking { get; set; }
    }
    /// <summary>
    /// Information about queued transactions sent by account.<br/>
    /// This information describes the state of the local rippled server, which may be different from other servers in the peer-to-peer XRP Ledger network.<br/>
    /// Some fields may be omitted because the values are calculated "lazily" by the queuing mechanism.
    /// </summary>
    public class AccountQueueData
    {
        /// <summary>
        /// Whether a transaction in the queue changes this address's ways of authorizing transactions.
        /// If true, this address can queue no further transactions until that transaction has been executed or dropped from the queue.
        /// </summary>
        [JsonPropertyName("auth_change_queued")]
        public bool? AuthChangeQueued { get; set; }
        /// <summary>
        /// The highest Sequence Number among transactions queued by this address.
        /// </summary>
        [JsonPropertyName("highest_sequence")]
        public int? HighestSequence { get; set; }
        /// <summary>
        /// The lowest Sequence Number among transactions queued by this address. 
        /// </summary>
        [JsonPropertyName("lowest_sequence")]
        public int? LowestSequence { get; set; }
        /// <summary>
        /// Integer amount of drops of XRP that could be debited from this address
        /// if every transaction in the queue consumes the maximum amount of XRP possible.
        /// </summary>
        [JsonPropertyName("max_spend_drops_total")]
        public string MaxSpendDropsTotal { get; set; }
        /// <summary>
        /// Information about each queued transaction from this address.
        /// </summary>
        [JsonPropertyName("transactions")]
        public List<AccountQueueTransaction> Transactions { get; set; }
        /// <summary>
        /// Number of queued transactions from this address.
        /// </summary>
        [JsonPropertyName("txn_count")]
        public int TxnCount { get; set; }
    }
    /// <summary>
    /// The `account_info` command retrieves information about an account, its activity, and its XRP balance.<br/>
    /// All information retrieved is relative to a particular version of the ledger. Returns an <see cref="AccountInfo"/>.
    /// </summary>
    /// <code>
    /// {
    /// 	"id": 2,
    /// 	"command": "account_info",
    /// 	"account": "rG1QQv2nh2gr7RCZ1P8YYcBUKCCN633jCn",
    /// 	"strict": true,
    /// 	"ledger_index": "current",
    /// 	"queue": true
    /// }
    /// </code>
    public class AccountInfoRequest : BaseLedgerRequest
    {
        public AccountInfoRequest(string account)
        {
            Account = account;
            Command = "account_info";
        }
        /// <summary>
        ///  A unique identifier for the account, most commonly the account's address.
        /// </summary>
        [JsonPropertyName("account")]
        public string Account { get; set; }
        /// <summary>
        /// If true, then the account field only accepts a public key or XRP Ledger address.<br/>
        /// Otherwise, account can be a secret or passphrase (not recommended).
        /// The default is false.
        /// </summary>
        [JsonPropertyName("strict")]
        public bool? Strict { get; set; }
        /// <summary>
        /// Whether to get info about this account's queued transactions.<br/>
        /// Can only be used when querying for the data from the current open ledger.<br/>
        /// Not available from servers in Reporting Mode.
        /// </summary>
        [JsonPropertyName("queue")]
        public bool? Queue { get; set; }
        /// <summary>
        /// Request SignerList objects associated with this account.
        /// </summary>
        [JsonPropertyName("signer_lists")]
        public bool? SignerLists { get; set; }
    }
}
