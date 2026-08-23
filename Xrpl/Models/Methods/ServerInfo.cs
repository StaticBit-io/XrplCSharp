using System;
using System.Runtime.Serialization;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Xrpl.Client.Json.Converters;
// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/models/methods/serverInfo.ts

namespace Xrpl.Models.Methods
{
    /// <summary>
    /// The `server_info` command asks the server for a human-readable version of  various information about the rippled server being queried.<br/>
    /// Expects a  response in the form of a {@link ServerInfoResponse}.
    /// </summary>
    public class ServerInfoRequest : BaseRequest
    {
        public ServerInfoRequest()
        {
            Command = "server_info";
        }
    }

    /// <summary>
    /// Depending on how the rippled server is configured, how long it has been running, and other factors,
    /// a server may be participating in the global XRP Ledger peer-to-peer network to different degrees.<br/>
    /// This is represented as the server_state field in the responses to the server_info method and server_state method.<br/>
    /// The possible responses follow a range of ascending interaction, with each later value superseding the previous one.<br/>
    /// <a>https://xrpl.org/rippled-server-states.html</a>
    /// </summary>
    public enum ServerStateInner
    {
        /// <summary>
        /// The server is not connected to the XRP Ledger peer-to-peer network whatsoever.<br/>
        /// It may be running in offline mode, or it may not be able to access the network for whatever reason.
        /// </summary>
        [EnumMember(Value = "disconnected")]
        Disconnected,
        /// <summary>
        /// The server believes it is connected to the network.
        /// </summary>
        [EnumMember(Value = "connected")]
        Connected,
        /// <summary>
        /// The server is currently behind on ledger versions.<br/>
        /// (It is normal for a server to spend a few minutes catching up after you start it).
        /// </summary>
        [EnumMember(Value = "syncing")]
        Syncing,
        /// <summary>
        /// The server is in agreement with the network.
        /// </summary>
        [EnumMember(Value = "tracking")]
        Tracking,
        /// <summary>
        /// The server is fully caught-up with the network and could participate in validation,
        /// but is not doing so (possibly because it has not been configured as a validator).
        /// </summary>
        [EnumMember(Value = "full")]
        Full,
        /// <summary>
        /// The server is currently participating in validation of the ledger.
        /// </summary>
        [EnumMember(Value = "validating")]
        Validating,
        /// <summary>
        /// The server is participating in validation of the ledger and currently proposing its own version.
        /// </summary>
        [EnumMember(Value = "proposing")]
        Proposing
    }
    
    public class ServerInfo : BaseMethodResult//todo rename to ServerInfoResponse extends BaseResponse 
    {
        [JsonPropertyName("info")]
        public Info Info { get; set; }
    }

    public class Info : BaseMethodResult
    {
        /// <summary>
        /// The version number of the running rippled version.
        /// </summary>
        [JsonPropertyName("build_version")]
        public string BuildVersion { get; set; }

        /// <summary>
        /// he NetworkID field is a protection against "cross-chain" transaction replay attacks,<br/>
        /// preventing the same transaction from being copied over<br/>
        /// and executing on a parallel network that it wasn't intended for.<br/>
        /// For compatibility with existing chains, the NetworkID field<br/>
        /// must be omitted on any network with a Network ID of 1024 or less,<br/>
        /// but must be included on any network with a Network ID of 1025 or greater.
        /// </summary>
        [JsonPropertyName("network_id")]
        public uint? NetworkID { get; set; }

        /// <summary>
        /// Range expression indicating the sequence numbers of the ledger versions the local rippled has in its database.
        /// </summary>
        [JsonPropertyName("complete_ledgers")]
        public string CompleteLedgers { get; set; }

        /// <summary>
        /// On an admin request, returns the hostname of the server running the rippled instance;<br/>
        /// otherwise, returns a single RFC-1751 word based on the node public key.
        /// </summary>
        [JsonPropertyName("hostid")]
        public string HostId { get; set; }

        /// <summary>
        /// Amount of time spent waiting for I/O operations, in milliseconds.<br/>
        /// If this number is not very, very low, then the rippled server is probably having serious load issues.
        /// </summary>
        [JsonPropertyName("io_latency_ms")]
        public int IoLatencyMs { get; set; }

        /// <summary>
        /// Information about the last time the server closed a ledger,
        /// including the amount of time it took to reach a consensus and the number of trusted validators participating.
        /// </summary>
        [JsonPropertyName("last_close")]
        public LastClose LastClose { get; set; }

        /// <summary>
        /// (Admin only) Detailed information about the current load state of the server.
        /// </summary>
        [JsonPropertyName("load")]
        public Load Load { get; set; }

        /// <summary>
        /// The load-scaled open ledger transaction cost the server is currently enforcing, as a multiplier on the base transaction cost.<br/>
        /// For example, at 1000 load factor and a reference transaction cost of 10 drops of XRP, the load-scaled transaction cost is 10,000 drops (0.01 XRP).<br/>
        /// The load factor is determined by the highest of the individual server's load factor,
        /// the cluster's load factor, the open ledger cost and the overall network's load factor.
        /// </summary>
        [JsonPropertyName("load_factor")]
        public double? LoadFactor { get; set; }

        /// <summary>
        /// Current multiplier to the transaction cost being used by the rest of the network.
        /// </summary>
        [JsonPropertyName("load_factor_net")]
        public double? LoadFactorNet { get; set; }

        /// <summary>
        /// How many other rippled servers this one is currently connected to.
        /// </summary>
        [JsonPropertyName("peers")]
        public int Peers { get; set; }

        /// <summary>
        /// Public key used to verify this server for peer-to-peer communications.<br/>
        /// This node key pair is automatically generated by the server the first   time it starts up.<br/>
        /// (If deleted, the server can create a new pair of Keys).
        /// </summary>
        [JsonPropertyName("pubkey_node")]
        public string PubkeyNode { get; set; }

        /// <summary>
        /// Public key used by this node to sign ledger validations.
        /// </summary>
        [JsonPropertyName("pubkey_validator")]
        public string PubkeyValidator { get; set; }

        /// <summary>
        /// A string indicating to what extent the server is participating in the   network.
        /// </summary>
        [JsonPropertyName("server_state")]
        [JsonConverter(typeof(EnumMemberValueConverter<ServerStateInner>))]
        public ServerStateInner ServerState { get; set; }

        /// <summary>
        /// A map of various server states with information about the time the   server spends in each.<br/>
        /// This can be useful for tracking the long-term   health of your server's connectivity to the network.
        /// </summary>
        [JsonPropertyName("state_accounting")]
        public AccountingStateSummary AccountingStateSummary { get; set; }

        /// <summary>
        /// Number of consecutive seconds that the server has been operational.
        /// </summary>
        [JsonPropertyName("uptime")]
        public int Uptime { get; set; }

        [JsonIgnore]
        public TimeSpan UptimeTimeSpan => TimeSpan.FromSeconds(Uptime);

        /// <summary>
        /// Information about the most recent fully-validated ledger.
        /// </summary>
        [JsonPropertyName("validated_ledger")]
        public ValidatedLedger? ValidatedLedger { get; set; }

        /// <summary>
        /// Minimum number of trusted validations required to validate a ledger   version.<br/>
        /// Some circumstances may cause the server to require more   validations.
        /// </summary>
        [JsonPropertyName("validation_quorum")]
        public long ValidationQuorum { get; set; }

        /// <summary>
        /// Either the human readable time, in UTC, when the current validator list will expire,
        /// the string unknown if the server has yet to load a published validator list or the string never if the server uses a static validator list.
        /// </summary>
        [JsonPropertyName("validator_list_expires")]
        [JsonConverter(typeof(NumberOrStringConverter))]
        public string ValidatorListExpires { get; set; }

        /// <summary>
        /// How long it took this server to reach a synchronised state after starting, in
        /// microseconds. A string, as the node sends it.
        /// </summary>
        [JsonPropertyName("initial_sync_duration_us")]
        public string InitialSyncDurationUs { get; set; }

        /// <summary>
        /// How many times this server's job queue has overflowed since it started.
        /// </summary>
        /// <remarks>
        /// Declared as a string because that is what the node sends - the same field on
        /// <c>server_state</c> is already modelled that way.
        /// </remarks>
        [JsonPropertyName("jq_trans_overflow")]
        public string JqTransOverflow { get; set; }

        /// <summary>
        /// How many peers this server has disconnected since it started.
        /// </summary>
        [JsonPropertyName("peer_disconnects")]
        public string PeerDisconnects { get; set; }

        /// <summary>
        /// How many peers this server has disconnected for exceeding a resource limit.
        /// </summary>
        [JsonPropertyName("peer_disconnects_resources")]
        public string PeerDisconnectsResources { get; set; }

        /// <summary>
        /// How long the server has been in its current <see cref="ServerState"/>, in microseconds.
        /// </summary>
        /// <remarks>
        /// A string here, although <c>ServerState.State</c> declares the same field as a number.
        /// Measured against a node rather than assumed: <c>server_info</c> sends
        /// <c>"25380868"</c>, quoted.
        /// </remarks>
        [JsonPropertyName("server_state_duration_us")]
        public string ServerStateDurationUs { get; set; }

        /// <summary>
        /// The server's current time in UTC, as a human-readable string.
        /// </summary>
        [JsonPropertyName("time")]
        public string Time { get; set; }

        /// <summary>
        /// The rough size of this server's configured node, e.g. <c>tiny</c>, <c>small</c>,
        /// <c>huge</c>.
        /// </summary>
        [JsonPropertyName("node_size")]
        public string NodeSize { get; set; }

        /// <summary>
        /// Which source revision this server was built from.
        /// </summary>
        /// <remarks>
        /// Not sent by every build - absent leaves this null rather than raising.
        /// </remarks>
        [JsonPropertyName("git")]
        public GitInfo Git { get; set; }

        /// <summary>
        /// The state of the published validator list this server is following.
        /// </summary>
        /// <remarks>
        /// Supersedes <see cref="ValidatorListExpires"/>, which modern rippled does not send.
        /// </remarks>
        [JsonPropertyName("validator_list")]
        public ValidatorListInfo ValidatorList { get; set; }

        /// <summary>
        /// The ports this server is listening on, and what speaks on each.
        /// </summary>
        [JsonPropertyName("ports")]
        public List<ServerPort> Ports { get; set; }

        //todo not found fields -  amendment_blocked?: boolean,  closed_ledger?:, load_factor_local?: number,   load_factor_cluster?: number
        //load_factor_fee_escalation?: number, load_factor_fee_queue?: number, load_factor_server?: number, network_ledger?: 'waiting'
    }

    /// <summary>
    /// Which source revision a server was built from.
    /// </summary>
    public class GitInfo : BaseMethodResult
    {
        /// <summary>The branch the build came from.</summary>
        [JsonPropertyName("branch")]
        public string Branch { get; set; }

        /// <summary>The commit the build came from.</summary>
        [JsonPropertyName("hash")]
        public string Hash { get; set; }
    }

    /// <summary>
    /// The state of the published validator list a server is following.
    /// </summary>
    public class ValidatorListInfo : BaseMethodResult
    {
        /// <summary>How many validator lists this server has loaded.</summary>
        [JsonPropertyName("count")]
        public int? Count { get; set; }

        /// <summary>
        /// When the list expires, in UTC - or <c>unknown</c> before a list is loaded, or
        /// <c>never</c> for a static configuration.
        /// </summary>
        [JsonPropertyName("expiration")]
        public string Expiration { get; set; }

        /// <summary>Whether the list is current, expired, or unknown.</summary>
        [JsonPropertyName("status")]
        public string Status { get; set; }
    }

    /// <summary>
    /// A port a server listens on, and what speaks on it.
    /// </summary>
    public class ServerPort : BaseMethodResult
    {
        /// <summary>
        /// The port number. A string, as the node sends it.
        /// </summary>
        [JsonPropertyName("port")]
        public string Port { get; set; }

        /// <summary>
        /// The protocols served on this port, e.g. <c>http</c>, <c>ws</c>, <c>peer</c>.
        /// </summary>
        [JsonPropertyName("protocol")]
        public List<string> Protocol { get; set; }
    }

    /// <summary>
    /// Information about the last time the server closed a ledger,
    /// including the amount of time it took to reach a consensus and the number of trusted validators participating.
    /// </summary>
    public class LastClose : BaseMethodResult
    {
        /// <summary>
        /// The amount of time it took to reach a consensus on the most recently  validated ledger version, in seconds.
        /// </summary>
        [JsonPropertyName("converge_time_s")]
        public double ConvergeTimeS { get; set; }

        /// <summary>
        /// How many trusted validators the server considered (including itself,if configured as a validator)
        /// in the consensus process for the most recently validated ledger version.
        /// </summary>
        [JsonPropertyName("proposers")]
        public int Proposers { get; set; }
    }

    /// <summary>
    /// (Admin only) Detailed information about the current load state of the server.
    /// </summary>
    public class JobType : BaseMethodResult
    {
        [JsonPropertyName("job_type")]
        public string JobTypeDescription { get; set; }

        [JsonPropertyName("per_second")]
        public int PerSecond { get; set; }

        [JsonPropertyName("in_progress")]
        public int? InProgress { get; set; }

        //todo not found fields peak_time?: number, avg_time?: number,  threads: number
        //https://github.com/XRPLF/xrpl.js/blob/b20c05c3680d80344006d20c44b4ae1c3b0ffcac/packages/xrpl/src/models/methods/serverInfo.ts#L28
    }

    /// <summary>
    /// (Admin only) Detailed information about the current load state of the   server.
    /// </summary>
    public class Load : BaseMethodResult
    {
        /// <summary>
        /// (Admin only) Information about the rate of different types of jobs  the server is doing and how much time it spends on each.
        /// </summary>
        [JsonPropertyName("job_types")]
        public List<JobType> JobTypes { get; set; }

        /// <summary>
        /// (Admin only) The number of threads in the server's main job pool.
        /// </summary>
        [JsonPropertyName("threads")]
        public int Threads { get; set; }
    }

    public class AccountingStateInfo : BaseMethodResult
    {
        [JsonPropertyName("duration_us")]
        public string DurationUs { get; set; }

        [JsonIgnore]
        public TimeSpan Duration
        {
            get
            {
                if (string.IsNullOrEmpty(DurationUs))
                    return TimeSpan.Zero;
                long microseconds = long.Parse(DurationUs, System.Globalization.CultureInfo.InvariantCulture);
                return TimeSpan.FromTicks(microseconds * 10);
            }
        }
        

        [JsonPropertyName("transitions")]
        public long Transitions { get; set; }
    }


    /// <summary>
    /// A map of various server states with information about the time the   server spends in each.<br/>
    /// This can be useful for tracking the long-term   health of your server's connectivity to the network.
    /// </summary>
    public class AccountingStateSummary : BaseMethodResult
    {
        [JsonPropertyName("connected")]
        public AccountingStateInfo Connected { get; set; }

        [JsonPropertyName("disconnected")]
        public AccountingStateInfo Disconnected { get; set; }

        [JsonPropertyName("full")]
        public AccountingStateInfo Full { get; set; }

        [JsonPropertyName("syncing")]
        public AccountingStateInfo Syncing { get; set; }

        [JsonPropertyName("tracking")]
        public AccountingStateInfo Tracking { get; set; }

        [JsonPropertyName("validating")]
        public AccountingStateInfo Validating { get; set; }

        [JsonPropertyName("proposing")]
        public AccountingStateInfo Proposing { get; set; }
    }

    /// <summary>
    /// Information about the most recent fully-validated ledger.
    /// </summary>
    public class ValidatedLedger : BaseMethodResult
    {
        /// <summary>
        /// The time since the ledger was closed, in seconds.
        /// </summary>
        [JsonPropertyName("age")]
        public int Age { get; set; }

        [JsonIgnore]
        public TimeSpan AgeTimeSpan => TimeSpan.FromSeconds(Age);

        /// <summary>
        /// Base fee, in XRP.<br/>
        /// This may be represented in scientific notation.<br/>
        /// Such as 1e-05 for 0.00005.
        /// </summary>
        [JsonPropertyName("base_fee_xrp")]
        [JsonConverter(typeof(ScientificDecimalConverter))]
        public decimal BaseFeeXrp { get; set; }

        /// <summary>
        /// Unique hash for the ledger, as hexadecimal.
        /// </summary>
        [JsonPropertyName("hash")]
        public string Hash { get; set; }

        /// <summary>
        /// Minimum amount of XRP (not drops) necessary for every account to.<br/>
        /// Keep in reserve.
        /// </summary>
        [JsonPropertyName("reserve_base_xrp")]
        [JsonConverter(typeof(ScientificDecimalConverter))]
        public decimal ReserveBaseXrp { get; set; }

        /// <summary>
        /// Amount of XRP (not drops) added to the account reserve for each  object an account owns in the ledger.
        /// </summary>
        [JsonPropertyName("reserve_inc_xrp")]
        [JsonConverter(typeof(ScientificDecimalConverter))]
        public decimal ReserveIncXrp { get; set; }

        /// <summary>
        /// The ledger index of the latest validated ledger.
        /// </summary>
        [JsonPropertyName("seq")]
        public int Sequence { get; set; }
    }
}
