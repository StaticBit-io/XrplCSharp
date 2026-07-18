using System.IO;
using System.Text.Json;

using GenerateEnums;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace XrplTests.GenerateEnums;

[TestClass]
public class TestUDefinitions
{
    private const string Sample = """
    {
      "FIELDS": [
        ["Generic", {"nth":0,"isVLEncoded":false,"isSerialized":false,"isSigningField":false,"type":"Unknown"}],
        ["Sponsor", {"nth":27,"isVLEncoded":true,"isSerialized":true,"isSigningField":true,"type":"AccountID"}]
      ],
      "TYPES": {"Done":-1,"AccountID":8},
      "LEDGER_ENTRY_TYPES": {"Any":-3,"AccountRoot":97},
      "TRANSACTION_RESULTS": {"telLOCAL_ERROR":-399,"tesSUCCESS":0},
      "TRANSACTION_TYPES": {"Invalid":-1,"Payment":0}
    }
    """;

    [TestMethod]
    public void TestUParse_ReadsAllFiveSections()
    {
        using JsonDocument doc = JsonDocument.Parse(Sample);
        Definitions d = Definitions.Parse(doc.RootElement);

        Assert.AreEqual(2, d.Fields.Count);
        Assert.AreEqual("AccountID", d.Fields["Sponsor"].Type);
        Assert.AreEqual(27, d.Fields["Sponsor"].Nth);
        Assert.IsTrue(d.Fields["Sponsor"].IsVLEncoded);
        Assert.AreEqual(8, d.Types["AccountID"]);
        Assert.AreEqual(97, d.LedgerEntryTypes["AccountRoot"]);
        Assert.AreEqual(0, d.TransactionResults["tesSUCCESS"]);
        Assert.AreEqual(0, d.TransactionTypes["Payment"]);
    }

    [TestMethod]
    public void TestUParse_UnwrapsNodeResultEnvelope()
    {
        // A node response nests the payload under "result"; ParseResponse unwraps it
        string wrapped = "{\"result\":" + Sample + ",\"status\":\"success\"}";
        using JsonDocument doc = JsonDocument.Parse(wrapped);
        Definitions d = Definitions.ParseResponse(doc.RootElement);
        Assert.AreEqual(2, d.Fields.Count);
        Assert.AreEqual(0, d.TransactionTypes["Payment"]);
    }

    [TestMethod]
    public void TestUParseResponse_NodeError_ThrowsClearMessage()
    {
        // rippled JSON-RPC error: HTTP 200 with status/error inside result
        string errorResponse =
            "{\"result\":{\"error\":\"unknownCmd\",\"error_message\":\"Unknown method.\",\"status\":\"error\"}}";
        using JsonDocument doc = JsonDocument.Parse(errorResponse);
        InvalidDataException ex = Assert.ThrowsExactly<InvalidDataException>(
            () => Definitions.ParseResponse(doc.RootElement));
        StringAssert.Contains(ex.Message, "unknownCmd");
        StringAssert.Contains(ex.Message, "Unknown method.");
    }
}
