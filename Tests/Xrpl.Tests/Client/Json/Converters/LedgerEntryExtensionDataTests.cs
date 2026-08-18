using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Json;
using Xrpl.Models.Ledger;
using Xrpl.Models.Transactions;

namespace XrplTests.Client.Json.Converters;

// Covers BaseLedgerEntry.UnknownFields: an amendment (or any node) can add a field to a ledger
// entry before this SDK models it. Before this attribute, that field was silently dropped on
// deserialize with no error and no trace of it in the typed model.
[TestClass]
public class TestULedgerEntryExtensionData
{
    private static readonly JsonSerializerOptions Options = XrplJsonOptions.Default;

    private const string AccountRootJsonWithUnknownField = @"{
        ""LedgerEntryType"": ""AccountRoot"",
        ""Account"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"",
        ""Balance"": ""10000000000"",
        ""Flags"": 0,
        ""Sequence"": 1,
        ""NewAmendmentField"": ""probe-value""
    }";

    [TestMethod]
    public void Deserialize_LOAccountRoot_Direct_CapturesUnknownField()
    {
        LOAccountRoot result = JsonSerializer.Deserialize<LOAccountRoot>(AccountRootJsonWithUnknownField, Options);

        Assert.IsNotNull(result.UnknownFields);
        Assert.IsTrue(result.UnknownFields.ContainsKey("NewAmendmentField"));
        Assert.AreEqual("probe-value", result.UnknownFields["NewAmendmentField"].GetString());
    }

    [TestMethod]
    public void Deserialize_LOAccountRoot_ThroughLOConverter_CapturesUnknownField()
    {
        // Goes through LOConverter.Read -> GetTypeForLedgerEntry -> JsonSerializer.Deserialize for
        // the concrete LOAccountRoot type. LOConverter itself is stripped from the inner options to
        // avoid recursion, but that only removes the envelope-level dispatch; the field-level read
        // for LOAccountRoot is still the ordinary reflection-based deserializer.
        BaseLedgerEntry result = JsonSerializer.Deserialize<BaseLedgerEntry>(AccountRootJsonWithUnknownField, Options);

        Assert.IsInstanceOfType(result, typeof(LOAccountRoot));
        LOAccountRoot accountRoot = (LOAccountRoot)result;
        Assert.IsNotNull(accountRoot.UnknownFields);
        Assert.IsTrue(accountRoot.UnknownFields.ContainsKey("NewAmendmentField"));
    }

    [TestMethod]
    public void Deserialize_ModifiedNode_FinalFieldsAndPreviousFields_CaptureUnknownFields()
    {
        string json = @"{
            ""LedgerEntryType"": ""AccountRoot"",
            ""LedgerIndex"": ""ABCDEF"",
            ""PreviousTxnID"": ""DEADBEEF"",
            ""PreviousTxnLgrSeq"": 12345,
            ""FinalFields"": {
                ""Account"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"",
                ""Balance"": ""10000000000"",
                ""Flags"": 0,
                ""Sequence"": 1,
                ""NewAmendmentField"": ""final-value""
            },
            ""PreviousFields"": {
                ""Balance"": ""9000000000"",
                ""AnotherUnknownField"": 42
            }
        }";

        ModifiedNode node = JsonSerializer.Deserialize<ModifiedNode>(json, Options);

        LOAccountRoot final = node.FinalFields as LOAccountRoot;
        Assert.IsNotNull(final);
        Assert.IsNotNull(final.UnknownFields);
        Assert.IsTrue(final.UnknownFields.ContainsKey("NewAmendmentField"));

        LOAccountRoot previous = node.PreviousFields as LOAccountRoot;
        Assert.IsNotNull(previous);
        Assert.IsNotNull(previous.UnknownFields);
        Assert.IsTrue(previous.UnknownFields.ContainsKey("AnotherUnknownField"));
        Assert.AreEqual(42, previous.UnknownFields["AnotherUnknownField"].GetInt32());
    }

    [TestMethod]
    public void Deserialize_LOAccountRoot_WithOnlyKnownFields_LeavesUnknownFieldsEmpty()
    {
        string json = @"{
            ""LedgerEntryType"": ""AccountRoot"",
            ""Account"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"",
            ""Balance"": ""10000000000"",
            ""Flags"": 0,
            ""Sequence"": 1
        }";

        LOAccountRoot result = JsonSerializer.Deserialize<LOAccountRoot>(json, Options);

        // System.Text.Json leaves the extension-data dictionary null (not an empty dictionary) when
        // nothing overflowed into it.
        Assert.IsTrue(result.UnknownFields == null || result.UnknownFields.Count == 0);
    }

    [TestMethod]
    public void Serialize_LOAccountRoot_Direct_RoundTripsUnknownField()
    {
        LOAccountRoot result = JsonSerializer.Deserialize<LOAccountRoot>(AccountRootJsonWithUnknownField, Options);

        string output = JsonSerializer.Serialize(result, Options);

        using JsonDocument doc = JsonDocument.Parse(output);
        Assert.IsTrue(doc.RootElement.TryGetProperty("NewAmendmentField", out JsonElement value));
        Assert.AreEqual("probe-value", value.GetString());
    }

    [TestMethod]
    public void Serialize_ModifiedNode_RoundTripsUnknownFieldInsideFinalFields()
    {
        string json = @"{
            ""LedgerEntryType"": ""AccountRoot"",
            ""LedgerIndex"": ""ABCDEF"",
            ""PreviousTxnID"": ""DEADBEEF"",
            ""PreviousTxnLgrSeq"": 12345,
            ""FinalFields"": {
                ""Account"": ""rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh"",
                ""Balance"": ""10000000000"",
                ""Flags"": 0,
                ""Sequence"": 1,
                ""NewAmendmentField"": ""final-value""
            }
        }";

        ModifiedNode node = JsonSerializer.Deserialize<ModifiedNode>(json, Options);
        string output = JsonSerializer.Serialize(node, Options);

        using JsonDocument doc = JsonDocument.Parse(output);
        JsonElement finalFields = doc.RootElement.GetProperty("FinalFields");
        Assert.IsTrue(finalFields.TryGetProperty("NewAmendmentField", out JsonElement value));
        Assert.AreEqual("final-value", value.GetString());
    }
}
