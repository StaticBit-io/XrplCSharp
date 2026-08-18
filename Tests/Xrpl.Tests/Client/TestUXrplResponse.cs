using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Xrpl.Client;
using Xrpl.Client.Json;
using Xrpl.Models.Ledger;
using Xrpl.Models.Subscriptions;
using Xrpl.Models.Methods;
using Xrpl.Tests;

namespace XrplTests.Client;

/// <summary>
/// The envelope a caller gets back: the typed projection and, beside it, the bytes the node sent.
/// The point of the pair is that the projection cannot be mistaken for the source — re-serializing
/// it drops members the model lacks and invents defaults for non-nullable CLR properties.
/// </summary>
[TestClass]
public class TestUXrplResponse
{
    [TestMethod]
    public void TestUCarriesResultAndRawSideBySide()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"result\":{\"ledger_index\":9,\"marker\":\"AABB\"}}");
        RawJson raw = new RawJson(frame, 10, frame.Length - 11);
        LOLedgerData typed = raw.Deserialize<LOLedgerData>();

        XrplResponse<LOLedgerData> response = new XrplResponse<LOLedgerData>(typed, raw, 2, null, null, null, false);

        Assert.AreSame(typed, response.Result);
        Assert.AreEqual("{\"ledger_index\":9,\"marker\":\"AABB\"}", response.Raw.ToString());
        Assert.AreEqual(2u, response.ApiVersion);
    }

    [TestMethod]
    public void TestUWarningsAreNeverNull()
    {
        XrplResponse<LOLedgerData> response = new XrplResponse<LOLedgerData>(null, default, null, null, null, null, false);

        Assert.IsNotNull(response.Warnings);
        Assert.AreEqual(0, response.Warnings.Count);
    }

    /// <summary>
    /// <c>var (result, raw) = response</c> must hand back exactly what <see cref="XrplResponse{T}.Result"/>
    /// and <see cref="XrplResponse{T}.Raw"/> would — this is the one-line escape hatch for the
    /// <c>var</c> call sites that otherwise have to be restructured for the new return type.
    /// </summary>
    [TestMethod]
    public void TestUDeconstructsIntoResultAndRaw()
    {
        byte[] frame = Encoding.UTF8.GetBytes("{\"result\":{\"ledger_index\":9,\"marker\":\"AABB\"}}");
        RawJson raw = new RawJson(frame, 10, frame.Length - 11);
        LOLedgerData typed = raw.Deserialize<LOLedgerData>();

        XrplResponse<LOLedgerData> response = new XrplResponse<LOLedgerData>(typed, raw, 2, null, null, null, false);

        var (result, deconstructedRaw) = response;

        Assert.AreSame(typed, result);
        Assert.AreEqual("{\"ledger_index\":9,\"marker\":\"AABB\"}", deconstructedRaw.ToString());
    }

    /// <summary>
    /// The wrapper's own paging signal, read off <see cref="XrplResponse{T}.Raw"/> rather than a
    /// parsed projection — the same rule <c>BaseResponse.HasNextPage</c> follows, now reachable from
    /// the type a caller of the client's own methods actually holds.
    /// </summary>
    [TestMethod]
    public void TestUHasNextPageReflectsTheMarker()
    {
        byte[] withMarker = Encoding.UTF8.GetBytes("{\"ledger_index\":9,\"marker\":\"AABB\"}");
        XrplResponse<LOLedgerData> paged = new XrplResponse<LOLedgerData>(
            null, new RawJson(withMarker, 0, withMarker.Length), null, null, null, null, false);
        Assert.IsTrue(paged.HasNextPage);

        byte[] withoutMarker = Encoding.UTF8.GetBytes("{\"ledger_index\":9}");
        XrplResponse<LOLedgerData> lastPage = new XrplResponse<LOLedgerData>(
            null, new RawJson(withoutMarker, 0, withoutMarker.Length), null, null, null, null, false);
        Assert.IsFalse(lastPage.HasNextPage);
    }

    /// <summary>
    /// The whole point of the feature, end to end over a real socket: what the node sent reaches
    /// the caller unchanged. The scripted body carries irregular whitespace and a member no model
    /// knows, so this fails if anything on the path normalizes or reprojects the bytes.
    /// </summary>
    [TestMethod]
    public async Task TestURawSurvivesTheTripFromTheSocket()
    {
        const string Result = "{\"ledger_current_index\" : 96000000,\"a_field_no_model_knows\":[1, 2]}";

        using ScriptedResponseServer server = new ScriptedResponseServer(
            "{\"id\":__ID__,\"status\":\"success\",\"type\":\"response\",\"api_version\":2,"
            + "\"warning\":\"load\",\"forwarded\":true,\"result\":" + Result + "}");
        using XrplClient client = new XrplClient(server.Url, new XrplClient.ClientOptions { ApiVersion = 2 });
        await client.Connect();

        XrplResponse<LOLedgerCurrentIndex> response =
            await client.LedgerCurrent(new LedgerCurrentRequest());

        // Byte for byte, whitespace included.
        Assert.AreEqual(Result, response.Raw.ToString());

        // The member the model does not know is in the raw text and absent from the projection —
        // this is the loss the raw bytes exist to make visible.
        StringAssert.Contains(response.Raw.ToString(), "a_field_no_model_knows");
        StringAssert.Contains(
            JsonSerializer.Serialize(response.Result, XrplJsonOptions.Default),
            "ledger_current_index");
        Assert.IsFalse(
            JsonSerializer.Serialize(response.Result, XrplJsonOptions.Default).Contains("a_field_no_model_knows"),
            "the typed projection cannot carry a member its model has no property for");

        Assert.AreEqual(96000000u, response.Result.CurrentIndex);

        await client.Disconnect();
    }

    /// <summary>
    /// The envelope the client used to unwrap and discard. `warning` in particular was unreachable:
    /// it is not part of `result`, so the raw bytes do not carry it either.
    /// </summary>
    [TestMethod]
    public async Task TestUEnvelopeReachesTheCaller()
    {
        using ScriptedResponseServer server = new ScriptedResponseServer(
            "{\"id\":__ID__,\"status\":\"success\",\"type\":\"response\",\"api_version\":2,"
            + "\"warning\":\"load\",\"forwarded\":true,"
            + "\"warnings\":[{\"id\":1004,\"message\":\"This is a reporting server\"}],"
            + "\"result\":{\"ledger_current_index\":9}}");
        using XrplClient client = new XrplClient(server.Url, new XrplClient.ClientOptions { ApiVersion = 2 });
        await client.Connect();

        XrplResponse<LOLedgerCurrentIndex> response =
            await client.LedgerCurrent(new LedgerCurrentRequest());

        Assert.AreEqual("load", response.Warning);
        Assert.AreEqual(2u, response.ApiVersion);
        Assert.IsTrue(response.Forwarded);
        Assert.AreEqual(1, response.Warnings.Count);
        Assert.AreEqual(1004u, response.Warnings[0].Id);

        await client.Disconnect();
    }

    /// <summary>
    /// <c>status</c> sits beside <c>result</c> in the envelope, the same as <c>warning</c> — before
    /// <see cref="XrplResponse{T}.Status"/> existed, <see cref="XrplResponse.From{T}(ResolvedResponse)"/>
    /// read every other envelope member off <c>BaseResponse</c> except this one, so it was
    /// unreachable from a caller holding only the typed response: not part of <c>Result</c> (no
    /// model declares it) and not part of <see cref="XrplResponse{T}.Raw"/> either (that is a slice
    /// of <c>result</c> alone). Value matches a real mainnet account_info response.
    /// </summary>
    [TestMethod]
    public async Task TestUStatusReachesTheCaller()
    {
        using ScriptedResponseServer server = new ScriptedResponseServer(
            "{\"id\":__ID__,\"status\":\"success\",\"type\":\"response\","
            + "\"result\":{\"ledger_current_index\":106359163}}");
        using XrplClient client = new XrplClient(server.Url, new XrplClient.ClientOptions { ApiVersion = 2 });
        await client.Connect();

        XrplResponse<LOLedgerCurrentIndex> response =
            await client.LedgerCurrent(new LedgerCurrentRequest());

        Assert.AreEqual("success", response.Status);

        await client.Disconnect();
    }

    /// <summary>The untyped path carries the same envelope and the same raw bytes.</summary>
    [TestMethod]
    public async Task TestUUntypedRequestAlsoCarriesRawAndEnvelope()
    {
        const string Result = "{\"ledger_current_index\" : 42,\"unknown\":true}";

        using ScriptedResponseServer server = new ScriptedResponseServer(
            "{\"id\":__ID__,\"status\":\"success\",\"type\":\"response\",\"warning\":\"load\","
            + "\"result\":" + Result + "}");
        using XrplClient client = new XrplClient(server.Url, new XrplClient.ClientOptions { ApiVersion = 2 });
        await client.Connect();

        XrplResponse<Dictionary<string, object>> response = await client.Request(
            new Dictionary<string, object> { ["command"] = "ledger_current" });

        Assert.AreEqual(Result, response.Raw.ToString());
        Assert.AreEqual("load", response.Warning);
        Assert.IsTrue(response.Result.ContainsKey("unknown"));

        await client.Disconnect();
    }


    /// <summary>
    /// Envelope models must stay serializable. The slice members are set-only for exactly this
    /// reason: they carry bounds, not bytes, and the converter refuses to write them — an envelope
    /// rebuilt from bounds would be a different document. If they ever regain a getter, System.Text.Json
    /// asks for one on write and every envelope throws, including the public subscription types a
    /// consumer may well be logging.
    /// </summary>
    [TestMethod]
    public void TestUEnvelopeModelsStaySerializable()
    {
        Assert.AreEqual("{}", JsonSerializer.Serialize(new BaseResponse(), XrplJsonOptions.Default));
        Assert.AreEqual("{}", JsonSerializer.Serialize(new ErrorResponse(), XrplJsonOptions.Default));

        string stream = JsonSerializer.Serialize(new LedgerStreamResponse(), XrplJsonOptions.Default);
        StringAssert.Contains(stream, "ledger_index");
    }

}
