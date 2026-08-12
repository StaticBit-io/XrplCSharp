using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Json;
using Xrpl.Models;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;

namespace XrplTests.Client.Json.Converters;

/// <summary>
/// account_objects returns a heterogeneous array — every element carries its own LedgerEntryType.
/// LOConverter is registered globally, so the elements of List&lt;BaseLedgerEntry&gt; are resolved to the
/// concrete LO* types; nothing but these tests pins that for the response model itself.
/// </summary>
[TestClass]
public class TestUAccountObjectsPolymorphism
{
    private static readonly JsonSerializerOptions Options = XrplJsonOptions.Default;

    private const string MixedResponse = @"{
        ""account"": ""r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59"",
        ""account_objects"": [
            {
                ""LedgerEntryType"": ""RippleState"",
                ""Balance"": {""currency"": ""USD"", ""issuer"": ""rrrrrrrrrrrrrrrrrrrrBZbvji"", ""value"": ""-16.005""},
                ""Flags"": 131072,
                ""HighLimit"": {""currency"": ""USD"", ""issuer"": ""r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59"", ""value"": ""5000""},
                ""LowLimit"": {""currency"": ""USD"", ""issuer"": ""rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B"", ""value"": ""0""},
                ""PreviousTxnID"": ""CFFF5CFE623C9543308C6529782B6A6532207D819795AAFE85555DB8BF390FE7"",
                ""PreviousTxnLgrSeq"": 14365854,
                ""index"": ""826CF5BFD28F3934B518D0BDF3231259CBD3FD0946E3C3CA0C97D2C75D2D1A09""
            },
            {
                ""LedgerEntryType"": ""Check"",
                ""Account"": ""rUn84CJZe1swmzfnRMHPBmTGVsQFhLtLTb"",
                ""Destination"": ""rfkE1aSy9G8Upk4JssnwBxhEv5p4mn2KTy"",
                ""SendMax"": ""100000000"",
                ""Sequence"": 2,
                ""PreviousTxnLgrSeq"": 8010340,
                ""index"": ""49647F0D748DC3FE26BDACBC57F251AADEFFF391403EC9BF87C97F67E9977FB0""
            },
            {
                ""LedgerEntryType"": ""Escrow"",
                ""Account"": ""rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn"",
                ""Destination"": ""ra5nK24KXen9AHvsdFTKHSANinZseWnPcX"",
                ""Amount"": ""10000"",
                ""PreviousTxnLgrSeq"": 14328672,
                ""index"": ""DC5F3851D8A1AB622F957761E5963BC5BD439D5C24AC6AD7AC4523F0640244AC""
            },
            {
                ""LedgerEntryType"": ""SignerList"",
                ""Flags"": 0,
                ""OwnerNode"": ""0000000000000000"",
                ""SignerQuorum"": 3,
                ""index"": ""A9C28A28B85CD533217F5C0A0C7767666B093FA58A0F2D80026FCC4CD932DDC7""
            }
        ],
        ""ledger_hash"": ""053DF17D2289D1C4971C22F235BC1FCA7D4B3AE966F842E5819D0749E0B8ECD3"",
        ""ledger_index"": 14378733,
        ""validated"": true
    }";

    [TestMethod]
    public void Deserialize_MixedAccountObjects_ResolvesEachElementToItsConcreteType()
    {
        AccountObjects response = JsonSerializer.Deserialize<AccountObjects>(MixedResponse, Options);

        Assert.IsNotNull(response);
        Assert.HasCount(4, response.AccountObjectList);

        Assert.IsInstanceOfType(response.AccountObjectList[0], typeof(LORippleState));
        Assert.IsInstanceOfType(response.AccountObjectList[1], typeof(LOCheck));
        Assert.IsInstanceOfType(response.AccountObjectList[2], typeof(LOEscrow));
        Assert.IsInstanceOfType(response.AccountObjectList[3], typeof(LOSignerList));
    }

    [TestMethod]
    public void Deserialize_MixedAccountObjects_KeepsSubtypeFields()
    {
        AccountObjects response = JsonSerializer.Deserialize<AccountObjects>(MixedResponse, Options);

        LORippleState state = (LORippleState)response.AccountObjectList[0];
        Assert.AreEqual("CFFF5CFE623C9543308C6529782B6A6532207D819795AAFE85555DB8BF390FE7", state.PreviousTxnID);
        Assert.AreEqual("USD", state.Balance.CurrencyCode);
        Assert.AreEqual("5000", state.HighLimit.Value);

        LOCheck check = (LOCheck)response.AccountObjectList[1];
        Assert.AreEqual("rUn84CJZe1swmzfnRMHPBmTGVsQFhLtLTb", check.Account);
        Assert.AreEqual("rfkE1aSy9G8Upk4JssnwBxhEv5p4mn2KTy", check.Destination);

        LOEscrow escrow = (LOEscrow)response.AccountObjectList[2];
        Assert.AreEqual("rf1BiGeXwwQoi8Z2ueFYTEXSwuJYfV2Jpn", escrow.Account);

        LOSignerList signerList = (LOSignerList)response.AccountObjectList[3];
        Assert.AreEqual(3u, signerList.SignerQuorum);
    }

    [TestMethod]
    public void Deserialize_MixedAccountObjects_SetsLedgerEntryTypeAndIndex()
    {
        AccountObjects response = JsonSerializer.Deserialize<AccountObjects>(MixedResponse, Options);

        Assert.AreEqual(LedgerEntryType.RippleState, response.AccountObjectList[0].LedgerEntryType);
        Assert.AreEqual(LedgerEntryType.Check, response.AccountObjectList[1].LedgerEntryType);
        Assert.AreEqual(LedgerEntryType.Escrow, response.AccountObjectList[2].LedgerEntryType);
        Assert.AreEqual(LedgerEntryType.SignerList, response.AccountObjectList[3].LedgerEntryType);

        Assert.AreEqual(
            "826CF5BFD28F3934B518D0BDF3231259CBD3FD0946E3C3CA0C97D2C75D2D1A09",
            response.AccountObjectList[0].Index);
    }

    /// <summary>
    /// A ledger object type the SDK does not know must not throw — it falls back to the base entry.
    /// This is why BaseLedgerEntry stays a concrete class.
    /// </summary>
    [TestMethod]
    public void Deserialize_UnknownLedgerEntryType_FallsBackToBaseLedgerEntry()
    {
        string json = @"{
            ""account"": ""rTest"",
            ""account_objects"": [
                { ""LedgerEntryType"": ""SomethingRippledAddedLater"", ""Whatever"": 1, ""index"": ""AABB"" }
            ]
        }";

        AccountObjects response = JsonSerializer.Deserialize<AccountObjects>(json, Options);

        Assert.HasCount(1, response.AccountObjectList);
        BaseLedgerEntry entry = response.AccountObjectList[0];
        Assert.AreEqual(typeof(BaseLedgerEntry), entry.GetType());
        Assert.AreEqual(LedgerEntryType.Unknown, entry.LedgerEntryType);
        Assert.AreEqual("AABB", entry.Index);
    }

    [TestMethod]
    public void Serialize_MixedAccountObjects_WritesConcreteTypeFields()
    {
        AccountObjects response = JsonSerializer.Deserialize<AccountObjects>(MixedResponse, Options);

        string json = JsonSerializer.Serialize(response, Options);

        Assert.Contains("\"LedgerEntryType\":\"RippleState\"", json);
        Assert.Contains("\"LedgerEntryType\":\"Check\"", json);
        Assert.Contains("rUn84CJZe1swmzfnRMHPBmTGVsQFhLtLTb", json);

        AccountObjects roundTrip = JsonSerializer.Deserialize<AccountObjects>(json, Options);
        Assert.IsInstanceOfType(roundTrip.AccountObjectList[0], typeof(LORippleState));
        Assert.IsInstanceOfType(roundTrip.AccountObjectList[1], typeof(LOCheck));
    }

    /// <summary>
    /// The list path is the one that used to build a fresh JsonSerializerOptions per element:
    /// a full page must resolve every entry, not just the first.
    /// </summary>
    [TestMethod]
    public void Deserialize_LargeAccountObjectsPage_ResolvesEveryElement()
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        builder.Append(@"{""account"":""rTest"",""account_objects"":[");
        for (int i = 0; i < 200; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append(@"{""LedgerEntryType"":""Offer"",""Account"":""rTest"",""Sequence"":")
                   .Append(i)
                   .Append(@",""index"":""")
                   .Append(i.ToString("X64"))
                   .Append(@"""}");
        }
        builder.Append("]}");

        AccountObjects response = JsonSerializer.Deserialize<AccountObjects>(builder.ToString(), Options);

        Assert.HasCount(200, response.AccountObjectList);
        for (int i = 0; i < 200; i++)
        {
            Assert.IsInstanceOfType(response.AccountObjectList[i], typeof(LOOffer));
            Assert.AreEqual((uint)i, ((LOOffer)response.AccountObjectList[i]).Sequence);
        }
    }
}
