using System;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Methods;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// What a plain rippled node says when asked for a Clio-only command - issue #132.
/// </summary>
/// <remarks>
/// <para>
/// <c>nft_info</c> and <c>nft_history</c> are served by Clio, not by rippled, so the stand this
/// suite runs against cannot answer them. That is worth a test rather than a gap: a consumer who
/// needs to work against both has to be able to recognise the refusal and fall back to their own
/// crawl, and what they can recognise it by is exactly what is asserted here.
/// </para>
/// <para>
/// The shape of the answers themselves is covered by unit tests built from Clio's own handlers.
/// This is the other half - that asking a node which does not serve them fails in a way that says
/// so, instead of looking like a network problem or an empty result.
/// </para>
/// </remarks>
[TestClass]
public class TestINFTClioCommands
{
    private const string TokenId = "00190000E78F76A49DD9158FA85DA4AAD95C0767303CC4611D73BB4300C989A8";

    private static IXrplClient client;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync();
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    [TestMethod]
    public async Task TestINFTInfoOnARippledNodeIsRefusedRecognisably()
    {
        RippledException error = await Assert.ThrowsExactlyAsync<RippledException>(
            () => client.NFTInfo(new NFTInfoRequest(TokenId)));

        Assert.IsNotNull(error.Response, "The node's own answer must reach the caller, not just a message.");
        Assert.AreEqual(
            "unknownCmd",
            error.Response.Error,
            $"A caller falling back to their own crawl needs to recognise this by the error code. Node said: {error.Message}");
    }

    [TestMethod]
    public async Task TestINFTHistoryOnARippledNodeIsRefusedRecognisably()
    {
        RippledException error = await Assert.ThrowsExactlyAsync<RippledException>(
            () => client.NFTHistory(new NFTHistoryRequest(TokenId)));

        Assert.IsNotNull(error.Response);
        Assert.AreEqual("unknownCmd", error.Response.Error, $"Node said: {error.Message}");
    }
}
