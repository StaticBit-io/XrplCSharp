using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Subscriptions;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

using JsonSerializer = System.Text.Json.JsonSerializer;

using static Xrpl.Client.Connection;
using static Xrpl.Client.XrplClient;

using BookOffers = Xrpl.Models.Transactions.BookOffers;
using Submit = Xrpl.Models.Transactions.Submit;

// https://github.com/XRPLF/xrpl.js/blob/main/packages/xrpl/src/client/index.ts

// https://xrpl.org/public-api-methods.html
namespace Xrpl.Client
{

    public delegate Task OnError(string error, string errorMessage, string message, object data);
    public delegate Task OnWarning(string warning, string message);
    public delegate Task OnServerWarning(List<RippleResponseWarning> warning, string message);
    public delegate Task OnConnected();
    public delegate Task OnDisconnect(int? code, string? description);
    public delegate Task OnPing(string ping);
    public delegate Task OnLedgerClosed(LedgerStream response);
    public delegate Task OnTransaction(TransactionStream response);
    public delegate Task OnManifestReceived(ValidationStream response);
    public delegate Task OnPeerStatusChange(PeerStatusStream response);
    public delegate Task OnConsensusPhase(ConsensusStream response);
    public delegate Task OnPathFind(PathFindStream response);


    public interface IXrplClient : IDisposable
    {
        Connection connection { get; set; }
        double feeCushion { get; set; }
        string maxFeeXRP { get; set; }
        uint? networkID { get; set; }

        /// <summary>
        /// Set network id for transactions, required in network where Id > 1024
        /// </summary>
        /// <param name="networkId">network id</param>
        public void SetNetworkId(uint? networkId)
        {
            this.networkID = networkId;
        }

        #region Server
        /// <summary> the url </summary>
        string Url();
        /// <summary> connect to the server </summary>
        Task Connect(System.Threading.CancellationToken cancellationToken = default);
        /// <summary> Disconnect from server </summary>
        Task Disconnect();
        /// <summary>
        /// Disconnects and waits for the WebSocket to be fully closed and cleaned up.
        /// </summary>
        /// <param name="timeout">Maximum time to wait for cleanup.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task DisconnectAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
        /// <summary> if the websocket is connected </summary>
        bool IsConnected();
        /// <summary> The subscribe method requests periodic notifications from the server when certain events happen. </summary>
        /// <param name="request">An <see cref="SubscribeRequest"/> request.</param>
        /// <returns></returns>
        Task<object> Subscribe(SubscribeRequest request, CancellationToken cancellationToken = default);
        /// <summary> The unsubscribe command tells the server to stop sending messages for a particular subscription or set of subscriptions.</summary>
        /// <param name="request">An <see cref="UnsubscribeRequest"/> request.</param>
        /// <returns></returns>
        Task<object> Unsubscribe(UnsubscribeRequest request, CancellationToken cancellationToken = default);
        /// <summary>
        /// The ping command returns an acknowledgement,
        /// so that clients can test the connection status and latency
        /// </summary>
        /// <param name="request">An <see cref="PingRequest"/> request.</param>
        /// <returns></returns>
        Task<object> Ping(CancellationToken cancellationToken = default);
        /// <summary> The server_info command asks the server for a human-readable version of various information about the rippled server being queried. </summary>
        /// <param name="request">An <see cref="ServerInfoRequest"/> request.</param>
        /// <returns>A <see cref="ServerInfo"/> response.</returns>
        Task<ServerInfo> ServerInfo(ServerInfoRequest request, CancellationToken cancellationToken = default);
        /// <summary> The server_state command asks the server for a human-readable version of various information about the rippled server being queried. </summary>
        /// <param name="request">An <see cref="ServerStateRequest"/> request.</param>
        /// <returns>A <see cref="ServerState"/> response.</returns>
        Task<ServerState> ServerState(ServerStateRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// The feature command returns information about amendments this server knows about,<br/>
        /// including whether they are enabled and if the server knows how to apply the amendments.<br/><br/>
        /// 
        /// This is the non-admin version of the feature admin command.<br/>
        /// It follows the same formatting as the admin command, but hides potentially sensitive data.
        /// </summary>
        /// <param name="feature">
        /// (Optional) The unique ID of an amendment, as hexadecimal;<br/>
        /// or the short name of the amendment.<br/>
        /// If provided, limits the response to one amendment. Otherwise, the response lists all amendments.
        /// </param>
        /// <returns>A <see cref="ServerFeatures"/> response. Feature and their states</returns>
        Task<ServerFeatures> ServerFeatures(string feature = null, CancellationToken cancellationToken = default);

        /// <summary> The fee command reports the current state of the open-ledger requirements for the transaction cost. </summary>
        /// <param name="request">An <see cref="FeeRequest"/> request.</param>
        /// <returns>An <see cref="Models.Methods.Fee"/> response.</returns>
        Task<Fee> Fee(CancellationToken cancellationToken = default);

        #endregion

        #region Account
        //https://xrpl.org/account-methods.html
        /// <summary> The account_info command retrieves information about an account, its activity, and its XRP balance. </summary>
        /// <param name="request">An <see cref="AccountInfoRequest"/> request.</param>
        /// <returns>An <see cref="Models.Methods.AccountInfo"/> response.</returns>
        Task<AccountInfo> AccountInfo(AccountInfoRequest request, CancellationToken cancellationToken = default);


        /// <summary> The account_offers method retrieves a list of offers made by a given account that are outstanding as of a particular ledger version </summary>
        /// <param name="request">An <see cref="AccountOffersRequest"/> request.</param>
        /// <returns>An <see cref="Models.Methods.AccountOffers"/> response.</returns>
        Task<AccountOffers> AccountOffers(AccountOffersRequest request, CancellationToken cancellationToken = default);

        /// <summary> The account_currencies command retrieves a list of currencies that an account can send or receive, based on its trust lines. </summary>
        /// <param name="request">An <see cref="AccountCurrenciesRequest"/> request.</param>
        /// <returns>An <see cref="Models.Methods.AccountCurrencies"/> response.</returns>
        Task<AccountCurrencies> AccountCurrencies(AccountCurrenciesRequest request, CancellationToken cancellationToken = default);


        /// <summary>
        /// The account_lines method returns information about an account's trust lines, including balances in all non-XRP currencies and assets.
        /// </summary>
        /// <param name="request">An <see cref="AccountLinesRequest"/> request.</param>
        /// <returns>An <see cref="Models.Methods.AccountLines"/> response.</returns>
        Task<AccountLines> AccountLines(AccountLinesRequest request, CancellationToken cancellationToken = default);


        /// <summary>
        /// The AccountObjects command returns the raw ledger format for all objects owned by an account. For a higher-level view of an account's trust lines and balances, see <see cref="Models.Methods.AccountLines"/> instead.
        /// </summary>
        /// <param name="request">An <see cref="AccountObjectsRequest"/> request.</param>
        /// <returns>An <see cref="Models.Methods.AccountObjects"/> response.</returns>
        Task<AccountObjects> AccountObjects(AccountObjectsRequest request, CancellationToken cancellationToken = default);


        /// <summary>
        /// The noripple_check command provides a quick way to check the status of the Default Ripple field
        /// for an account and the No Ripple flag of its trust lines, compared with the recommended settings
        /// </summary>
        /// <returns>An <see cref="NoRippleCheckRequest"/> response.</returns>
        /// <returns>An <see cref="Models.Methods.NoRippleCheck"/> response.</returns>
        Task<NoRippleCheck> NoRippleCheck(NoRippleCheckRequest request, CancellationToken cancellationToken = default);


        /// <summary> The gateway_balances command calculates the total balances issued by a given account,
        /// optionally excluding amounts held by operational addresses. </summary>
        /// <param name="request">An <see cref="GatewayBalancesRequest"/> request.</param>
        /// <returns>An <see cref="GatewayBalancesResponse"/> response.</returns>
        Task<GatewayBalancesResponse> GatewayBalances(GatewayBalancesRequest request, CancellationToken cancellationToken = default);


        /// <summary> The account_tx method retrieves a list of transactions that involved the specified account </summary>
        /// <param name="request">An <see cref="AccountTransactionsRequest"/> request.</param>
        /// <returns>An <see cref="Models.Methods.AccountTransactions"/> response.</returns>
        Task<AccountTransactions> AccountTransactions(AccountTransactionsRequest request, CancellationToken cancellationToken = default);
        /// <summary> The account_channels method returns information about an account's Payment Channels.
        /// This includes only channels where the specified account is the channel's source, not the destination. </summary>
        /// <param name="request">An <see cref="AccountChannelsRequest"/> request.</param>
        /// <returns>An <see cref="Models.Methods.AccountChannels"/> response.</returns>
        Task<AccountChannels> AccountChannels(AccountChannelsRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// The simulate method executes a dry run of any transaction type,
        /// enabling you to preview the results and metadata of a transaction without committing them to the XRP Ledger.<br/>
        /// Since this command never submits a transaction to the network, it doesn't incur any fees.<br/>
        /// Expects a response in the form of a  <see cref="SimulateRequest"/> .
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        Task<SimulateResponse> Simulate(SimulateRequest request, CancellationToken cancellationToken = default);
        #endregion

        #region NFT


        /// <summary> The nft_buy_offers method returns a list of buy offers for a given NFToken object. </summary>
        /// <param name="request">An <see cref="NFTBuyOffersRequest"/> request.</param>
        /// <returns>An <see cref="Models.Methods.NFTBuyOffers"/> response.</returns>
        Task<NFTBuyOffers> NFTBuyOffers(NFTBuyOffersRequest request, CancellationToken cancellationToken = default);

        /// <summary> The nft_sell_offers method returns a list of sell offers for a given NFToken object</summary>
        /// <param name="request">An <see cref="NFTSellOffersRequest"/> request.</param>
        /// <returns>An <see cref="Models.Methods.NFTSellOffers"/> response.</returns>
        Task<NFTSellOffers> NFTSellOffers(NFTSellOffersRequest request, CancellationToken cancellationToken = default);


        /// <summary> The account_nfts method returns a list of NFToken objects for the specified account.</summary>
        /// <param name="request">An <see cref="AccountNFTsRequest"/> request.</param>
        /// <returns>An <see cref="Models.Methods.AccountNFTs"/> response.</returns>
        Task<AccountNFTs> AccountNFTs(AccountNFTsRequest request, CancellationToken cancellationToken = default);


        #endregion

        #region Transactions
        ////https://xrpl.org/transaction-methods.html
        ///// <summary>
        ///// The submit method applies a transaction and sends it to the network to be confirmed and included in future ledgers.
        ///// </summary>
        ///// <param name="request">An <see cref="SubmitRequest"/> request.</param>
        ///// <returns>An <see cref="Models.Transaction.Submit"/> response.</returns>
        //Task<Submit> Submit(SubmitRequest request);
        /// <summary>
        /// Submits a transaction to the XRP Ledger for processing.
        /// </summary>
        /// <param name="tx">
        /// Transaction in JSON format with an array of Signers.<br/>
        /// To be successful, the weights of the signatures must be equal or higher than the quorum of the SignerList.
        /// </param>
        /// <param name="wallet">wallet</param>
        /// <param name="autoFill">use autofill for tx</param>
        /// <param name="failHard">yse fail hard</param>
        /// <returns>An <see cref="Models.Transactions.Submit"/> response.</returns>
        Task<Submit> Submit(Dictionary<string, object> tx, XrplWallet wallet, bool autoFill = true, bool failHard = false, CancellationToken cancellationToken = default);
        /// <summary>
        /// Submits a transaction to the XRP Ledger for processing.
        /// </summary>
        /// <param name="tx">
        /// Transaction.<br/>
        /// To be successful, the weights of the signatures must be equal or higher than the quorum of the SignerList.
        /// </param>
        /// <param name="wallet">wallet</param>
        /// <param name="autoFill">use autofill for tx</param>
        /// <param name="failHard">yse fail hard</param>
        /// <returns>An <see cref="Models.Transactions.Submit"/> response.</returns>
        Task<Submit> Submit(ITransactionRequest tx, XrplWallet wallet, bool autoFill = true, bool failHard = false, CancellationToken cancellationToken = default);
        /// <summary>
        /// The tx method retrieves information on a single transaction, by its identifying hash
        /// </summary>
        /// <param name="request">An <see cref="TxRequest"/> request.</param>
        /// <returns>An <see cref="TransactionResponse"/> response.</returns>
        Task<TransactionResponse> Tx(TxRequest request, CancellationToken cancellationToken = default);

        Task<TransactionSummary> TxV2(TxRequest request, CancellationToken cancellationToken = default);
        #endregion

        #region Channels


        #endregion

        #region Ledger
        //https://xrpl.org/ledger-methods.html

        /// <summary>
        /// The ledger_request command tells server to fetch a specific ledger version from its connected peers.
        /// This only works if one of the server's immediately-connected peers has that ledger.
        /// You may need to run the command several times to completely fetch a ledger
        /// </summary>
        /// <param name="request">An <see cref="LedgerRequest"/> request.</param>
        /// <returns>An <see cref="LOLedger"/> response.</returns>
        Task<LOLedger> Ledger(LedgerRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// The ledger_data method retrieves contents of the specified ledger.
        /// You can iterate through several calls to retrieve the entire contents of a single ledger version.
        /// </summary>
        /// <param name="request">An <see cref="LedgerDataRequest"/> request.</param>
        /// <returns>An <see cref="LOLedgerData"/> response.</returns>
        Task<LOLedgerData> LedgerData(LedgerDataRequest request, CancellationToken cancellationToken = default);
        /// <summary> The ledger_closed method returns the unique identifiers of the most recently closed ledger. </summary>
        /// <param name="request">An <see cref="LedgerClosedRequest"/> response.</param>
        /// <returns>An <see cref="LOBaseLedger"/> response.</returns>
        Task<LOBaseLedger> LedgerClosed(LedgerClosedRequest request, CancellationToken cancellationToken = default);
        /// <summary>
        /// The ledger_current method returns the unique identifiers of the current in-progress ledger.<br/>
        /// This command is mostly useful for testing, because the ledger returned is still in flux.
        /// </summary>
        /// <param name="request">An <see cref="LedgerCurrentRequest"/> response.</param>
        /// <returns>An <see cref="LOLedgerCurrentIndex"/> response.</returns>
        Task<LOLedgerCurrentIndex> LedgerCurrent(LedgerCurrentRequest request, CancellationToken cancellationToken = default);
        /// <summary>
        /// The ledger_entry method returns a single ledger object from the XRP Ledger in its raw format.<br/>
        /// See ledger format for information on the different types of objects you can retrieve.
        /// </summary>
        /// <param name="request">An <see cref="LedgerEntryRequest"/> response.</param>
        /// <returns>An <see cref="LedgerEntryResponse"/> response.</returns>
        Task<LedgerEntryResponse> LedgerEntry(LedgerEntryRequest request, CancellationToken cancellationToken = default);


        #endregion

        /// <summary>
        /// The amm_info method gets information about an Automated Market Maker (AMM) instance.
        /// </summary>
        /// <param name="request">An <see cref="AMMInfoRequest"/> request.</param>
        /// <returns>An <see cref="AMMInfoResponse"/> response.</returns>
        Task<AMMInfoResponse> AmmInfo(AMMInfoRequest request, CancellationToken cancellationToken = default);
        /// <summary>
        /// The book_offers method retrieves a list of offers, also known as the order book , between two currencies
        /// </summary>
        /// <param name="request">An <see cref="BookOffersRequest"/> request.</param>
        /// <returns>An <see cref="Models.Transactions.BookOffers"/> response.</returns>
        Task<BookOffers> BookOffers(BookOffersRequest request, CancellationToken cancellationToken = default);
        /// <summary>
        /// The random command provides a random number to be used as a source of entropy for random number generation by clients.<br/>
        /// https://xrpl.org/random.html#random
        /// </summary>
        /// <param name="request">An <see cref="RandomRequest"/> request.</param>
        /// <returns></returns>
        Task<object> Random(CancellationToken cancellationToken = default);

        /// <summary>
        /// The <c>deposit_authorized</c> command indicates whether one account is authorized to send payments
        /// directly to another. https://xrpl.org/deposit_authorized.html
        /// </summary>
        /// <param name="request">A <see cref="DepositAuthorizedRequest"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="DepositAuthorized"/> response.</returns>
        Task<DepositAuthorized> DepositAuthorized(DepositAuthorizedRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// The <c>path_find</c> create sub-command creates an ongoing request to find possible paths
        /// along which a payment transaction could be made.<br/>
        /// WebSocket API only.<br/>
        /// After the initial response, the server sends asynchronous follow-ups via the <see cref="OnPathFind"/> event.
        /// </summary>
        /// <param name="request">A <see cref="PathFindCreateRequest"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="PathFindResponse"/> with initial path alternatives.</returns>
        Task<PathFindResponse> PathFind(PathFindCreateRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// The <c>path_find</c> close sub-command instructs the server to stop sending information
        /// about the current open pathfinding request.
        /// </summary>
        /// <param name="request">A <see cref="PathFindCloseRequest"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="PathFindResponse"/>.</returns>
        Task<PathFindResponse> PathFindClose(PathFindCloseRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// The <c>path_find</c> status sub-command requests an immediate update about the client's
        /// currently-open pathfinding request.
        /// </summary>
        /// <param name="request">A <see cref="PathFindStatusRequest"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="PathFindResponse"/>.</returns>
        Task<PathFindResponse> PathFindStatus(PathFindStatusRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// The <c>ripple_path_find</c> method is a simplified version of the path_find method
        /// that provides a single response with a payment path you can use right away.<br/>
        /// Available in both WebSocket and JSON-RPC APIs.
        /// </summary>
        /// <param name="request">A <see cref="RipplePathFindRequest"/>.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A <see cref="RipplePathFindResponse"/>.</returns>
        Task<RipplePathFindResponse> RipplePathFind(RipplePathFindRequest request, CancellationToken cancellationToken = default);

        // Task<ServerState> ServerState(ServerStateRequest request);
        //Task<SubmitMultisign> SubmitMultisign(SubmitMultisignRequest request);
        //Task<TransactionEntry> TransactionEntry(TransactionEntryRequest request);
        Task<object> AnyRequest(BaseRequest request, CancellationToken cancellationToken = default);

        Task<Dictionary<string, object>> Request(Dictionary<string, object> request, CancellationToken cancellationToken = default);
        Task<T> GRequest<T, R>(R request, CancellationToken cancellationToken = default) where R : BaseRequest;


        #region Sugars
        /// <summary>
        /// Autofills fields in a transaction. This will set `Sequence`, `Fee`,
        /// `lastLedgerSequence` according to the current state of the server this Client
        /// is connected to. It also converts all X-Addresses to classic addresses and
        /// flags interfaces into numbers.
        /// </summary>
        /// <param name="tx">A {@link Transaction} in JSON format</param>
        /// <param name="signersCount">The expected number of signers for this transaction. Only used for multisigned transactions.</param>
        /// <returns>The autofilled transaction.</returns>
        Task<Dictionary<string, object>> Autofill(Dictionary<string, object> tx, int? signersCount = null, CancellationToken cancellationToken = default);
        /// <summary>
        /// Autofills fields in a transaction. This will set `Sequence`, `Fee`,
        /// `lastLedgerSequence` according to the current state of the server this Client
        /// is connected to. It also converts all X-Addresses to classic addresses and
        /// flags interfaces into numbers.
        /// </summary>
        /// <param name="tx">A {@link Transaction} in JSON format</param>
        /// <param name="signersCount">The expected number of signers for this transaction. Only used for multisigned transactions.</param>
        /// <returns>The autofilled transaction.</returns>
        Task<T> Autofill<T>(T tx, int? signersCount = null, CancellationToken cancellationToken = default) where T : ITransactionRequest;
        Task<uint> GetLedgerIndex(CancellationToken cancellationToken = default);
        Task<string> GetXrpBalance(string address, CancellationToken cancellationToken = default);
        Task ChangeServer(string server, ClientOptions? options = null, CancellationToken cancellationToken = default);

        string EnsureClassicAddress(string address);

        #endregion
    }

    public class XrplClient : IXrplClient
    {

        public class ClientOptions : ConnectionOptions
        {
            public uint? NetworkID { get; set; }
            public double? feeCushion { get; set; }
            public string? maxFeeXRP { get; set; }

            /// <summary>
            /// The API version to use when making requests.
            /// </summary>
            public uint? ApiVersion { get; set; }
        }

        public Connection connection { get; set; }
        public double feeCushion { get; set; }
        public string maxFeeXRP { get; set; }
        public uint? networkID { get; set; }

        /// <summary>
        /// The API version to use when making requests.
        /// </summary>
        public uint ApiVersion { get; set; }
        //public event OnError OnError;
        //public event OnConnected OnConnected;
        //public event OnDisconnect OnDisconnect;
        //public event OnLedgerClosed OnLedgerClosed;
        //public event OnTransaction OnTransaction;
        //public event OnManifestReceived OnManifestReceived;
        //public event OnPeerStatusChange OnPeerStatusChange;
        //public event OnConsensusPhase OnConsensusPhase;
        //public event OnPathFind OnPathFind;

        ///// <summary> Current web socket client state </summary>
        //public WebSocketState SocketState => client.State;

        private readonly ConcurrentDictionary<int, TaskInfo> tasks;

        public XrplClient(string server, ClientOptions? options = null)
        {

            if (!IsValidWss(server))
            {
                throw new Exception("Invalid WSS Server Url");
            }
            SetSettings(options);
            connection = new Connection(server, options);
        }

        private void SetSettings(ClientOptions options)
        {
            if (feeCushion != 0 && options is null)
            {
                return;
            }

            feeCushion = options?.feeCushion ?? 1.2;
            maxFeeXRP = options?.maxFeeXRP;
            networkID = options?.NetworkID;
            ApiVersion = options?.ApiVersion ?? 2;
        }

        public async Task ChangeServer(string server, ClientOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (!IsValidWss(server))
            {
                throw new Exception("Invalid WSS Server Url");
            }
            SetSettings(options);

            await connection.ChangeServer(server, options, cancellationToken);
            await SetNetworkId();
        }

        /// <inheritdoc />
        public string Url()
        {
            return this.connection.GetUrl();
        }

        public bool IsValidWss(string server)
        {
            return true;
        }

        /// <summary>
        /// Connect to the server
        /// </summary>
        /// <param name="cancellationToken">cancellation token</param>
        /// <returns></returns>
        public async Task Connect(System.Threading.CancellationToken cancellationToken = default)
        {
            await connection.Connect(cancellationToken);
            await SetNetworkId();
        }

        private async Task SetNetworkId()
        {
            var server = await ServerInfo(new ServerInfoRequest());
            if (server?.Info?.NetworkID is { } id and > 1024)
            {
                SetNetworkId(id);
            }
            else
            {
                SetNetworkId(networkId: null);
            }
        }

        public void SetNetworkId(uint? networkId)
        {
            this.networkID = networkId;
        }

        /// <inheritdoc />
        public async Task Disconnect()
        {
            await connection.Disconnect();
        }

        /// <inheritdoc />
        public async Task DisconnectAndWaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            await connection.DisconnectAndWaitAsync(timeout, cancellationToken);
        }

        /// <inheritdoc />
        public bool IsConnected()
        {
            return this.connection.IsConnected();
        }

        // SUGARS
        public Task<Dictionary<string, object>> Autofill(Dictionary<string, object> tx, int? signersCount = null, CancellationToken cancellationToken = default)
        {
            return AutofillSugar.Autofill(this, tx, signersCount, cancellationToken);
        }
        public async Task<T> Autofill<T>(T tx, int? signersCount = null, CancellationToken cancellationToken = default) where T : ITransactionRequest
        {
            var dic = tx.ToDictionary();
            var filled = await AutofillSugar.Autofill(this, dic, signersCount, cancellationToken).ConfigureAwait(false);
            var json = JsonSerializer.Serialize(filled, XrplJsonOptions.Default);
            tx = (T)JsonSerializer.Deserialize(json, tx.GetType(), XrplJsonOptions.Default);

            return tx;
        }

        /// <inheritdoc />
        public Task<Submit> Submit(Dictionary<string, object> tx, XrplWallet wallet, bool autoFill = true, bool failHard = false, CancellationToken cancellationToken = default)
        {
            if (this.networkID is { } network)
            {
                tx["NetworkID"] = network;
            }

            return this.Submit(tx, autoFill, failHard, wallet, cancellationToken);
        }
        /// <inheritdoc />
        public Task<Submit> Submit(ITransactionRequest tx, XrplWallet wallet, bool autoFill = true, bool failHard = false, CancellationToken cancellationToken = default)
        {
            if (this.networkID is { } network)
            {
                tx.NetworkID = network;
            }

            var json = tx.ToJson();
            //var json = JsonConvert.SerializeObject(tx);
            Dictionary<string, object> txJson = JsonSerializer.Deserialize<Dictionary<string, object>>(json, XrplJsonOptions.Default);
            return this.Submit(txJson, autoFill, failHard, wallet, cancellationToken);
        }

        /// <inheritdoc />
        public Task<uint> GetLedgerIndex(CancellationToken cancellationToken = default)
        {
            return GetLedgerSugar.GetLedgerIndex(this, cancellationToken);
        }
        /// <inheritdoc />
        public Task<string> GetXrpBalance(string address, CancellationToken cancellationToken = default)
        {
            return BalancesSugar.GetXrpBalance(this, address, cancellationToken: cancellationToken);
        }

        // REQUESTS
        /// <inheritdoc />
        public Task<AccountChannels> AccountChannels(AccountChannelsRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<AccountChannels, AccountChannelsRequest>(request, cancellationToken);
        }

        public Task<SimulateResponse> Simulate(SimulateRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<SimulateResponse, SimulateRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<AccountCurrencies> AccountCurrencies(AccountCurrenciesRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<AccountCurrencies, AccountCurrenciesRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<AccountInfo> AccountInfo(AccountInfoRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<AccountInfo, AccountInfoRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<AccountLines> AccountLines(AccountLinesRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<AccountLines, AccountLinesRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<AccountNFTs> AccountNFTs(AccountNFTsRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<AccountNFTs, AccountNFTsRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<AccountObjects> AccountObjects(AccountObjectsRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<AccountObjects, AccountObjectsRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<AccountOffers> AccountOffers(AccountOffersRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<AccountOffers, AccountOffersRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<AccountTransactions> AccountTransactions(AccountTransactionsRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<AccountTransactions, AccountTransactionsRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<AMMInfoResponse> AmmInfo(AMMInfoRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<AMMInfoResponse, AMMInfoRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<BookOffers> BookOffers(BookOffersRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<BookOffers, BookOffersRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<DepositAuthorized> DepositAuthorized(DepositAuthorizedRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<DepositAuthorized, DepositAuthorizedRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<LOLedger> Ledger(LedgerRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<LOLedger, LedgerRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<LOBaseLedger> LedgerClosed(LedgerClosedRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<LOBaseLedger, LedgerClosedRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<LOLedgerCurrentIndex> LedgerCurrent(LedgerCurrentRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<LOLedgerCurrentIndex, LedgerCurrentRequest>(request, cancellationToken);
        }
        /// <inheritdoc />
        public Task<LOLedgerData> LedgerData(LedgerDataRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<LOLedgerData, LedgerDataRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<LedgerEntryResponse> LedgerEntry(LedgerEntryRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<LedgerEntryResponse, LedgerEntryRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<Fee> Fee(CancellationToken cancellationToken = default)
        {
            FeeRequest request = new FeeRequest();
            return this.GRequest<Fee, FeeRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<GatewayBalancesResponse> GatewayBalances(GatewayBalancesRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<GatewayBalancesResponse, GatewayBalancesRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<NFTBuyOffers> NFTBuyOffers(NFTBuyOffersRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<NFTBuyOffers, NFTBuyOffersRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<NFTSellOffers> NFTSellOffers(NFTSellOffersRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<NFTSellOffers, NFTSellOffersRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<NoRippleCheck> NoRippleCheck(NoRippleCheckRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<NoRippleCheck, NoRippleCheckRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<PathFindResponse> PathFind(PathFindCreateRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<PathFindResponse, PathFindCreateRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<PathFindResponse> PathFindClose(PathFindCloseRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<PathFindResponse, PathFindCloseRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<PathFindResponse> PathFindStatus(PathFindStatusRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<PathFindResponse, PathFindStatusRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<object> Ping(CancellationToken cancellationToken = default)
        {
            PingRequest request = new PingRequest();
            return this.GRequest<object, PingRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<object> Random(CancellationToken cancellationToken = default)
        {
            RandomRequest request = new RandomRequest();
            return this.GRequest<object, RandomRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<RipplePathFindResponse> RipplePathFind(RipplePathFindRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<RipplePathFindResponse, RipplePathFindRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<ServerInfo> ServerInfo(ServerInfoRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<ServerInfo, ServerInfoRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<ServerState> ServerState(ServerStateRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<ServerState, ServerStateRequest>(request, cancellationToken);
        }
        /// <inheritdoc />
        public Task<ServerFeatures> ServerFeatures(string feature = null, CancellationToken cancellationToken = default)
        {
            var request = new ServerFeaturesRequest()
            {
                Feature = feature
            };
            return this.GRequest<ServerFeatures, ServerFeaturesRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        //public Task<Submit> Submit(SubmitRequest request)
        //{
        //    return this.GRequest<Submit, SubmitRequest>(request);
        //}

        //public Task<SubmitMultisign> SubmitMultisign(SubmitMultisignRequest request, Wallet wallet)
        //{
        //    return this.GRequest<SubmitMultisign, SubmitMultisignRequest>(request);
        //}

        /// <inheritdoc />
        public Task<object> Subscribe(SubscribeRequest request, CancellationToken cancellationToken = default)
        {

            return this.GRequest<object, SubscribeRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<object> Unsubscribe(UnsubscribeRequest request, CancellationToken cancellationToken = default)
        {

            return this.GRequest<object, UnsubscribeRequest>(request, cancellationToken);
        }

        //public Task<TransactionEntry> TransactionEntry(TransactionEntryRequest request)
        //{
        //    return this.GRequest<TransactionEntry, TransactionEntryRequest>(request);
        //}

        /// <inheritdoc />
        public Task<TransactionResponse> Tx(TxRequest request, CancellationToken cancellationToken = default)
        {
            request.ApiVersion = 1;
            return this.GRequest<TransactionResponse, TxRequest>(request, cancellationToken);
        }

        public Task<TransactionSummary> TxV2(TxRequest request, CancellationToken cancellationToken = default)
        {
            request.ApiVersion = 2;
            return this.GRequest<TransactionSummary, TxRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<object> AnyRequest(BaseRequest request, CancellationToken cancellationToken = default)
        {
            return this.GRequest<object, BaseRequest>(request, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<Dictionary<string, object>> Request(Dictionary<string, object> request, CancellationToken cancellationToken = default)
        {
            //string account = request["Account"] ? EnsureClassicAddress((string)request["account"]) : null;
            //request["Account"] = account;
            if(!request.TryGetValue(nameof(ApiVersion), out var value)){
            {
                request[nameof(ApiVersion)] = ApiVersion;
            }}
            var response = await this.connection.Request(request, cancellationToken: cancellationToken);

            // mutates `response` to add warnings
            //handlePartialPayment(req.command, response)
            return response;

        }

        /// <inheritdoc />
        public async Task<T> GRequest<T, R>(R request, CancellationToken cancellationToken = default) where R : BaseRequest
        {
            request.ApiVersion ??= ApiVersion;
            object response = await this.connection.GRequest<T, R>(request, cancellationToken: cancellationToken);
            
            // mutates `response` to add warnings
            //handlePartialPayment(req.command, response)
            
            return (T)response;
        }

        public string EnsureClassicAddress(string address)
        {
            return Xrpl.Sugar.Utils.EnsureClassicAddress(address);
        }

        #region IDisposable

        public void Dispose()
        {
            // todo: should check for ws...
            connection?.Disconnect();
        }

        #endregion
    }
}
