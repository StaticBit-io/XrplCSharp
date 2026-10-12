using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Subscriptions;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// <c>book_changes</c>, <c>manifest</c> and <c>version</c> against a node.
/// </summary>
[TestClass]
public class TestIBookChangesManifestVersion
{
    private static IXrplClient client;
    private static readonly TestNodeType nodeType = IntegrationTestConfig.CurrentNodeType;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(nodeType);
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    /// <summary>
    /// The issuer sells 100 USD for 50 XRP; a taker buys 10 USD for 5 XRP. The ledger holding the
    /// taker's offer reports that trade for the XRP/USD book.
    /// </summary>
    [TestMethod]
    public async Task TestBookChanges_ReportsATradeInItsLedger()
    {
        XrplWallet issuer = XrplWallet.Generate();
        XrplWallet taker = XrplWallet.Generate();
        await IntegrationTestConfig.TryFundWalletsAsync(client, nodeType, issuer, taker);

        await SubmitAsync(new TrustSet
        {
            Account = taker.ClassicAddress,
            LimitAmount = new Currency { CurrencyCode = "USD", Issuer = issuer.ClassicAddress, Value = "1000" },
        }, taker);

        await SubmitAsync(new OfferCreate
        {
            Account = issuer.ClassicAddress,
            TakerGets = new Currency { CurrencyCode = "USD", Issuer = issuer.ClassicAddress, Value = "100" },
            TakerPays = new Currency { Value = "50000000" },
        }, issuer);

        TransactionSummary trade = await SubmitAsync(new OfferCreate
        {
            Account = taker.ClassicAddress,
            TakerGets = new Currency { Value = "5000000" },
            TakerPays = new Currency { CurrencyCode = "USD", Issuer = issuer.ClassicAddress, Value = "10" },
        }, taker);
        Assert.IsNotNull(trade.LedgerIndex, "a validated transaction names its ledger");

        BookChangesResponse response = await client.BookChanges(
            new BookChangesRequest { LedgerIndex = new LedgerIndex((uint)trade.LedgerIndex.Value) }).Typed();

        Assert.AreEqual("bookChanges", response.Type);
        Assert.AreEqual((uint)trade.LedgerIndex.Value, response.LedgerIndex);
        Assert.IsNotNull(response.LedgerHash);
        Assert.IsNotNull(response.LedgerTime);

        BookChange change = response.Changes.SingleOrDefault(c => c.AssetB?.Issuer == issuer.ClassicAddress);
        Assert.IsNotNull(change, "the XRP/USD book the taker traded against must be reported");
        Assert.IsTrue(change.IsXrpA, "XRP is always side A");
        Assert.AreEqual("USD", change.AssetB.Currency);
        Assert.AreEqual("5000000", change.VolumeA);
        Assert.AreEqual(10m, decimal.Parse(change.VolumeB, CultureInfo.InvariantCulture));
        Assert.AreEqual(500000m, decimal.Parse(change.Close, NumberStyles.Float, CultureInfo.InvariantCulture), "500000 drops per USD");
        Assert.IsNull(change.Domain);
    }

    /// <summary>
    /// A node key with no validator manifest behind it: rippled's manifest cache maps it to itself, so the
    /// details name it as both keys, with no manifest. A standalone node has no validator manifests.
    /// </summary>
    [TestMethod]
    public async Task TestManifest_KeyWithoutManifest()
    {
        ServerInfo info = await client.ServerInfo(new ServerInfoRequest()).Typed();
        string nodeKey = info.Info.PubkeyNode;
        Assert.IsNotNull(nodeKey);

        ManifestResponse response = await client.Manifest(new ManifestRequest(nodeKey)).Typed();

        Assert.AreEqual(nodeKey, response.Requested);
        Assert.IsNull(response.Manifest, "the node's own peer key has no validator manifest");
        Assert.IsNotNull(response.Details);
        Assert.AreEqual(nodeKey, response.Details.MasterKey);
        Assert.AreEqual(nodeKey, response.Details.EphemeralKey);
        Assert.IsNull(response.Details.Seq);
    }

    /// <summary>
    /// The SDK asks with API version 2 by default, so the node answers with version numbers.
    /// </summary>
    [TestMethod]
    public async Task TestVersion_ReportsTheSupportedRange()
    {
        VersionResponse response = await client.Version().Typed();

        Assert.AreEqual("1", response.Version.First);
        Assert.IsGreaterThanOrEqualTo(2, int.Parse(response.Version.Last, CultureInfo.InvariantCulture));
        Assert.IsNull(response.Version.Good, "good is sent to API version 1 requests only");
    }

    private static async Task<TransactionSummary> SubmitAsync<T>(T tx, XrplWallet wallet) where T : ITransactionRequest
    {
        T autofilled = await client.Autofill(tx);
        TransactionSummary result = await client.SubmitAndWait(autofilled, wallet, true);
        Assert.AreEqual("tesSUCCESS", result.Meta?.TransactionResult, $"{typeof(T).Name} failed");
        return result;
    }
}
