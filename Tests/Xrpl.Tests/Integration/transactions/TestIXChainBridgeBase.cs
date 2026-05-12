using System;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
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
    protected static TestNodeType nodeType = TestNodeType.Standalone;

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
        return await IntegrationTestConfig.CreateClientAsync(TestNodeType.Standalone);
    }
}
