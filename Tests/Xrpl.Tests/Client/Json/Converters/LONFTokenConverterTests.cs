using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class TestULONFTokenConverter
{
    private const string NFTokenID = "000800006203F49C21D5D6E022CB16DE3538F248662FC73C29ABA6A90000000D";
    private const string OtherNFTokenID = "000800006203F49C21D5D6E022CB16DE3538F248662FC73C29ABA6A90000000E";
    private const string URI = "68747470733A2F2F6578616D706C652E636F6D";

    [TestMethod]
    public void Read_WrappedNFToken_UnwrapsCorrectly()
    {
        string json = @"{
            ""NFToken"": {
                ""NFTokenID"": ""000800006203F49C21D5D6E022CB16DE3538F248662FC73C29ABA6A90000000D"",
                ""URI"": ""68747470733A2F2F6578616D706C652E636F6D""
            }
        }";
        NFToken result = JsonSerializer.Deserialize<NFToken>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result);
        Assert.AreEqual(NFTokenID, result.NFTokenID);
        Assert.AreEqual(URI, result.URI);
    }

    [TestMethod]
    public void Read_MissingUri_NftIdOnly()
    {
        string json = @"{
            ""NFToken"": {
                ""NFTokenID"": ""000800006203F49C21D5D6E022CB16DE3538F248662FC73C29ABA6A90000000D""
            }
        }";
        NFToken result = JsonSerializer.Deserialize<NFToken>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(result);
        Assert.AreEqual(NFTokenID, result.NFTokenID);
        Assert.IsNull(result.URI);
    }

    /// <summary>
    /// The converter is declared as an attribute on <see cref="NFToken"/> itself, which outranks
    /// options.Converters — re-entering the serializer with the converter stripped from that list used to
    /// call Write again, recursively, until System.Text.Json aborted at MaxDepth.
    /// </summary>
    [TestMethod]
    public void Write_SingleNFToken_WritesWrappedShape()
    {
        NFToken token = new NFToken { NFTokenID = NFTokenID, URI = URI };

        string json = JsonSerializer.Serialize(token, XrplJsonOptions.Default);

        Assert.AreEqual(
            "{\"NFToken\":{\"NFTokenID\":\"" + NFTokenID + "\",\"URI\":\"" + URI + "\"}}",
            json);
    }

    [TestMethod]
    public void Write_NullUri_OmitsUri()
    {
        NFToken token = new NFToken { NFTokenID = NFTokenID, URI = null };

        string json = JsonSerializer.Serialize(token, XrplJsonOptions.Default);

        Assert.AreEqual("{\"NFToken\":{\"NFTokenID\":\"" + NFTokenID + "\"}}", json);
    }

    [TestMethod]
    public void Write_NullToken_WritesJsonNull()
    {
        Assert.AreEqual("null", JsonSerializer.Serialize<NFToken>(null, XrplJsonOptions.Default));
    }

    /// <summary>
    /// The converter honours the ignore condition of the options it is handed instead of hard-coding one:
    /// plain options keep nulls, <see cref="XrplJsonOptions.Default"/> drops them.
    /// </summary>
    [TestMethod]
    public void Write_NullUri_PlainOptions_KeepsNull()
    {
        NFToken token = new NFToken { NFTokenID = NFTokenID, URI = null };

        string json = JsonSerializer.Serialize(token, new JsonSerializerOptions());

        Assert.AreEqual("{\"NFToken\":{\"NFTokenID\":\"" + NFTokenID + "\",\"URI\":null}}", json);
    }

    [TestMethod]
    public void RoundTrip_FullNFToken_Matches()
    {
        NFToken source = new NFToken { NFTokenID = NFTokenID, URI = URI };

        NFToken result = JsonSerializer.Deserialize<NFToken>(
            JsonSerializer.Serialize(source, XrplJsonOptions.Default), XrplJsonOptions.Default);

        Assert.IsNotNull(result);
        Assert.AreEqual(source.NFTokenID, result.NFTokenID);
        Assert.AreEqual(source.URI, result.URI);
    }

    [TestMethod]
    public void RoundTrip_NullUri_Matches()
    {
        NFToken source = new NFToken { NFTokenID = NFTokenID, URI = null };

        NFToken result = JsonSerializer.Deserialize<NFToken>(
            JsonSerializer.Serialize(source, XrplJsonOptions.Default), XrplJsonOptions.Default);

        Assert.IsNotNull(result);
        Assert.AreEqual(source.NFTokenID, result.NFTokenID);
        Assert.IsNull(result.URI);
    }

    [TestMethod]
    public void Write_NFTokenPage_SerializesEveryToken()
    {
        LONFTokenPage page = new LONFTokenPage
        {
            NFTokens = new List<NFToken>
            {
                new NFToken { NFTokenID = NFTokenID, URI = URI },
                new NFToken { NFTokenID = OtherNFTokenID },
            }
        };

        string json = JsonSerializer.Serialize(page, XrplJsonOptions.Default);

        StringAssert.Contains(json, "\"NFToken\":{\"NFTokenID\":\"" + NFTokenID + "\",\"URI\":\"" + URI + "\"}");
        StringAssert.Contains(json, "\"NFToken\":{\"NFTokenID\":\"" + OtherNFTokenID + "\"}");
    }

    /// <summary>
    /// Regression: an NFTokenPage reaches <see cref="Meta"/> through any of the three affected-node kinds,
    /// so serializing the metadata of an NFToken transaction hit the recursion in every one of them.
    /// </summary>
    [TestMethod]
    public void Write_MetaWithNFTokenPageInCreatedNode_Completes()
    {
        AssertMetaRoundTrips(BuildMeta(@"{
            ""CreatedNode"": {
                ""LedgerEntryType"": ""NFTokenPage"",
                ""LedgerIndex"": ""0FD0A2E7D0B4E7D77E5A6F9DE0D5E4A00000000000000000000000000000FFFF"",
                ""NewFields"": { ""NFTokens"": [ NFTOKEN ] }
            }
        }"));
    }

    [TestMethod]
    public void Write_MetaWithNFTokenPageInModifiedNodeFinalFields_Completes()
    {
        AssertMetaRoundTrips(BuildMeta(@"{
            ""ModifiedNode"": {
                ""LedgerEntryType"": ""NFTokenPage"",
                ""LedgerIndex"": ""0FD0A2E7D0B4E7D77E5A6F9DE0D5E4A00000000000000000000000000000FFFF"",
                ""FinalFields"": { ""Flags"": 0, ""NFTokens"": [ NFTOKEN ] },
                ""PreviousTxnID"": ""03F847F7728739230C2C783FE1F0D56BCFE379FEFA521053FCCFBA1F9D697255"",
                ""PreviousTxnLgrSeq"": 75443929
            }
        }"));
    }

    [TestMethod]
    public void Write_MetaWithNFTokenPageInModifiedNodePreviousFields_Completes()
    {
        AssertMetaRoundTrips(BuildMeta(@"{
            ""ModifiedNode"": {
                ""LedgerEntryType"": ""NFTokenPage"",
                ""LedgerIndex"": ""0FD0A2E7D0B4E7D77E5A6F9DE0D5E4A00000000000000000000000000000FFFF"",
                ""FinalFields"": { ""Flags"": 0 },
                ""PreviousFields"": { ""NFTokens"": [ NFTOKEN ] },
                ""PreviousTxnID"": ""03F847F7728739230C2C783FE1F0D56BCFE379FEFA521053FCCFBA1F9D697255"",
                ""PreviousTxnLgrSeq"": 75443929
            }
        }"));
    }

    [TestMethod]
    public void Write_MetaWithNFTokenPageInDeletedNodeFinalFields_Completes()
    {
        AssertMetaRoundTrips(BuildMeta(@"{
            ""DeletedNode"": {
                ""LedgerEntryType"": ""NFTokenPage"",
                ""LedgerIndex"": ""0FD0A2E7D0B4E7D77E5A6F9DE0D5E4A00000000000000000000000000000FFFF"",
                ""FinalFields"": { ""Flags"": 0, ""NFTokens"": [ NFTOKEN ] }
            }
        }"));
    }

    private static Meta BuildMeta(string affectedNodeJson)
    {
        string nfToken = "{ \"NFToken\": { \"NFTokenID\": \"" + NFTokenID + "\", \"URI\": \"" + URI + "\" } }";
        string json = @"{
            ""TransactionIndex"": 0,
            ""TransactionResult"": ""tesSUCCESS"",
            ""AffectedNodes"": [ " + affectedNodeJson.Replace("NFTOKEN", nfToken) + @" ]
        }";

        Meta meta = JsonSerializer.Deserialize<Meta>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(meta);
        return meta;
    }

    private static void AssertMetaRoundTrips(Meta meta)
    {
        string json = JsonSerializer.Serialize(meta, XrplJsonOptions.Default);

        StringAssert.Contains(json, "\"NFToken\":{\"NFTokenID\":\"" + NFTokenID + "\",\"URI\":\"" + URI + "\"}");

        Meta reread = JsonSerializer.Deserialize<Meta>(json, XrplJsonOptions.Default);
        Assert.IsNotNull(reread);
        Assert.AreEqual(1, reread.AffectedNodes.Count);
    }
}
