using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Text.Json;

using Xrpl.Client.Json;
using Xrpl.Client.Json.Converters;
using Xrpl.Models.Methods;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class TestULONFTokenConverter
{
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
        Assert.AreEqual("000800006203F49C21D5D6E022CB16DE3538F248662FC73C29ABA6A90000000D", result.NFTokenID);
        Assert.AreEqual("68747470733A2F2F6578616D706C652E636F6D", result.URI);
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
        Assert.AreEqual("000800006203F49C21D5D6E022CB16DE3538F248662FC73C29ABA6A90000000D", result.NFTokenID);
        Assert.IsNull(result.URI);
    }
}
