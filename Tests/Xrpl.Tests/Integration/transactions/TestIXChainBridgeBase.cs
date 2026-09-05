using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

using static Xrpl.Models.Common.Common;

namespace XrplTests.Xrpl.ClientLib.Integration;

public abstract class TestIXChainBridgeBase
{
    public TestContext TestContext { get; set; }
    protected abstract IXrplClient GetClient();
    protected static TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;

    /// <summary>
    /// Genesis account address — required as IssuingChainDoor for XRP-XRP bridges in standalone mode.
    /// </summary>
    protected const string GenesisAccount = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh";

    /// <summary>
    /// Default IOU currency code for bridge tests.
    /// </summary>
    protected const string TestCurrencyCode = "USD";

    protected static void ValidateResult(Submit res)
    {
        if (res is not { EngineResult: "tesSUCCESS" or "terQUEUED" })
            throw new RippleException($"Transaction failed: {res.EngineResult}");
    }

    protected static void ValidateResult(TransactionSummary res)
    {
        if (res is not { Meta: { TransactionResult: "tesSUCCESS" or "terQUEUED" } })
            throw new RippleException($"Transaction failed: {res.Meta?.TransactionResult}");
    }

    /// <summary>
    /// Creates an XRP-XRP bridge definition for standalone testing.
    /// IssuingChainDoor must be the genesis account for XRP bridges.
    /// </summary>
    protected static XChainBridgeModel CreateXrpTestBridge(string lockingDoor)
    {
        return new XChainBridgeModel
        {
            LockingChainDoor = lockingDoor,
            LockingChainIssue = new IssuedCurrency { Currency = "XRP" },
            IssuingChainDoor = GenesisAccount,
            IssuingChainIssue = new IssuedCurrency { Currency = "XRP" },
        };
    }

    /// <summary>
    /// Creates an IOU-IOU bridge definition for standalone testing.
    /// On the locking side, door and issuer can be different accounts.
    /// On the issuing side, the door account MUST be the token issuer
    /// (IssuingChainDoor == IssuingChainIssue.issuer).
    /// </summary>
    protected static XChainBridgeModel CreateIouTestBridge(
        string lockingDoor, string lockingIssuer,
        string issuingDoor,
        string currencyCode = TestCurrencyCode)
    {
        return new XChainBridgeModel
        {
            LockingChainDoor = lockingDoor,
            LockingChainIssue = new IssuedCurrency { Currency = currencyCode, Issuer = lockingIssuer },
            IssuingChainDoor = issuingDoor,
            IssuingChainIssue = new IssuedCurrency { Currency = currencyCode, Issuer = issuingDoor },
        };
    }

    /// <summary>
    /// Sets up a TrustLine from <paramref name="holder"/> to <paramref name="issuer"/> for the given currency.
    /// </summary>
    protected static async Task SetupTrustLine(
        IXrplClient client, XrplWallet holder, string issuer,
        string currencyCode = TestCurrencyCode, string limit = "10000000")
    {
        TrustSet trustSet = new TrustSet
        {
            Account = holder.ClassicAddress,
            LimitAmount = new Currency
            {
                CurrencyCode = currencyCode,
                Issuer = issuer,
                Value = limit,
            }
        };
        trustSet = await client.Autofill(trustSet);
        TransactionSummary res = await client.SubmitAndWait(trustSet, holder, true);
        ValidateResult(res);
    }

    /// <summary>
    /// Enables the DefaultRipple flag on an issuer account.
    /// Required for IOU transfers between third-party accounts through the issuer.
    /// Must be called BEFORE creating TrustLines.
    /// </summary>
    protected static async Task EnableDefaultRipple(IXrplClient client, XrplWallet issuer)
    {
        AccountSet accountSet = new AccountSet
        {
            Account = issuer.ClassicAddress,
            SetFlag = AccountSetAsfFlags.asfDefaultRipple,
        };
        accountSet = await client.Autofill(accountSet);
        TransactionSummary res = await client.SubmitAndWait(accountSet, issuer, true);
        ValidateResult(res);
    }

    protected static async Task<IXrplClient> CreateStandaloneClient()
    {
        return await IntegrationTestConfig.CreateClientAsync();
    }

    protected static Currency Drops(string drops) => new Currency { Value = drops, CurrencyCode = "XRP" };

    protected static Currency Iou(string issuer, string value) => new Currency { CurrencyCode = TestCurrencyCode, Issuer = issuer, Value = value };

    /// <summary>Submits a setup transaction and fails loudly if the ledger refuses it.</summary>
    protected static async Task SubmitAsync(IXrplClient client, ITransactionRequest tx, XrplWallet signer)
    {
        ITransactionRequest autofilled = await client.Autofill(tx);
        ValidateResult(await client.SubmitAndWait(autofilled, signer, true));
    }

    /// <summary>
    /// Makes <paramref name="witnesses"/> the door's signer list, which is where rippled looks to
    /// decide whether an attestation counts and how many are needed.
    /// </summary>
    protected static async Task SetWitnessesAsync(IXrplClient client, XrplWallet door, uint quorum, params XrplWallet[] witnesses)
    {
        await SubmitAsync(client, new SignerListSet
        {
            Account = door.ClassicAddress,
            SignerQuorum = quorum,
            SignerEntries = witnesses
                .Select(w => new SignerEntryWrapper { SignerEntry = new SignerEntry { Account = w.ClassicAddress, SignerWeight = 1 } })
                .ToList(),
        }, door);
    }

    /// <summary>What <paramref name="holder"/> holds of the issuer's currency, zero if no line exists.</summary>
    protected static async Task<decimal> IouBalanceAsync(IXrplClient client, string holder, string issuer)
    {
        AccountLines lines = await client.AccountLines(new AccountLinesRequest(holder)).Typed();
        TrustLine line = lines.TrustLines?.FirstOrDefault(l => l.Account == issuer && l.Currency == TestCurrencyCode);
        return line?.BalanceAsNumber ?? 0m;
    }

    /// <summary>How many objects of one type the account owns, for asserting a claim id came or went.</summary>
    protected static async Task<int> CountObjectsAsync(IXrplClient client, string account, LedgerEntryType type)
    {
        AccountObjects objects = await client.AccountObjects(new AccountObjectsRequest(account) { Type = type }).Typed();
        return objects.AccountObjectList?.Count ?? 0;
    }

    /// <summary>The accounts and bridge spec behind a commit that is waiting for attestations.</summary>
    protected sealed record IouBridge(
        XrplWallet Door, XrplWallet Issuer, IReadOnlyList<XrplWallet> Witnesses,
        XrplWallet User, XrplWallet Recipient, XChainBridgeModel Bridge)
    {
        public XrplWallet Witness => Witnesses[0];
    }

    /// <summary>
    /// Locking door, IOU issuer, <paramref name="witnessCount"/> witnesses on a quorum of
    /// <paramref name="quorum"/>, a user holding 1000 USD and a recipient with a trust line;
    /// bridge created on the door; claim id 1 created by the recipient; 100 USD committed by
    /// the user. What is left is the attestation.
    /// </summary>
    protected static async Task<IouBridge> CommitOnIouBridgeAsync(
        IXrplClient client, uint quorum = 1, int witnessCount = 1)
    {
        XrplWallet door = XrplWallet.Generate();
        XrplWallet issuer = XrplWallet.Generate();
        XrplWallet[] witnesses = Enumerable.Range(0, witnessCount).Select(_ => XrplWallet.Generate()).ToArray();
        XrplWallet user = XrplWallet.Generate();
        XrplWallet recipient = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, new[] { door, issuer, user, recipient }.Concat(witnesses).ToArray());

        await EnableDefaultRipple(client, issuer);
        await SetupTrustLine(client, door, issuer.ClassicAddress);
        await SetupTrustLine(client, user, issuer.ClassicAddress);
        await SetupTrustLine(client, recipient, issuer.ClassicAddress);
        await SubmitAsync(client, new Payment { Account = issuer.ClassicAddress, Destination = user.ClassicAddress, Amount = Iou(issuer.ClassicAddress, "1000") }, issuer);

        // The issuing door only has to be an address in the spec: nothing on this ledger looks it up
        XChainBridgeModel bridge = CreateIouTestBridge(door.ClassicAddress, issuer.ClassicAddress, XrplWallet.Generate().ClassicAddress);
        await SubmitAsync(client, new XChainCreateBridge { Account = door.ClassicAddress, XChainBridge = bridge, SignatureReward = Drops("100") }, door);
        await SetWitnessesAsync(client, door, quorum, witnesses);

        await SubmitAsync(client, new XChainCreateClaimID { Account = recipient.ClassicAddress, XChainBridge = bridge, SignatureReward = Drops("100"), OtherChainSource = user.ClassicAddress }, recipient);
        await SubmitAsync(client, new XChainCommit { Account = user.ClassicAddress, XChainBridge = bridge, XChainClaimID = "1", Amount = Iou(issuer.ClassicAddress, "100"), OtherChainDestination = recipient.ClassicAddress }, user);

        return new IouBridge(door, issuer, witnesses, user, recipient, bridge);
    }

    /// <summary>
    /// The unsigned attestation for the commit made in <see cref="CommitOnIouBridgeAsync"/>. A null
    /// <paramref name="destination"/> leaves the funds for an explicit XChainClaim instead of
    /// having them delivered when quorum is reached.
    /// </summary>
    protected static XChainAddClaimAttestation ClaimAttestation(IouBridge setup, string destination, XrplWallet witness = null)
    {
        XrplWallet attester = witness ?? setup.Witness;
        return new XChainAddClaimAttestation
        {
            Account = attester.ClassicAddress,
            XChainBridge = setup.Bridge,
            OtherChainSource = setup.User.ClassicAddress,
            // The attested send is on the issuing chain, so the amount carries the issuing chain issue
            Amount = new Currency { CurrencyCode = TestCurrencyCode, Issuer = setup.Bridge.IssuingChainDoor, Value = "100" },
            AttestationRewardAccount = attester.ClassicAddress,
            Destination = destination,
            WasLockingChainSend = 0,
            XChainClaimID = "1",
        };
    }
}
