using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Sugar;

using Xrpl.Tests;

namespace XrplTests.Xrpl.Sugar;

/// <summary>
/// The sugar helpers that read a ledger take <c>LOLedger.LedgerEntity</c>, which is interface
/// typed, and used to cast it outright.
/// </summary>
/// <remarks>
/// That cast has two failure modes and neither surfaced as a protocol error: a response with no
/// <c>ledger</c> member casts to <see langword="null"/> and faults on the next dereference, while
/// a binary response deserializes to <c>LedgerBinaryEntity</c> and throws
/// <see cref="System.InvalidCastException"/>. Both mean the same thing to a caller - the node did
/// not return the shape asked for - so both should say so.
/// </remarks>
[TestClass]
public class TestULedgerShapeGuards
{
    private const string LedgerReplyWithoutLedgerMember =
        "{\"id\":__ID__,\"status\":\"success\",\"type\":\"response\",\"api_version\":2,"
        + "\"result\":{\"ledger_current_index\":106359163,\"validated\":false}}";

    private const string BinaryLedgerReply =
        "{\"id\":__ID__,\"status\":\"success\",\"type\":\"response\",\"api_version\":2,"
        + "\"result\":{\"ledger\":{\"ledger_data\":\"01AB\",\"transactions\":[]},"
        + "\"ledger_index\":106359163,\"validated\":true}}";

    private static async Task<ValidationException> Failure(string scriptedResponse)
    {
        using ScriptedResponseServer server = new ScriptedResponseServer(scriptedResponse);
        using XrplClient client = new XrplClient(server.Url, new XrplClient.ClientOptions { ApiVersion = 2 });
        await client.Connect();

        try
        {
            return await Assert.ThrowsExactlyAsync<ValidationException>(() => client.GetLedgerIndex());
        }
        finally
        {
            await client.Disconnect();
        }
    }

    /// <summary>A reply carrying no <c>ledger</c> member fails as a protocol error, not an NRE.</summary>
    [TestMethod]
    public async Task TestUGetLedgerIndexRejectsAReplyWithoutALedgerObject()
    {
        ValidationException failure = await Failure(LedgerReplyWithoutLedgerMember);

        StringAssert.Contains(failure.Message, "did not include a JSON ledger object");
    }

    /// <summary>
    /// A binary reply deserializes to a different concrete type; the caller is told which, since
    /// the fix is on their side - drop <c>binary</c> from the request.
    /// </summary>
    [TestMethod]
    public async Task TestUGetLedgerIndexRejectsABinaryLedgerObject()
    {
        ValidationException failure = await Failure(BinaryLedgerReply);

        StringAssert.Contains(failure.Message, "LedgerBinaryEntity");
        StringAssert.Contains(failure.Message, "binary");
    }
}
