using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Json;
using Xrpl.Models;
using Xrpl.Models.Transactions;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class TransactionResponseConverterTests
{
    private static readonly JsonSerializerOptions Options = XrplJsonOptions.Default;

    [TestMethod]
    public void Read_Payment_ReturnsPaymentResponse()
    {
        string json = @"{
            ""TransactionType"": ""Payment"",
            ""Account"": ""rSender"",
            ""Destination"": ""rDest"",
            ""Amount"": ""1000000"",
            ""Fee"": ""12"",
            ""Sequence"": 42
        }";
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(PaymentResponse));
    }

    [TestMethod]
    public void Read_AccountSet_ReturnsAccountSetResponse()
    {
        string json = @"{
            ""TransactionType"": ""AccountSet"",
            ""Account"": ""rTest"",
            ""Fee"": ""12"",
            ""Sequence"": 1
        }";
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(AccountSetResponse));
    }

    [TestMethod]
    public void Read_TrustSet_ReturnsTrustSetResponse()
    {
        string json = @"{
            ""TransactionType"": ""TrustSet"",
            ""Account"": ""rTest"",
            ""Fee"": ""12"",
            ""Sequence"": 1,
            ""LimitAmount"": {
                ""currency"": ""USD"",
                ""issuer"": ""rIssuer"",
                ""value"": ""100""
            }
        }";
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(TrustSetResponse));
    }

    [TestMethod]
    public void Read_OfferCreate_ReturnsOfferCreateResponse()
    {
        string json = @"{
            ""TransactionType"": ""OfferCreate"",
            ""Account"": ""rTest"",
            ""Fee"": ""12"",
            ""Sequence"": 1,
            ""TakerPays"": ""1000000"",
            ""TakerGets"": {""currency"": ""USD"", ""issuer"": ""rIssuer"", ""value"": ""10""}
        }";
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(OfferCreateResponse));
    }

    [TestMethod]
    public void Read_EscrowCreate_ReturnsEscrowCreateResponse()
    {
        string json = @"{
            ""TransactionType"": ""EscrowCreate"",
            ""Account"": ""rTest"",
            ""Fee"": ""12"",
            ""Sequence"": 1,
            ""Amount"": ""1000000"",
            ""Destination"": ""rDest""
        }";
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(EscrowCreateResponse));
    }

    [TestMethod]
    public void Read_NFTokenMint_ReturnsNFTokenMintResponse()
    {
        string json = @"{
            ""TransactionType"": ""NFTokenMint"",
            ""Account"": ""rTest"",
            ""Fee"": ""12"",
            ""Sequence"": 1,
            ""NFTokenTaxon"": 0
        }";
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(NFTokenMintResponse));
    }

    [TestMethod]
    public void Read_AMMCreate_ReturnsAMMCreateResponse()
    {
        string json = @"{
            ""TransactionType"": ""AMMCreate"",
            ""Account"": ""rTest"",
            ""Fee"": ""12"",
            ""Sequence"": 1,
            ""Amount"": ""1000000"",
            ""Amount2"": {""currency"": ""USD"", ""issuer"": ""rIssuer"", ""value"": ""100""},
            ""TradingFee"": 500
        }";
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(AMMCreateResponse));
    }

    [TestMethod]
    public void Read_CredentialCreate_ReturnsCredentialCreateResponse()
    {
        string json = @"{
            ""TransactionType"": ""CredentialCreate"",
            ""Account"": ""rIssuer"",
            ""Fee"": ""12"",
            ""Sequence"": 1,
            ""Subject"": ""rSubject"",
            ""CredentialType"": ""4B5943""
        }";
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(CredentialCreateResponse));
    }

    [TestMethod]
    public void Read_Batch_ReturnsBatchResponse()
    {
        string json = @"{
            ""TransactionType"": ""Batch"",
            ""Account"": ""rTest"",
            ""Fee"": ""12"",
            ""Sequence"": 1,
            ""RawTransactions"": []
        }";
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(BatchResponse));
    }

    [TestMethod]
    public void Read_UnknownType_ReturnsTransactionResponse()
    {
        string json = @"{
            ""TransactionType"": ""FutureTransaction"",
            ""Account"": ""rTest"",
            ""Fee"": ""12"",
            ""Sequence"": 1
        }";
        ITransactionResponse result = JsonSerializer.Deserialize<ITransactionResponse>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(TransactionResponse));
        Assert.AreEqual(TransactionType.Unknown, result.TransactionType);
        Assert.AreEqual("rTest", result.Account);
        Assert.AreEqual("12", result.Fee);
        Assert.AreEqual(1u, result.Sequence);
    }
}
