using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

namespace XrplTests.Xrpl.ClientLib.Integration;

public abstract class TestILoanBase
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

    protected static string GetCreatedObjectId(TransactionSummary result, LedgerEntryType entryType = LedgerEntryType.Unknown)
    {
        if (result.Meta?.AffectedNodes == null) return null;

        foreach (AffectedNode node in result.Meta.AffectedNodes)
        {
            if (node.CreatedNode != null)
            {
                if (entryType == LedgerEntryType.Unknown || node.CreatedNode.LedgerEntryType == entryType)
                    return node.CreatedNode.LedgerIndex;
            }
        }
        return null;
    }

    protected static async Task<IXrplClient> CreateStandaloneClient()
    {
        return await IntegrationTestConfig.CreateClientAsync(TestNodeType.Standalone);
    }
}
