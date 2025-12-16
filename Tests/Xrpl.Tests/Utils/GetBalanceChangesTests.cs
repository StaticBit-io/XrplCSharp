using Microsoft.VisualStudio.TestTools.UnitTesting;

using Newtonsoft.Json;

using System.Linq;

using Xrpl.Models.Transactions;
using Xrpl.Utils;

namespace XrplTests.Utils;
[TestClass]
public class GetBalanceChangesTests
{
    [TestMethod]
    public void TestBalanceChanges_Metadata1()
    {
        // Arrange
        var metadata = JsonConvert.DeserializeObject<Meta>(metaData_1);
        var buyer = "rLiooJRSKeiNfRJcDBUhu4rcjQjGLWqa4p";
        var seller = "rnFApzSsKwXyTZtci4Z6nLVL8E1nLZzSBF";
        var issuer = "rXPMxBeefHGxx2K7g5qmmWq3gFsgawkoa";

        // Act
        var changes = BalanceChanges.GetBalanceChanges(metadata);

        var currencyCode = "XPM";
        var buyerChanges = changes[buyer];
        var sellerChanges = changes[seller];
        var issuerChanges = changes[issuer];

        var buyerXrpChanges = buyerChanges.FirstOrDefault(c => c.CurrencyCode == "XRP");
        var buyerTokenChanges = buyerChanges.FirstOrDefault(c => c.CurrencyCode != "XRP");

        var sellerXrpChanges = sellerChanges.FirstOrDefault(c => c.CurrencyCode == "XRP");
        var sellerTokenChanges = sellerChanges.FirstOrDefault(c => c.CurrencyCode != "XRP");

        var issuerXrpChanges = issuerChanges.FirstOrDefault(c => c.CurrencyCode == "XRP");
        var issuerTokenBuyerChanges = issuerChanges.FirstOrDefault(c => c.Issuer == buyer);
        var issuerTokenSellerChanges = issuerChanges.FirstOrDefault(c => c.Issuer == seller);

        // Assert
        Assert.HasCount(3, changes);

        // Check XRP balance changes
        Assert.HasCount(2, sellerChanges);
        Assert.AreEqual("XRP", sellerXrpChanges.CurrencyCode);
        Assert.AreEqual("8755899", sellerXrpChanges.Value);
        Assert.AreEqual(currencyCode, sellerTokenChanges.CurrencyCode);
        Assert.AreEqual(issuer, sellerTokenChanges.Issuer);
        Assert.AreEqual("-639.11146415997", sellerTokenChanges.Value);

        Assert.HasCount(2, buyerChanges);
        Assert.AreEqual("XRP", buyerXrpChanges.CurrencyCode);
        Assert.AreEqual("-8755911", buyerXrpChanges.Value);

        // Check XPM balance changes
        Assert.AreEqual("639.11146416", buyerTokenChanges.Value);
        Assert.AreEqual(issuer, buyerTokenChanges.Issuer);
        Assert.AreEqual(currencyCode, buyerTokenChanges.CurrencyCode);

        Assert.HasCount(2, issuerChanges);
        Assert.IsNull(issuerXrpChanges);
        Assert.AreEqual("639.11146415997", issuerTokenSellerChanges.Value);
        Assert.AreEqual(seller, issuerTokenSellerChanges.Issuer);
        Assert.AreEqual("-639.11146416", issuerTokenBuyerChanges.Value);
        Assert.AreEqual(buyer, issuerTokenBuyerChanges.Issuer);
    }
    private const string metaData_1 = @"{
    ""AffectedNodes"": [
        {
            ""ModifiedNode"": {
                ""FinalFields"": {
                    ""Account"": ""rnFApzSsKwXyTZtci4Z6nLVL8E1nLZzSBF"",
                    ""Balance"": ""16535178"",
                    ""Flags"": 0,
                    ""OwnerCount"": 21,
                    ""Sequence"": 75028676
                },
                ""LedgerEntryType"": ""AccountRoot"",
                ""LedgerIndex"": ""193CC0FF109E95DCC5C6A194062C8DA5D6AF686B58F25F37741C9F832239A220"",
                ""PreviousFields"": {
                    ""Balance"": ""7779279"",
                    ""Sequence"": 75028675
                },
                ""PreviousTxnID"": ""A136A8CC0469C43331C182A21B830B662A7C7C610111DD071AE6008952BD16A5"",
                ""PreviousTxnLgrSeq"": 96288837
            }
        },
        {
            ""ModifiedNode"": {
                ""FinalFields"": {
                    ""Balance"": {
                        ""currency"": ""XPM"",
                        ""issuer"": ""rrrrrrrrrrrrrrrrrrrrBZbvji"",
                        ""value"": ""-440384.9706391713""
                    },
                    ""Flags"": 2228224,
                    ""HighLimit"": {
                        ""currency"": ""XPM"",
                        ""issuer"": ""rLiooJRSKeiNfRJcDBUhu4rcjQjGLWqa4p"",
                        ""value"": ""500000000""
                    },
                    ""HighNode"": ""147"",
                    ""LowLimit"": {
                        ""currency"": ""XPM"",
                        ""issuer"": ""rXPMxBeefHGxx2K7g5qmmWq3gFsgawkoa"",
                        ""value"": ""0""
                    },
                    ""LowNode"": ""0""
                },
                ""LedgerEntryType"": ""RippleState"",
                ""LedgerIndex"": ""59A72090180763A223F2488E91D17DA8790E36EAD3E57B8324E9FD83EB59D33D"",
                ""PreviousFields"": {
                    ""Balance"": {
                        ""currency"": ""XPM"",
                        ""issuer"": ""rrrrrrrrrrrrrrrrrrrrBZbvji"",
                        ""value"": ""-439745.8591750113""
                    }
                },
                ""PreviousTxnID"": ""7FA73025DE9809236787B40D800B399D2F3AA9EA761C8B6EB73546D5FBD55186"",
                ""PreviousTxnLgrSeq"": 96288694
            }
        },
        {
            ""ModifiedNode"": {
                ""FinalFields"": {
                    ""Balance"": {
                        ""currency"": ""XPM"",
                        ""issuer"": ""rrrrrrrrrrrrrrrrrrrrBZbvji"",
                        ""value"": ""0""
                    },
                    ""Flags"": 2228224,
                    ""HighLimit"": {
                        ""currency"": ""XPM"",
                        ""issuer"": ""rnFApzSsKwXyTZtci4Z6nLVL8E1nLZzSBF"",
                        ""value"": ""500000000""
                    },
                    ""HighNode"": ""0"",
                    ""LowLimit"": {
                        ""currency"": ""XPM"",
                        ""issuer"": ""rXPMxBeefHGxx2K7g5qmmWq3gFsgawkoa"",
                        ""value"": ""0""
                    },
                    ""LowNode"": ""28""
                },
                ""LedgerEntryType"": ""RippleState"",
                ""LedgerIndex"": ""775C31901E6ED29BD86449D84E8B7C32C1690D4CFD8B60951A1F563631D3EFEC"",
                ""PreviousFields"": {
                    ""Balance"": {
                        ""currency"": ""XPM"",
                        ""issuer"": ""rrrrrrrrrrrrrrrrrrrrBZbvji"",
                        ""value"": ""-639.1114641599702""
                    }
                },
                ""PreviousTxnID"": ""D35D35ABF80E01A3C6257DD381AB7646317DB1068F41429B253F2D8AD34955D5"",
                ""PreviousTxnLgrSeq"": 96288844
            }
        },
        {
            ""ModifiedNode"": {
                ""FinalFields"": {
                    ""Account"": ""rLiooJRSKeiNfRJcDBUhu4rcjQjGLWqa4p"",
                    ""Balance"": ""856234677"",
                    ""BurnedNFTokens"": 5,
                    ""EmailHash"": ""30AB0CDE4381F4B04E5E9BA261EC43BC"",
                    ""Flags"": 0,
                    ""MintedNFTokens"": 6,
                    ""OwnerCount"": 194,
                    ""Sequence"": 68298395,
                    ""TicketCount"": 10
                },
                ""LedgerEntryType"": ""AccountRoot"",
                ""LedgerIndex"": ""802D506AE9B26779593CADB94D0338F76A26C56CA14769F630791873295DD345"",
                ""PreviousFields"": {
                    ""Balance"": ""864990588""
                },
                ""PreviousTxnID"": ""7FA73025DE9809236787B40D800B399D2F3AA9EA761C8B6EB73546D5FBD55186"",
                ""PreviousTxnLgrSeq"": 96288694
            }
        },
        {
            ""ModifiedNode"": {
                ""FinalFields"": {
                    ""Account"": ""rLiooJRSKeiNfRJcDBUhu4rcjQjGLWqa4p"",
                    ""BookDirectory"": ""AAB29FD34DF2C06D4898503CD4F05649E7E4C0203E3EA1B85019EE956F2F43B7"",
                    ""BookNode"": ""0"",
                    ""Flags"": 0,
                    ""OwnerNode"": ""16b"",
                    ""Sequence"": 68298032,
                    ""TakerGets"": ""922691406"",
                    ""TakerPays"": {
                        ""currency"": ""XPM"",
                        ""issuer"": ""rXPMxBeefHGxx2K7g5qmmWq3gFsgawkoa"",
                        ""value"": ""67349.09039103124""
                    }
                },
                ""LedgerEntryType"": ""Offer"",
                ""LedgerIndex"": ""99190AA9074D541395ECB52F90F1084C0D0096CCF6B14FA7D5EC42904794ED72"",
                ""PreviousFields"": {
                    ""TakerGets"": ""931447317"",
                    ""TakerPays"": {
                        ""currency"": ""XPM"",
                        ""issuer"": ""rXPMxBeefHGxx2K7g5qmmWq3gFsgawkoa"",
                        ""value"": ""67988.20185519121""
                    }
                },
                ""PreviousTxnID"": ""7FA73025DE9809236787B40D800B399D2F3AA9EA761C8B6EB73546D5FBD55186"",
                ""PreviousTxnLgrSeq"": 96288694
            }
        }
    ],
    ""DeliveredAmount"": ""8755911"",
    ""TransactionIndex"": 39,
    ""TransactionResult"": ""tesSUCCESS"",
    ""delivered_amount"": ""8755911""
}";
}
