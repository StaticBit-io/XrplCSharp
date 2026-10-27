using System.Collections.Generic;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Json;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Methods;

namespace XrplTests.Client.Json;

[TestClass]
public class TestURequestSerialization
{
    private static readonly JsonSerializerOptions Options = XrplJsonOptions.Default;

    [TestMethod]
    public void Serialize_AccountInfoRequest_IncludesAllFields()
    {
        BaseRequest request = new AccountInfoRequest("rTestAddress") { Queue = true, Strict = true };
        string json = JsonSerializer.Serialize(request, request.GetType(), Options);

        Assert.IsTrue(json.Contains("\"account\""), "Missing 'account' field");
        Assert.IsTrue(json.Contains("rTestAddress"), "Missing account value");
        Assert.IsTrue(json.Contains("\"command\""), "Missing 'command' field");
        Assert.IsTrue(json.Contains("\"account_info\""), "Missing command value");
        Assert.IsTrue(json.Contains("\"queue\""), "Missing 'queue' field");
        Assert.IsTrue(json.Contains("\"strict\""), "Missing 'strict' field");
    }

    [TestMethod]
    public void Serialize_AccountLinesRequest_IncludesAccountAndLimit()
    {
        BaseRequest request = new AccountLinesRequest("rTestAddress") { Limit = 100 };
        string json = JsonSerializer.Serialize(request, request.GetType(), Options);

        Assert.IsTrue(json.Contains("\"account\""), "Missing 'account' field");
        Assert.IsTrue(json.Contains("rTestAddress"), "Missing account value");
        Assert.IsTrue(json.Contains("\"limit\""), "Missing 'limit' field");
        Assert.IsTrue(json.Contains("100"), "Missing limit value");
        Assert.IsTrue(json.Contains("\"command\""), "Missing 'command' field");
    }

    [TestMethod]
    public void Serialize_AccountObjectsRequest_IncludesAccount()
    {
        BaseRequest request = new AccountObjectsRequest("rTestAddress");
        string json = JsonSerializer.Serialize(request, request.GetType(), Options);

        Assert.IsTrue(json.Contains("\"account\""), "Missing 'account' field");
        Assert.IsTrue(json.Contains("rTestAddress"), "Missing account value");
        Assert.IsTrue(json.Contains("\"account_objects\""), "Missing command value");
    }

    [TestMethod]
    public void Serialize_AccountOffersRequest_IncludesAccount()
    {
        BaseRequest request = new AccountOffersRequest("rTestAddress") { Limit = 50 };
        string json = JsonSerializer.Serialize(request, request.GetType(), Options);

        Assert.IsTrue(json.Contains("\"account\""), "Missing 'account' field");
        Assert.IsTrue(json.Contains("rTestAddress"), "Missing account value");
        Assert.IsTrue(json.Contains("\"limit\""), "Missing 'limit' field");
    }

    [TestMethod]
    public void Serialize_SubscribeRequest_IncludesStreams()
    {
        BaseRequest request = new SubscribeRequest
        {
            Streams = new List<StreamType> { StreamType.Ledger, StreamType.Transactions }
        };
        string json = JsonSerializer.Serialize(request, request.GetType(), Options);

        Assert.IsTrue(json.Contains("\"streams\""), "Missing 'streams' field");
        Assert.IsTrue(json.Contains("ledger"), "Missing stream value");
        Assert.IsTrue(json.Contains("transactions"), "Missing stream value");
    }

    [TestMethod]
    public void Serialize_LedgerEntryRequest_IncludesFields()
    {
        BaseRequest request = new LedgerEntryRequest
        {
            RippleState = new RippleStateQuery
            {
                Addresses = new[] { "rAccount1", "rAccount2" },
                Currency = "USD"
            }
        };
        string json = JsonSerializer.Serialize(request, request.GetType(), Options);

        Assert.IsTrue(json.Contains("\"ripple_state\"") || json.Contains("\"RippleState\""),
            "Missing 'ripple_state' field");
        Assert.IsTrue(json.Contains("USD"), "Missing currency value");
    }

    [TestMethod]
    public void Serialize_LedgerEntryRequest_TransactionProposal_UsesRippledParameterNames()
    {
        BaseRequest request = new LedgerEntryRequest
        {
            TransactionProposal = new TransactionProposalQuery
            {
                Account = "rAccount1",
                TicketSequence = 7
            }
        };
        string json = JsonSerializer.Serialize(request, request.GetType(), Options);

        // rippled parseTransactionProposal reads jss::account and jss::ticket_seq.
        System.Text.Json.Nodes.JsonNode query = System.Text.Json.Nodes.JsonNode.Parse(json)!["transaction_proposal"];
        Assert.IsNotNull(query, "Missing 'transaction_proposal' field");
        Assert.AreEqual("rAccount1", query["account"]!.GetValue<string>());
        Assert.AreEqual(7u, query["ticket_seq"]!.GetValue<uint>());
    }

    [TestMethod]
    public void Serialize_BookOffersRequest_IncludesAllFields()
    {
        BaseRequest request = new BookOffersRequest
        {
            TakerGets = new TakerAmount { Currency = "XRP" },
            TakerPays = new TakerAmount { Currency = "USD", Issuer = "rIssuer" },
            Limit = 10
        };
        string json = JsonSerializer.Serialize(request, request.GetType(), Options);

        Assert.IsTrue(json.Contains("\"taker_gets\""), "Missing 'taker_gets' field");
        Assert.IsTrue(json.Contains("\"taker_pays\""), "Missing 'taker_pays' field");
        Assert.IsTrue(json.Contains("\"limit\""), "Missing 'limit' field");
    }

    [TestMethod]
    public void Serialize_TxRequest_IncludesTransaction()
    {
        BaseRequest request = new TxRequest("ABC123HASH");
        string json = JsonSerializer.Serialize(request, request.GetType(), Options);

        Assert.IsTrue(json.Contains("\"transaction\"") || json.Contains("ABC123HASH"),
            "Missing 'transaction' field or hash value");
        Assert.IsTrue(json.Contains("\"command\""), "Missing 'command' field");
    }

    [TestMethod]
    public void Serialize_ServerInfoRequest_IncludesCommand()
    {
        BaseRequest request = new ServerInfoRequest();
        string json = JsonSerializer.Serialize(request, request.GetType(), Options);

        Assert.IsTrue(json.Contains("\"command\""), "Missing 'command' field");
        Assert.IsTrue(json.Contains("\"server_info\""), "Missing command value");
    }

    [TestMethod]
    public void Serialize_FeeRequest_IncludesCommand()
    {
        BaseRequest request = new FeeRequest();
        string json = JsonSerializer.Serialize(request, request.GetType(), Options);

        Assert.IsTrue(json.Contains("\"command\""), "Missing 'command' field");
        Assert.IsTrue(json.Contains("\"fee\""), "Missing command value");
    }

    /// <summary>
    /// path_find create searches a permissioned domain's books when <c>domain</c> is sent, as
    /// ripple_path_find does; rippled's PathRequest reads it from the create request.
    /// </summary>
    [TestMethod]
    public void Serialize_PathFindCreateRequest_WithDomain()
    {
        const string domain = "F10D0CC9A0F9A3CBF585B80BE09A186483668FDBDD39AA7E3370F3649CE134E5";
        BaseRequest request = new PathFindCreateRequest("rSource", "rDestination", new Currency { Value = "1000000" })
        {
            Domain = domain,
        };
        string json = JsonSerializer.Serialize(request, request.GetType(), Options);

        StringAssert.Contains(json, "\"command\":\"path_find\"");
        StringAssert.Contains(json, "\"subcommand\":\"create\"");
        StringAssert.Contains(json, "\"domain\":\"" + domain + "\"");
    }

    [TestMethod]
    public void Serialize_PathFindCreateRequest_WithoutDomain_OmitsIt()
    {
        BaseRequest request = new PathFindCreateRequest("rSource", "rDestination", new Currency { Value = "1000000" });
        string json = JsonSerializer.Serialize(request, request.GetType(), Options);

        Assert.DoesNotContain("\"domain\"", json);
    }
}
