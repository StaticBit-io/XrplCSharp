namespace Xrpl.Models
{
    /// <summary>
    /// The type of a transaction (TransactionType field) is the most fundamental information about a transaction.<br/>
    /// This indicates what type of operation the transaction is supposed to do.
    /// </summary>
    public enum TransactionType
    {
        /// <summary> Set options on an account.</summary>
        AccountSet,
        /// <summary> Delete an account.</summary>
        AccountDelete,
        /// <summary> Cancel a check.</summary>
        CheckCancel,
        /// <summary> Redeem a check.</summary>
        CheckCash,
        /// <summary>Create a check.</summary>
        CheckCreate,
        /// <summary>Preauthorizes an account to send payments to this one.</summary>
        DepositPreauth,
        /// <summary>Reclaim escrowed XRP.</summary>
        EscrowCancel,
        /// <summary>Create an escrowed XRP payment.</summary>
        EscrowCreate,
        /// <summary>Deliver escrowed XRP to recipient.</summary>
        EscrowFinish,
        /// <summary>Accept an offer to buy or sell an NFToken.</summary>
        NFTokenAcceptOffer,
        /// <summary>Use TokenBurn to permanently destroy NFTs.</summary>
        NFTokenBurn,
        /// <summary>Cancel existing token offers to buy or sell an NFToken.</summary>
        NFTokenCancelOffer,
        /// <summary>Create an offer to buy or sell NFTs.</summary>
        NFTokenCreateOffer,
        /// <summary>Use TokenMint to issue new NFTs.</summary>
        NFTokenMint,
        /// <summary>Withdraw a currency-exchange order.</summary>
        OfferCancel,
        /// <summary>Submit an order to exchange currency.</summary>
        OfferCreate,
        /// <summary>Send funds from one account to another.</summary>
        Payment,
        /// <summary>Claim money from a payment channel.</summary>
        PaymentChannelClaim,
        /// <summary>Open a new payment channel.</summary>
        PaymentChannelCreate,
        /// <summary>Add more XRP to a payment channel.</summary>
        PaymentChannelFund,
        /// <summary>Add, remove, or modify an account's regular key pair.</summary>
        SetRegularKey,
        /// <summary>Add, remove, or modify an account's multi-signing list.</summary>
        SignerListSet,
        /// <summary>Set aside one or more sequence numbers as Tickets.</summary>
        TicketCreate,
        /// <summary>Add or modify a trust line.</summary>
        TrustSet,
        /// <summary>
        /// An EnableAmendment pseudo-transaction marks a change in the status of a proposed amendment when it:<br/>
        /// * Gains supermajority approval from validators.<br/>
        /// * Loses supermajority approval.<br/>
        /// * Is enabled on the XRP Ledger protocol.
        /// </summary>
        EnableAmendment,
        /// <summary>
        /// A SetFee pseudo-transaction marks a change in transaction cost or reserve requirements as a result of Fee Voting.
        /// </summary>
        SetFee,
        /// <summary>
        /// A UNLModify pseudo-transaction marks a change to the Negative UNL, indicating that a trusted validator has gone offline or come back online.
        /// </summary>
        UNLModify,
        /// <summary> AMMBid is used for submitting a vote for the trading fee of an AMM Instance. </summary>
        AMMBid,
        /// <summary>
        /// AMMCreate is used to create AccountRoot and the corresponding AMM ledger entries.
        /// </summary>
        AMMCreate,
        /// <summary>
        /// Delete an empty Automated Market Maker (AMM) instance that could not be fully deleted automatically.
        /// </summary>
        AMMDelete,
        /// <summary>
        /// AMMDeposit is the deposit transaction used to add liquidity to the AMM instance pool,
        /// thus obtaining some share of the instance's pools in the form of LPTokenOut.
        /// </summary>
        AMMDeposit,
        /// <summary>
        /// AMMVote is used for submitting a vote for the trading fee of an AMM Instance.
        /// </summary>
        AMMVote,
        /// <summary>
        /// AMMWithdraw is the withdraw transaction used to remove liquidity from the AMM
        /// instance pool, thus redeeming some share of the pools that one owns in the form
        /// of LPTokenIn.
        /// </summary>
        AMMWithdraw,
        /// <summary>
        /// The Clawback transaction is used by the token issuer to claw back issued tokens from a holder.
        /// </summary>
        Clawback,

        /// <summary>
        /// The AMMClawback transaction claws back tokens from an Automated Market Maker (AMM) pool.
        /// It allows the issuer to recover tokens that a holder has deposited into an AMM.
        /// </summary>
        AMMClawback,

        /// <summary>
        /// The NFTokenModify transaction modifies an NFToken's URI if its tfMutable is set to true.
        /// </summary>
        NFTokenModify,

        /// <summary>
        /// Unknown tx Type.
        /// </summary>
        Unknown,

        Batch,

        /// <summary>
        /// The MPTokenIssuanceCreate transaction creates an MPTokenIssuance object.
        /// </summary>
        MPTokenIssuanceCreate,

        /// <summary>
        /// The MPTokenIssuanceDestroy transaction removes an MPTokenIssuance object.
        /// </summary>
        MPTokenIssuanceDestroy,

        /// <summary>
        /// The MPTokenIssuanceSet transaction is used to globally lock/unlock an MPTokenIssuance.
        /// </summary>
        MPTokenIssuanceSet,

        /// <summary>
        /// The MPTokenAuthorize transaction authorizes an account to hold an MPT.
        /// </summary>
        MPTokenAuthorize,

        /// <summary>
        /// Creates a new Oracle ledger entry or updates the fields of an existing one.
        /// </summary>
        OracleSet,

        /// <summary>
        /// Deletes an Oracle ledger entry.
        /// </summary>
        OracleDelete,

        /// <summary>
        /// Creates or updates the DID (Decentralized Identifier) associated with an account.
        /// </summary>
        DIDSet,

        /// <summary>
        /// Deletes the DID (Decentralized Identifier) associated with an account.
        /// </summary>
        DIDDelete,

        /// <summary>
        /// Create a permissioned domain, or modify one that you own.
        /// </summary>
        PermissionedDomainSet,

        /// <summary>
        /// Delete a permissioned domain that you own.
        /// </summary>
        PermissionedDomainDelete,

        /// <summary>
        /// Creates a new credential issued to a subject account.
        /// Requires the Credentials amendment.
        /// </summary>
        CredentialCreate,

        /// <summary>
        /// Accepts a provisionally-issued credential, making it valid.
        /// Requires the Credentials amendment.
        /// </summary>
        CredentialAccept,

        /// <summary>
        /// Deletes (revokes) a credential from the ledger.
        /// Requires the Credentials amendment.
        /// </summary>
        CredentialDelete,

        /// <summary>
        /// Creates a cross-chain bridge between a locking chain and an issuing chain.
        /// </summary>
        XChainCreateBridge,

        /// <summary>
        /// Modifies the parameters of an existing cross-chain bridge.
        /// </summary>
        XChainModifyBridge,

        /// <summary>
        /// Creates a new cross-chain claim ID that is used for a cross-chain transfer.
        /// </summary>
        XChainCreateClaimID,

        /// <summary>
        /// Initiates a cross-chain transfer of value. Locks value on the source chain.
        /// </summary>
        XChainCommit,

        /// <summary>
        /// Completes a cross-chain transfer of value by claiming the locked value on the destination chain.
        /// </summary>
        XChainClaim,

        /// <summary>
        /// Creates a new account on the destination chain via a cross-chain transfer.
        /// </summary>
        XChainAccountCreateCommit,

        /// <summary>
        /// Adds an attestation to a cross-chain claim. Witnesses use this to confirm cross-chain transfers.
        /// </summary>
        XChainAddClaimAttestation,

        /// <summary>
        /// Adds an attestation to a cross-chain account create. Witnesses use this to confirm account creation.
        /// </summary>
        XChainAddAccountCreateAttestation,

        /// <summary>
        /// Creates a new vault for holding pooled assets.
        /// </summary>
        /// <remarks>Requires the Vault amendment (XLS-65d). This feature is in draft and subject to change.</remarks>
        VaultCreate,

        /// <summary>
        /// Modifies the settings of an existing vault.
        /// </summary>
        /// <remarks>Requires the Vault amendment (XLS-65d). This feature is in draft and subject to change.</remarks>
        VaultSet,

        /// <summary>
        /// Deletes an empty vault.
        /// </summary>
        /// <remarks>Requires the Vault amendment (XLS-65d). This feature is in draft and subject to change.</remarks>
        VaultDelete,

        /// <summary>
        /// Deposits assets into a vault.
        /// </summary>
        /// <remarks>Requires the Vault amendment (XLS-65d). This feature is in draft and subject to change.</remarks>
        VaultDeposit,

        /// <summary>
        /// Withdraws assets from a vault.
        /// </summary>
        /// <remarks>Requires the Vault amendment (XLS-65d). This feature is in draft and subject to change.</remarks>
        VaultWithdraw,

        /// <summary>
        /// Claws back assets from a vault.
        /// </summary>
        /// <remarks>Requires the Vault amendment (XLS-65d). This feature is in draft and subject to change.</remarks>
        VaultClawback,

        /// <summary>
        /// Creates or modifies a loan broker.
        /// </summary>
        /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
        LoanBrokerSet,

        /// <summary>
        /// Deletes a loan broker.
        /// </summary>
        /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
        LoanBrokerDelete,

        /// <summary>
        /// Deposits cover assets into a loan broker.
        /// </summary>
        /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
        LoanBrokerCoverDeposit,

        /// <summary>
        /// Withdraws cover assets from a loan broker.
        /// </summary>
        /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
        LoanBrokerCoverWithdraw,

        /// <summary>
        /// Claws back cover assets from a loan broker.
        /// </summary>
        /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
        LoanBrokerCoverClawback,

        /// <summary>
        /// Creates or modifies a loan.
        /// </summary>
        /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
        LoanSet,

        /// <summary>
        /// Deletes a loan.
        /// </summary>
        /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
        LoanDelete,

        /// <summary>
        /// Manages a loan (e.g. accept, liquidate).
        /// </summary>
        /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
        LoanManage,

        /// <summary>
        /// Makes a payment on a loan.
        /// </summary>
        /// <remarks>Requires the Loan amendment (XLS-66d). This feature is in draft and subject to change.</remarks>
        LoanPay,

        /// <summary>
        /// Grants permissions to another account to send transactions on your behalf.
        /// </summary>
        DelegateSet,

        /// <summary>
        /// An administrative pseudo-transaction that fixes ledger state inconsistencies.
        /// </summary>
        LedgerStateFix,
    }
}

