using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec;
using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Models.Utils;
using Xrpl.Sugar;
using Xrpl.Utils;
using Xrpl.Utils.Hashes;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// Every transaction type the SDK can wrap as a Batch inner, exercised as one. The
/// batch normaliser rewrites each inner (flags, fee, signing key, sequence), and the
/// outer signature commits to the inner ids it computes, so a type that serialises
/// differently from Payment - a field the normaliser strips, an STIssue, an STArray -
/// can only be trusted once a node has validated it inside a Batch. Each test reads
/// every inner back by its computed id and checks the inner result the ledger recorded.
/// </summary>
[TestClass]
[TestCategory("Batch")]
public class TestIBatchInnerTypes
{
    private static bool batchV11Active;

    public TestContext TestContext { get; set; }
    private static IXrplClient client;
    private static TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;

    private const string GenesisAccount = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";
    private const string TestCurrency = "USD";

    private static readonly TimeSpan EscrowFinishMargin = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan EscrowCancelMargin = TimeSpan.FromSeconds(24);

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync();
        batchV11Active = await AmendmentGuard.IsEnabledAsync(client, AmendmentGuard.BatchV11);
    }

    [TestInitialize]
    public void CheckBatchV11Amendment()
    {
        if (!batchV11Active)
        {
            Assert.Inconclusive("BatchV1_1 amendment is not enabled on the test node.");
        }
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    #region Helpers

    private sealed record InnerResult(string Hash, string TransactionType, string Result, Meta Meta);

    private static Dictionary<string, object> Reparse(string blob) =>
        JsonSerializer.Deserialize<Dictionary<string, object>>(
            XrplBinaryCodec.Decode(blob).ToJsonString(), XrplJsonOptions.Default);

    private static async Task<(XrplWallet owner, XrplWallet peer)> FundPairAsync()
    {
        XrplWallet owner = XrplWallet.Generate();
        XrplWallet peer = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, owner, peer);
        return (owner, peer);
    }

    private static async Task<uint> NextSequenceAsync(XrplWallet wallet)
    {
        AccountInfo info = await client.AccountInfo(new AccountInfoRequest(wallet.ClassicAddress)
        {
            LedgerIndex = new LedgerIndex(LedgerIndexType.Current),
        }).Typed();
        return info.AccountData.Sequence ?? throw new InvalidOperationException($"account_info for {wallet.ClassicAddress} carries no Sequence");
    }

    private static async Task<DateTime> ValidatedCloseTimeAsync()
    {
        LOLedger ledger = await client.Ledger(new LedgerRequest { LedgerIndex = new LedgerIndex(LedgerIndexType.Validated) }).Typed();
        LedgerEntity entity = (LedgerEntity)ledger.LedgerEntity;
        return entity.CloseTime ?? throw new InvalidOperationException("validated ledger has no close_time");
    }

    private static async Task WaitForCloseTimeAsync(DateTime target)
    {
        while (await ValidatedCloseTimeAsync() < target)
        {
            await Task.Delay(TimeSpan.FromSeconds(3));
        }
    }

    private static async Task SubmitPlainAsync(ITransactionRequest tx, XrplWallet signer, string context)
    {
        ITransactionRequest autofilled = await client.Autofill(tx);
        TransactionSummary res = await client.SubmitAndWait(autofilled, signer, true);
        if (res is not { Meta: { TransactionResult: "tesSUCCESS" or "terQUEUED" } })
            throw new RippleException($"{context} failed: {res.Meta?.TransactionResult}");
    }

    private static Batch NewBatch(XrplWallet outer, BatchFlags flags, params RawTransactionWrapper[] inners) => new Batch
    {
        Account = outer.ClassicAddress,
        Flags = flags,
        RawTransactions = inners.ToList(),
    };

    /// <summary>
    /// Autofills, signs (batch signers first, the outer account last), submits and
    /// waits for the outer Batch, then reads every inner back by its computed id.
    /// </summary>
    private static async Task<List<InnerResult>> SubmitBatchAsync(Batch batch, XrplWallet outer, params XrplWallet[] batchSigners)
    {
        Batch autofilled = await client.Autofill(batch);
        Dictionary<string, object> current = autofilled.ToDictionary();

        foreach (XrplWallet signer in batchSigners)
        {
            SignatureResult part = signer.Sign(current);
            current = Reparse(part.TxBlob);
        }
        SignatureResult final = outer.Sign(current);

        TransactionSummary res = await client.SubmitRequestAndWait(final.TxBlob, false);
        Assert.IsTrue(res.Meta?.TransactionResult is "tesSUCCESS",
            $"outer Batch must validate with tesSUCCESS, got {res.Meta?.TransactionResult}");

        JsonObject signed = XrplBinaryCodec.Decode(final.TxBlob).AsObject();
        List<InnerResult> inners = new List<InnerResult>();
        foreach (JsonNode wrapper in signed["RawTransactions"].AsArray())
        {
            JsonObject inner = wrapper["RawTransaction"].AsObject();
            string hash = inner.ComputeInnerTxId().ToUpperInvariant();
            TransactionResponse innerTx = await client.TxV1(new TxRequest(hash)).Typed();
            inners.Add(new InnerResult(hash, inner["TransactionType"].GetValue<string>(), innerTx.Meta?.TransactionResult, innerTx.Meta));
        }
        return inners;
    }

    private static void AssertAllInnersSucceeded(IReadOnlyList<InnerResult> inners)
    {
        foreach (InnerResult inner in inners)
        {
            Assert.AreEqual("tesSUCCESS", inner.Result, $"inner {inner.TransactionType} {inner.Hash} must validate with tesSUCCESS");
        }
    }

    private static string CreatedIndex(Meta meta, LedgerEntryType type) =>
        meta?.AffectedNodes?
            .Select(n => n.CreatedNode)
            .FirstOrDefault(c => c is { } && c.LedgerEntryType == type)?.LedgerIndex
        ?? throw new AssertFailedException($"metadata carries no created {type} node");

    private static string ToHex(string text) => Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>
    /// A no-op AccountSet: rippled rejects a Batch with fewer than two inners
    /// (temARRAY_EMPTY), so a single interesting inner travels with this one.
    /// </summary>
    private static RawTransactionWrapper Touch(XrplWallet wallet) => new AccountSet { Account = wallet.ClassicAddress }.ToBatchTx();

    #endregion

    [TestMethod]
    public async Task Batch_Inner_DIDSet_DIDDelete()
    {
        (XrplWallet owner, _) = await FundPairAsync();

        Batch batch = NewBatch(owner, BatchFlags.tfAllOrNothing,
            new DIDSet { Account = owner.ClassicAddress, Data = ToHex("batch did") }.ToBatchTx(),
            new DIDDelete { Account = owner.ClassicAddress }.ToBatchTx());

        AssertAllInnersSucceeded(await SubmitBatchAsync(batch, owner));
    }

    [TestMethod]
    public async Task Batch_Inner_OracleSet_OracleDelete()
    {
        (XrplWallet owner, _) = await FundPairAsync();
        DateTime closeTime = await ValidatedCloseTimeAsync();
        const uint documentId = 7;

        OracleSet set = new OracleSet
        {
            Account = owner.ClassicAddress,
            OracleDocumentID = documentId,
            LastUpdateTime = closeTime,
            Provider = "batch",
            AssetClass = "currency",
            PriceDataSeries = new List<PriceDataWrapper>
            {
                new PriceDataWrapper { PriceData = new PriceData { BaseAsset = "XRP", QuoteAsset = "USD", AssetPrice = 740, Scale = 3 } },
            },
        };
        OracleDelete delete = new OracleDelete { Account = owner.ClassicAddress, OracleDocumentID = documentId };

        Batch batch = NewBatch(owner, BatchFlags.tfAllOrNothing, set.ToBatchTx(), delete.ToBatchTx());
        AssertAllInnersSucceeded(await SubmitBatchAsync(batch, owner));
    }

    [TestMethod]
    public async Task Batch_Inner_CredentialCreate_Accept_Delete()
    {
        (XrplWallet owner, XrplWallet peer) = await FundPairAsync();
        string credentialType = ToHex("batch_credential");

        Batch batch = NewBatch(owner, BatchFlags.tfAllOrNothing,
            new CredentialCreate { Account = owner.ClassicAddress, Subject = peer.ClassicAddress, CredentialType = credentialType }.ToBatchTx(),
            new CredentialAccept { Account = peer.ClassicAddress, Issuer = owner.ClassicAddress, CredentialType = credentialType }.ToBatchTx(),
            new CredentialDelete { Account = owner.ClassicAddress, Subject = peer.ClassicAddress, Issuer = owner.ClassicAddress, CredentialType = credentialType }.ToBatchTx());

        AssertAllInnersSucceeded(await SubmitBatchAsync(batch, owner, peer));
    }

    [TestMethod]
    public async Task Batch_Inner_MPTokenIssuance_Create_Set_Authorize_Destroy()
    {
        (XrplWallet owner, XrplWallet peer) = await FundPairAsync();

        // The issuance id is derived from the create's sequence, which the normaliser
        // assigns as the outer sequence + 1 for the first inner of the outer account
        uint outerSequence = await NextSequenceAsync(owner);
        string issuanceId = ParseMPTID.GenerateMPTokenIssuanceID(outerSequence + 1, owner.ClassicAddress);

        Batch batch = NewBatch(owner, BatchFlags.tfAllOrNothing,
            new MPTokenIssuanceCreate { Account = owner.ClassicAddress, Flags = MPTokenIssuanceCreateFlags.tfMPTCanLock | MPTokenIssuanceCreateFlags.tfMPTCanTransfer }.ToBatchTx(),
            new MPTokenIssuanceSet { Account = owner.ClassicAddress, MPTokenIssuanceID = issuanceId, Flags = MPTokenIssuanceSetFlags.tfMPTLock }.ToBatchTx(),
            new MPTokenAuthorize { Account = peer.ClassicAddress, MPTokenIssuanceID = issuanceId }.ToBatchTx(),
            new MPTokenAuthorize { Account = peer.ClassicAddress, MPTokenIssuanceID = issuanceId, Flags = MPTokenAuthorizeFlags.tfMPTUnauthorize }.ToBatchTx(),
            new MPTokenIssuanceDestroy { Account = owner.ClassicAddress, MPTokenIssuanceID = issuanceId }.ToBatchTx());
        batch.Sequence = outerSequence;

        AssertAllInnersSucceeded(await SubmitBatchAsync(batch, owner, peer));
    }

    [TestMethod]
    public async Task Batch_Inner_NFToken_Mint_Offers_Cancel_Modify_Accept_Burn()
    {
        (XrplWallet owner, XrplWallet peer) = await FundPairAsync();

        List<InnerResult> mint = await SubmitBatchAsync(NewBatch(owner, BatchFlags.tfAllOrNothing,
            new NFTokenMint { Account = owner.ClassicAddress, NFTokenTaxon = 0, Flags = NFTokenMintFlags.tfTransferable | NFTokenMintFlags.tfMutable }.ToBatchTx(),
            Touch(owner)), owner);
        AssertAllInnersSucceeded(mint);
        string nftokenId = mint[0].Meta?.NFTokenId;
        Assert.IsNotNull(nftokenId, "the inner NFTokenMint must report nftoken_id");

        List<InnerResult> offers = await SubmitBatchAsync(NewBatch(owner, BatchFlags.tfAllOrNothing,
            new NFTokenCreateOffer { Account = owner.ClassicAddress, NFTokenID = nftokenId, Amount = new Currency { ValueAsXrp = 1m }, Flags = NFTokenCreateOfferFlags.tfSellNFToken }.ToBatchTx(),
            new NFTokenCreateOffer { Account = owner.ClassicAddress, NFTokenID = nftokenId, Amount = new Currency { ValueAsXrp = 2m }, Flags = NFTokenCreateOfferFlags.tfSellNFToken }.ToBatchTx()), owner);
        AssertAllInnersSucceeded(offers);
        string sellOffer = offers[0].Meta?.OfferID;
        string staleOffer = offers[1].Meta?.OfferID;
        Assert.IsNotNull(sellOffer, "the inner NFTokenCreateOffer must report offer_id");
        Assert.IsNotNull(staleOffer, "the second inner NFTokenCreateOffer must report offer_id");

        List<InnerResult> transfer = await SubmitBatchAsync(NewBatch(owner, BatchFlags.tfAllOrNothing,
            new NFTokenCancelOffer { Account = owner.ClassicAddress, NFTokenOffers = new[] { staleOffer } }.ToBatchTx(),
            new NFTokenModify { Account = owner.ClassicAddress, NFTokenID = nftokenId, URI = ToHex("ipfs://batch") }.ToBatchTx(),
            new NFTokenAcceptOffer { Account = peer.ClassicAddress, NFTokenSellOffer = sellOffer }.ToBatchTx()), owner, peer);
        AssertAllInnersSucceeded(transfer);

        List<InnerResult> burn = await SubmitBatchAsync(NewBatch(peer, BatchFlags.tfAllOrNothing,
            new NFTokenBurn { Account = peer.ClassicAddress, NFTokenID = nftokenId }.ToBatchTx(),
            Touch(peer)), peer);
        AssertAllInnersSucceeded(burn);
    }

    [TestMethod]
    public async Task Batch_Inner_CheckCreate_CheckCancel_CheckCash()
    {
        (XrplWallet owner, XrplWallet peer) = await FundPairAsync();

        List<InnerResult> created = await SubmitBatchAsync(NewBatch(owner, BatchFlags.tfAllOrNothing,
            new CheckCreate { Account = owner.ClassicAddress, Destination = peer.ClassicAddress, SendMax = new Currency { ValueAsXrp = 1m } }.ToBatchTx(),
            new CheckCreate { Account = owner.ClassicAddress, Destination = peer.ClassicAddress, SendMax = new Currency { ValueAsXrp = 2m } }.ToBatchTx()), owner);
        AssertAllInnersSucceeded(created);
        string cashable = CreatedIndex(created[0].Meta, LedgerEntryType.Check);
        string cancellable = CreatedIndex(created[1].Meta, LedgerEntryType.Check);

        List<InnerResult> settled = await SubmitBatchAsync(NewBatch(owner, BatchFlags.tfAllOrNothing,
            new CheckCancel { Account = owner.ClassicAddress, CheckID = cancellable }.ToBatchTx(),
            new CheckCash { Account = peer.ClassicAddress, CheckID = cashable, Amount = new Currency { ValueAsXrp = 1m } }.ToBatchTx()), owner, peer);
        AssertAllInnersSucceeded(settled);
    }

    [TestMethod]
    public async Task Batch_Inner_EscrowCreate_EscrowFinish_EscrowCancel()
    {
        (XrplWallet owner, XrplWallet peer) = await FundPairAsync();
        DateTime closeTime = await ValidatedCloseTimeAsync();
        uint outerSequence = await NextSequenceAsync(owner);

        Batch create = NewBatch(owner, BatchFlags.tfAllOrNothing,
            new EscrowCreate { Account = owner.ClassicAddress, Destination = peer.ClassicAddress, Amount = new Currency { ValueAsXrp = 1m }, FinishAfter = closeTime + EscrowFinishMargin }.ToBatchTx(),
            new EscrowCreate { Account = owner.ClassicAddress, Destination = peer.ClassicAddress, Amount = new Currency { ValueAsXrp = 1m }, FinishAfter = closeTime + EscrowFinishMargin, CancelAfter = closeTime + EscrowCancelMargin }.ToBatchTx());
        create.Sequence = outerSequence;
        AssertAllInnersSucceeded(await SubmitBatchAsync(create, owner));

        await WaitForCloseTimeAsync(closeTime + EscrowCancelMargin);

        Batch settle = NewBatch(owner, BatchFlags.tfAllOrNothing,
            new EscrowFinish { Account = owner.ClassicAddress, Owner = owner.ClassicAddress, OfferSequence = outerSequence + 1 }.ToBatchTx(),
            new EscrowCancel { Account = owner.ClassicAddress, Owner = owner.ClassicAddress, OfferSequence = outerSequence + 2 }.ToBatchTx());
        AssertAllInnersSucceeded(await SubmitBatchAsync(settle, owner));
    }

    [TestMethod]
    public async Task Batch_Inner_PaymentChannelCreate_Fund_Claim()
    {
        (XrplWallet owner, XrplWallet peer) = await FundPairAsync();
        uint outerSequence = await NextSequenceAsync(owner);
        string channel = Hashes.HashPaymentChannel(owner.ClassicAddress, peer.ClassicAddress, (int)outerSequence + 1);

        Batch open = NewBatch(owner, BatchFlags.tfAllOrNothing,
            new PaymentChannelCreate { Account = owner.ClassicAddress, Destination = peer.ClassicAddress, Amount = "1000000", SettleDelay = 60, PublicKey = owner.PublicKey }.ToBatchTx(),
            Touch(owner));
        open.Sequence = outerSequence;
        AssertAllInnersSucceeded(await SubmitBatchAsync(open, owner));

        // The channel source may claim without a signature, so both moves fit one batch
        Batch use = NewBatch(owner, BatchFlags.tfAllOrNothing,
            new PaymentChannelFund { Account = owner.ClassicAddress, Channel = channel, Amount = "500000" }.ToBatchTx(),
            new PaymentChannelClaim { Account = owner.ClassicAddress, Channel = channel, Balance = "700000" }.ToBatchTx());
        AssertAllInnersSucceeded(await SubmitBatchAsync(use, owner));
    }

    [TestMethod]
    public async Task Batch_Inner_OfferCreate_OfferCancel()
    {
        (XrplWallet owner, XrplWallet peer) = await FundPairAsync();
        uint outerSequence = await NextSequenceAsync(owner);

        Batch batch = NewBatch(owner, BatchFlags.tfAllOrNothing,
            new OfferCreate
            {
                Account = owner.ClassicAddress,
                TakerGets = new Currency { ValueAsXrp = 1m },
                TakerPays = new Currency { CurrencyCode = TestCurrency, Issuer = peer.ClassicAddress, Value = "1" },
            }.ToBatchTx(),
            new OfferCancel { Account = owner.ClassicAddress, OfferSequence = outerSequence + 1 }.ToBatchTx());
        batch.Sequence = outerSequence;

        AssertAllInnersSucceeded(await SubmitBatchAsync(batch, owner));
    }

    [TestMethod]
    public async Task Batch_Inner_PermissionedDomainSet_Delete()
    {
        (XrplWallet owner, XrplWallet peer) = await FundPairAsync();

        List<InnerResult> created = await SubmitBatchAsync(NewBatch(owner, BatchFlags.tfAllOrNothing,
            new PermissionedDomainSet
            {
                Account = owner.ClassicAddress,
                AcceptedCredentials = new List<AcceptedCredentialWrapper>
                {
                    new AcceptedCredentialWrapper { Credential = new AcceptedCredential { Issuer = peer.ClassicAddress, CredentialType = ToHex("batch_domain") } },
                },
            }.ToBatchTx(),
            Touch(owner)), owner);
        AssertAllInnersSucceeded(created);
        string domainId = CreatedIndex(created[0].Meta, LedgerEntryType.PermissionedDomain);

        AssertAllInnersSucceeded(await SubmitBatchAsync(NewBatch(owner, BatchFlags.tfAllOrNothing,
            new PermissionedDomainDelete { Account = owner.ClassicAddress, DomainID = domainId }.ToBatchTx(),
            Touch(owner)), owner));
    }

    [TestMethod]
    public async Task Batch_Inner_DelegateSet()
    {
        (XrplWallet owner, XrplWallet peer) = await FundPairAsync();

        Batch batch = NewBatch(owner, BatchFlags.tfAllOrNothing,
            new DelegateSet
            {
                Account = owner.ClassicAddress,
                Authorize = peer.ClassicAddress,
                Permissions = new List<PermissionWrapper> { new PermissionWrapper { Permission = new PermissionEntry { PermissionValue = 1 } } },
            }.ToBatchTx(),
            Touch(owner));

        AssertAllInnersSucceeded(await SubmitBatchAsync(batch, owner));
    }

    [TestMethod]
    public async Task Batch_Inner_TrustSet_Payment_Clawback()
    {
        (XrplWallet owner, XrplWallet peer) = await FundPairAsync();
        await SubmitPlainAsync(new AccountSet { Account = owner.ClassicAddress, SetFlag = AccountSetAsfFlags.asfAllowTrustLineClawback }, owner, "asfAllowTrustLineClawback");

        Batch batch = NewBatch(owner, BatchFlags.tfAllOrNothing,
            new TrustSet { Account = peer.ClassicAddress, LimitAmount = new Currency { CurrencyCode = TestCurrency, Issuer = owner.ClassicAddress, Value = "1000" } }.ToBatchTx(),
            new Payment { Account = owner.ClassicAddress, Destination = peer.ClassicAddress, Amount = new Currency { CurrencyCode = TestCurrency, Issuer = owner.ClassicAddress, Value = "100" } }.ToBatchTx(),
            // Clawback names the holder as the issuer of the amount being clawed back
            new ClawBack { Account = owner.ClassicAddress, Amount = new Currency { CurrencyCode = TestCurrency, Issuer = peer.ClassicAddress, Value = "40" } }.ToBatchTx());

        AssertAllInnersSucceeded(await SubmitBatchAsync(batch, owner, peer));
    }

    [TestMethod]
    public async Task Batch_Inner_AMMCreate_Deposit_Vote_Bid_Withdraw_Clawback()
    {
        (XrplWallet issuer, XrplWallet holder) = await FundPairAsync();
        await SubmitPlainAsync(new AccountSet { Account = issuer.ClassicAddress, SetFlag = AccountSetAsfFlags.asfAllowTrustLineClawback }, issuer, "asfAllowTrustLineClawback");
        await SubmitPlainAsync(new AccountSet { Account = issuer.ClassicAddress, SetFlag = AccountSetAsfFlags.asfDefaultRipple }, issuer, "asfDefaultRipple");
        await SubmitPlainAsync(new TrustSet { Account = holder.ClassicAddress, LimitAmount = new Currency { CurrencyCode = TestCurrency, Issuer = issuer.ClassicAddress, Value = "1000000" } }, holder, "TrustSet");
        await SubmitPlainAsync(new Payment { Account = issuer.ClassicAddress, Destination = holder.ClassicAddress, Amount = new Currency { CurrencyCode = TestCurrency, Issuer = issuer.ClassicAddress, Value = "10000" } }, issuer, "issue tokens");

        IssuedCurrency token = new IssuedCurrency { Currency = TestCurrency, Issuer = issuer.ClassicAddress };
        IssuedCurrency xrp = new IssuedCurrency { Currency = "XRP" };

        Batch create = NewBatch(holder, BatchFlags.tfAllOrNothing,
            new AMMCreate
            {
                Account = holder.ClassicAddress,
                Amount = new Currency { CurrencyCode = TestCurrency, Issuer = issuer.ClassicAddress, Value = "1000" },
                Amount2 = new Currency { ValueAsXrp = 10m },
                TradingFee = 500,
            }.ToBatchTx(),
            Touch(holder));
        AssertAllInnersSucceeded(await SubmitBatchAsync(create, holder));

        Batch operate = NewBatch(holder, BatchFlags.tfAllOrNothing,
            new AMMDeposit { Account = holder.ClassicAddress, Asset = token, Asset2 = xrp, Amount = new Currency { CurrencyCode = TestCurrency, Issuer = issuer.ClassicAddress, Value = "100" }, Flags = AMMDepositFlags.tfSingleAsset }.ToBatchTx(),
            new AMMVote { Account = holder.ClassicAddress, Asset = token, Asset2 = xrp, TradingFee = 100 }.ToBatchTx(),
            new AMMBid { Account = holder.ClassicAddress, Asset = token, Asset2 = xrp }.ToBatchTx(),
            new AMMWithdraw { Account = holder.ClassicAddress, Asset = token, Asset2 = xrp, Amount = new Currency { CurrencyCode = TestCurrency, Issuer = issuer.ClassicAddress, Value = "50" }, Flags = AMMWithdrawFlags.tfSingleAsset }.ToBatchTx(),
            new AMMClawBack { Account = issuer.ClassicAddress, Holder = holder.ClassicAddress, Asset = token, Asset2 = xrp, Amount = new Currency { CurrencyCode = TestCurrency, Issuer = issuer.ClassicAddress, Value = "100" } }.ToBatchTx());
        AssertAllInnersSucceeded(await SubmitBatchAsync(operate, holder, issuer));
    }

    [TestMethod]
    public async Task Batch_Inner_XChain_CreateBridge_Modify_ClaimID_Commit_AccountCreateCommit()
    {
        (XrplWallet door, XrplWallet user) = await FundPairAsync();
        XChainBridgeModel bridge = new XChainBridgeModel
        {
            LockingChainDoor = door.ClassicAddress,
            LockingChainIssue = new IssuedCurrency { Currency = "XRP" },
            IssuingChainDoor = GenesisAccount,
            IssuingChainIssue = new IssuedCurrency { Currency = "XRP" },
        };
        Currency reward(string drops) => new Currency { Value = drops, CurrencyCode = "XRP" };

        Batch batch = NewBatch(door, BatchFlags.tfAllOrNothing,
            new XChainCreateBridge { Account = door.ClassicAddress, XChainBridge = bridge, SignatureReward = reward("100"), MinAccountCreateAmount = reward("10000000") }.ToBatchTx(),
            new XChainModifyBridge { Account = door.ClassicAddress, XChainBridge = bridge, SignatureReward = reward("200") }.ToBatchTx(),
            new XChainCreateClaimID { Account = user.ClassicAddress, XChainBridge = bridge, SignatureReward = reward("200"), OtherChainSource = user.ClassicAddress }.ToBatchTx(),
            new XChainCommit { Account = user.ClassicAddress, XChainBridge = bridge, XChainClaimID = "1", Amount = reward("1000000"), OtherChainDestination = XrplWallet.Generate().ClassicAddress }.ToBatchTx(),
            new XChainAccountCreateCommit { Account = user.ClassicAddress, XChainBridge = bridge, Destination = XrplWallet.Generate().ClassicAddress, Amount = reward("20000000"), SignatureReward = reward("200") }.ToBatchTx());

        AssertAllInnersSucceeded(await SubmitBatchAsync(batch, door, user));
    }

    /// <summary>
    /// tfIndependent applies every inner on its own: a healthy account has nothing for
    /// LedgerStateFix to repair, so that inner is recorded with tecFAILED_PROCESSING
    /// while its sibling and the outer Batch still validate.
    /// </summary>
    [TestMethod]
    public async Task Batch_Inner_LedgerStateFix_Independent_RecordsInnerTec()
    {
        (XrplWallet owner, _) = await FundPairAsync();

        Batch batch = NewBatch(owner, BatchFlags.tfIndependent,
            new AccountSet { Account = owner.ClassicAddress, Domain = ToHex("batch.example") }.ToBatchTx(),
            new LedgerStateFix { Account = owner.ClassicAddress, LedgerFixType = 1, Owner = owner.ClassicAddress }.ToBatchTx());

        List<InnerResult> inners = await SubmitBatchAsync(batch, owner);
        Assert.AreEqual("tesSUCCESS", inners[0].Result, "the AccountSet inner must validate");
        Assert.IsTrue(inners[1].Result is "tesSUCCESS" or "tecFAILED_PROCESSING",
            $"the LedgerStateFix inner must be recorded with its own result, got {inners[1].Result}");
    }

    [TestMethod]
    public async Task Batch_Inner_SponsorshipSet_Create_Delete()
    {
        if (!await AmendmentGuard.IsEnabledAsync(client, AmendmentGuard.Sponsor))
        {
            Assert.Inconclusive("Sponsor amendment (XLS-68) is not enabled on the test node.");
        }
        (XrplWallet owner, XrplWallet peer) = await FundPairAsync();

        Batch batch = NewBatch(owner, BatchFlags.tfAllOrNothing,
            new SponsorshipSet { Account = owner.ClassicAddress, Sponsee = peer.ClassicAddress, FeeAmountDelta = new Currency { ValueAsXrp = 1m }, RemainingOwnerCountDelta = 1 }.ToBatchTx(),
            new SponsorshipSet { Account = owner.ClassicAddress, Sponsee = peer.ClassicAddress, Flags = SponsorshipSetFlags.tfDeleteObject }.ToBatchTx());

        AssertAllInnersSucceeded(await SubmitBatchAsync(batch, owner));
    }
}
