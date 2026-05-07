using System;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.ClientLib.Integration;

public abstract class TestIMPTokenBase
{
    public TestContext TestContext { get; set; }
    protected abstract IXrplClient GetClient();
    protected static TestNodeType nodeType = TestNodeType.Standalone;

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

    protected static string GetMPTokenIssuanceIdFromMeta(TransactionSummary result)
    {
        return result.Meta?.MptIssuanceId;
    }

    protected static MPTokenFlags? GetMPTokenFlagsFromMeta(TransactionSummary result)
    {
        try
        {
            if (result.Meta?.AffectedNodes != null)
            {
                foreach (var node in result.Meta.AffectedNodes)
                {
                    if (node.ModifiedNode is { LedgerEntryType: LedgerEntryType.MPToken, FinalFields: LOMPToken { Flags: not null } finalFields })
                    {
                        return finalFields.Flags;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    protected static async Task<IXrplClient> CreateStandaloneClient()
    {
        return await IntegrationTestConfig.CreateClientAsync(TestNodeType.Standalone);
    }
}
