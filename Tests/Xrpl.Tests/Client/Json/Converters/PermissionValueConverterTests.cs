using System.Collections.Generic;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.BinaryCodec;
using Xrpl.Client.Json;
using Xrpl.Models;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

namespace XrplTests.Client.Json.Converters;

/// <summary>
/// rippled returns a Delegate PermissionValue as a name string and gains new permission names with
/// every amendment, so a node is routinely ahead of the name tables bundled in a release of this
/// library. A name this version cannot map must cost the caller that one field and nothing else:
/// neither the rest of the transaction nor the rest of an account_objects page.
/// </summary>
[TestClass]
public class TestUPermissionValueConverter
{
    private static readonly JsonSerializerOptions Options = XrplJsonOptions.Default;

    private const string UnknownName = "BrandNewPermissionV2";

    private static PermissionEntry ReadEntry(string permissionValueToken)
    {
        string json = $@"{{ ""Permission"": {{ ""PermissionValue"": {permissionValueToken} }} }}";
        return JsonSerializer.Deserialize<PermissionWrapper>(json, Options).Permission;
    }

    [TestMethod]
    public void TestReadGranularPermissionName()
    {
        PermissionEntry entry = ReadEntry(@"""TrustlineAuthorize""");

        Assert.AreEqual(65537u, entry.PermissionValue);
        Assert.AreEqual("TrustlineAuthorize", entry.PermissionValueName);
    }

    [TestMethod]
    public void TestReadTransactionTypeName()
    {
        // Payment is transaction type 0, so its permission value is 0 + 1.
        PermissionEntry entry = ReadEntry(@"""Payment""");

        Assert.AreEqual(1u, entry.PermissionValue);
        Assert.AreEqual("Payment", entry.PermissionValueName);
    }

    [TestMethod]
    public void TestReadNumericValueResolvesName()
    {
        PermissionEntry entry = ReadEntry("65537");

        Assert.AreEqual(65537u, entry.PermissionValue);
        Assert.AreEqual("TrustlineAuthorize", entry.PermissionValueName);
    }

    [TestMethod]
    public void TestReadNumericStringValueResolvesName()
    {
        PermissionEntry entry = ReadEntry(@"""1""");

        Assert.AreEqual(1u, entry.PermissionValue);
        Assert.AreEqual("Payment", entry.PermissionValueName);
    }

    [TestMethod]
    public void TestReadUnknownNameKeepsTokenAndDoesNotThrow()
    {
        PermissionEntry entry = ReadEntry($@"""{UnknownName}""");

        // 0 is not a valid permission value on the ledger, so it is a safe "unknown" sentinel.
        Assert.AreEqual(0u, entry.PermissionValue);
        Assert.AreEqual(UnknownName, entry.PermissionValueName);
    }

    [TestMethod]
    public void TestReadUnknownNumberKeepsValueWithoutName()
    {
        PermissionEntry entry = ReadEntry("70000");

        Assert.AreEqual(70000u, entry.PermissionValue);
        Assert.IsNull(entry.PermissionValueName);
    }

    [TestMethod]
    public void TestWriteKnownValueAsNumber()
    {
        // The binary codec reads PermissionValue as a UInt32: writing the number is what keeps
        // local signing working, and must not change because a name is now carried alongside it.
        PermissionWrapper wrapper = new PermissionWrapper
        {
            Permission = new PermissionEntry { PermissionValue = 65537, PermissionValueName = "TrustlineAuthorize" },
        };

        string json = JsonSerializer.Serialize(wrapper, Options);

        Assert.AreEqual(@"{""Permission"":{""PermissionValue"":65537}}", json);
    }

    [TestMethod]
    public void TestWriteUnknownNameAsString()
    {
        // No number to write, but rippled accepts the name in tx_json, so the entry survives a
        // read/submit round trip instead of being sent as the invalid value 0.
        PermissionWrapper wrapper = new PermissionWrapper
        {
            Permission = new PermissionEntry { PermissionValue = 0, PermissionValueName = UnknownName },
        };

        string json = JsonSerializer.Serialize(wrapper, Options);

        Assert.AreEqual($@"{{""Permission"":{{""PermissionValue"":""{UnknownName}""}}}}", json);
    }

    [TestMethod]
    public void TestUnknownPermissionKeepsRestOfTransaction()
    {
        string json = $@"{{
            ""TransactionType"": ""DelegateSet"",
            ""Account"": ""r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59"",
            ""Authorize"": ""rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B"",
            ""Fee"": ""12"",
            ""Sequence"": 42,
            ""hash"": ""CFFF5CFE623C9543308C6529782B6A6532207D819795AAFE85555DB8BF390FE7"",
            ""Permissions"": [
                {{ ""Permission"": {{ ""PermissionValue"": ""Payment"" }} }},
                {{ ""Permission"": {{ ""PermissionValue"": ""{UnknownName}"" }} }}
            ]
        }}";

        ITransactionResponse response = JsonSerializer.Deserialize<ITransactionResponse>(json, Options);

        // TransactionResponseConverter swallows a JsonException and returns a bare object whose
        // TransactionType stays at the enum default, so an unknown name used to turn a DelegateSet
        // into an empty AccountSet without any error reaching the caller.
        Assert.IsInstanceOfType<DelegateSetResponse>(response);
        Assert.AreEqual(TransactionType.DelegateSet, response.TransactionType);
        Assert.AreEqual("r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59", response.Account);

        DelegateSetResponse delegateSet = (DelegateSetResponse)response;
        Assert.AreEqual("rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B", delegateSet.Authorize);
        Assert.IsNotNull(delegateSet.Permissions);
        Assert.HasCount(2, delegateSet.Permissions);
        Assert.AreEqual(1u, delegateSet.Permissions[0].Permission.PermissionValue);
        Assert.AreEqual(0u, delegateSet.Permissions[1].Permission.PermissionValue);
        Assert.AreEqual(UnknownName, delegateSet.Permissions[1].Permission.PermissionValueName);
    }

    [TestMethod]
    public void TestUnknownPermissionKeepsRestOfAccountObjectsPage()
    {
        string json = $@"{{
            ""account"": ""r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59"",
            ""account_objects"": [
                {{
                    ""LedgerEntryType"": ""Delegate"",
                    ""Account"": ""r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59"",
                    ""Authorize"": ""rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B"",
                    ""Flags"": 0,
                    ""OwnerNode"": ""0"",
                    ""PreviousTxnID"": ""CFFF5CFE623C9543308C6529782B6A6532207D819795AAFE85555DB8BF390FE7"",
                    ""PreviousTxnLgrSeq"": 14365854,
                    ""index"": ""826CF5BFD28F3934B518D0BDF3231259CBD3FD0946E3C3CA0C97D2C75D2D1A09"",
                    ""Permissions"": [ {{ ""Permission"": {{ ""PermissionValue"": ""{UnknownName}"" }} }} ]
                }},
                {{
                    ""LedgerEntryType"": ""Ticket"",
                    ""Account"": ""r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59"",
                    ""TicketSequence"": 7,
                    ""Flags"": 0,
                    ""OwnerNode"": ""0"",
                    ""PreviousTxnID"": ""CFFF5CFE623C9543308C6529782B6A6532207D819795AAFE85555DB8BF390FE7"",
                    ""PreviousTxnLgrSeq"": 14365854,
                    ""index"": ""3E8C4EF0D3D6E2B0F5C7A9B1D2E3F4A5B6C7D8E9F0A1B2C3D4E5F6A7B8C9D0E1""
                }}
            ],
            ""ledger_current_index"": 14380380,
            ""validated"": false
        }}";

        AccountObjects response = JsonSerializer.Deserialize<AccountObjects>(json, Options);

        // The unknown name used to throw out of the element converter and take the whole page with
        // it, including the unrelated Ticket entry.
        Assert.HasCount(2, response.AccountObjectList);

        LODelegate delegateEntry = (LODelegate)response.AccountObjectList[0];
        Assert.AreEqual("rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B", delegateEntry.Authorize);
        Assert.HasCount(1, delegateEntry.Permissions);
        Assert.AreEqual(0u, delegateEntry.Permissions[0].Permission.PermissionValue);
        Assert.AreEqual(UnknownName, delegateEntry.Permissions[0].Permission.PermissionValueName);

        Assert.IsInstanceOfType<LOTicket>(response.AccountObjectList[1]);
    }

    [TestMethod]
    public void TestKnownPermissionsStillEncodeForSigning()
    {
        DelegateSet transaction = new DelegateSet
        {
            Account = "rH438jEAzTs5PYtV6CHZqpDpwCKQmPW9Cg",
            Authorize = "rLNaPoKeeBjZe2qs6x52yVPZpZ8td4dc6w",
            Fee = 12,
            Sequence = 42,
            LastLedgerSequence = 100,
            Permissions = new List<PermissionWrapper>
            {
                new PermissionWrapper { Permission = new PermissionEntry { PermissionValue = 1 } },
                new PermissionWrapper { Permission = new PermissionEntry { PermissionValue = 65537 } },
            },
        };

        string json = JsonSerializer.Serialize(transaction, Options);
        Dictionary<string, object> asDictionary = JsonSerializer.Deserialize<Dictionary<string, object>>(json, Options);

        string blob = XrplBinaryCodec.Encode(asDictionary);

        // 2034 00000001 is Permission{PermissionValue: 1}, 2034 00010001 is 65537.
        StringAssert.Contains(blob, "EF203400000001E1EF203400010001E1");
    }

    [TestMethod]
    public void TestUnknownPermissionCannotBeSignedSilently()
    {
        // The name is written back out because rippled accepts it, but the binary codec has no
        // number for it. Refusing to encode is the point: signing 0 would submit a permission the
        // owner never granted.
        DelegateSet transaction = new DelegateSet
        {
            Account = "rH438jEAzTs5PYtV6CHZqpDpwCKQmPW9Cg",
            Authorize = "rLNaPoKeeBjZe2qs6x52yVPZpZ8td4dc6w",
            Fee = 12,
            Sequence = 42,
            Permissions = new List<PermissionWrapper>
            {
                new PermissionWrapper { Permission = new PermissionEntry { PermissionValue = 0, PermissionValueName = UnknownName } },
            },
        };

        string json = JsonSerializer.Serialize(transaction, Options);
        Dictionary<string, object> asDictionary = JsonSerializer.Deserialize<Dictionary<string, object>>(json, Options);

        InvalidJsonException exception = Assert.ThrowsExactly<InvalidJsonException>(() => XrplBinaryCodec.Encode(asDictionary));
        StringAssert.Contains(exception.Message, UnknownName);
    }

    [TestMethod]
    public void TestSignablePermissionsSurviveRoundTrip()
    {
        DelegateSet transaction = new DelegateSet
        {
            Account = "r9cZA1mLK5R5Am25ArfXFmqgNwjZgnfk59",
            Authorize = "rvYAfWj5gh67oV6fW32ZzP3Aw4Eubs59B",
            Permissions = new List<PermissionWrapper>
            {
                new PermissionWrapper { Permission = new PermissionEntry { PermissionValue = 1 } },
                new PermissionWrapper { Permission = new PermissionEntry { PermissionValue = 65537 } },
            },
        };

        string json = JsonSerializer.Serialize(transaction, Options);
        DelegateSet parsed = JsonSerializer.Deserialize<DelegateSet>(json, Options);

        Assert.HasCount(2, parsed.Permissions);
        Assert.AreEqual(1u, parsed.Permissions[0].Permission.PermissionValue);
        Assert.AreEqual(65537u, parsed.Permissions[1].Permission.PermissionValue);
    }
}
