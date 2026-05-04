using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Json;
using Xrpl.Models.Transactions;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class TransactionRequestConverterTests
{
    private static readonly JsonSerializerOptions Options = XrplJsonOptions.Default;

    [TestMethod]
    public void Read_Payment_ReturnsPaymentRequest()
    {
        string json = @"{
            ""TransactionType"": ""Payment"",
            ""Account"": ""rSender"",
            ""Destination"": ""rDest"",
            ""Amount"": ""1000000""
        }";
        ITransactionRequest result = JsonSerializer.Deserialize<ITransactionRequest>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(Payment));
        Payment payment = (Payment)result;
        Assert.AreEqual("rSender", payment.Account);
        Assert.AreEqual("rDest", payment.Destination);
    }

    [TestMethod]
    public void Read_AccountSet_ReturnsAccountSet()
    {
        string json = @"{
            ""TransactionType"": ""AccountSet"",
            ""Account"": ""rTest""
        }";
        ITransactionRequest result = JsonSerializer.Deserialize<ITransactionRequest>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(AccountSet));
    }

    [TestMethod]
    public void Read_TrustSet_ReturnsTrustSet()
    {
        string json = @"{
            ""TransactionType"": ""TrustSet"",
            ""Account"": ""rTest"",
            ""LimitAmount"": {""currency"": ""USD"", ""issuer"": ""rIssuer"", ""value"": ""100""}
        }";
        ITransactionRequest result = JsonSerializer.Deserialize<ITransactionRequest>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(TrustSet));
    }

    [TestMethod]
    public void Read_OfferCreate_ReturnsOfferCreate()
    {
        string json = @"{
            ""TransactionType"": ""OfferCreate"",
            ""Account"": ""rTest"",
            ""TakerPays"": ""1000000"",
            ""TakerGets"": {""currency"": ""USD"", ""issuer"": ""rIssuer"", ""value"": ""10""}
        }";
        ITransactionRequest result = JsonSerializer.Deserialize<ITransactionRequest>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(OfferCreate));
    }

    [TestMethod]
    public void Read_NFTokenMint_ReturnsNFTokenMint()
    {
        string json = @"{
            ""TransactionType"": ""NFTokenMint"",
            ""Account"": ""rTest"",
            ""NFTokenTaxon"": 0
        }";
        ITransactionRequest result = JsonSerializer.Deserialize<ITransactionRequest>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(NFTokenMint));
    }

    [TestMethod]
    public void Read_AMMCreate_ReturnsAMMCreate()
    {
        string json = @"{
            ""TransactionType"": ""AMMCreate"",
            ""Account"": ""rTest"",
            ""Amount"": ""1000000"",
            ""Amount2"": {""currency"": ""USD"", ""issuer"": ""rIssuer"", ""value"": ""100""},
            ""TradingFee"": 500
        }";
        ITransactionRequest result = JsonSerializer.Deserialize<ITransactionRequest>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(AMMCreate));
    }

    [TestMethod]
    public void Read_OracleSet_ReturnsOracleSet()
    {
        string json = @"{
            ""TransactionType"": ""OracleSet"",
            ""Account"": ""rTest""
        }";
        ITransactionRequest result = JsonSerializer.Deserialize<ITransactionRequest>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(OracleSet));
    }

    [TestMethod]
    public void Read_DIDSet_ReturnsDIDSet()
    {
        string json = @"{
            ""TransactionType"": ""DIDSet"",
            ""Account"": ""rTest""
        }";
        ITransactionRequest result = JsonSerializer.Deserialize<ITransactionRequest>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(DIDSet));
    }

    [TestMethod]
    public void Read_CredentialCreate_ReturnsCredentialCreate()
    {
        string json = @"{
            ""TransactionType"": ""CredentialCreate"",
            ""Account"": ""rIssuer"",
            ""Subject"": ""rSubject"",
            ""CredentialType"": ""4B5943""
        }";
        ITransactionRequest result = JsonSerializer.Deserialize<ITransactionRequest>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(CredentialCreate));
    }

    [TestMethod]
    public void Read_Batch_ReturnsBatch()
    {
        string json = @"{
            ""TransactionType"": ""Batch"",
            ""Account"": ""rTest"",
            ""RawTransactions"": []
        }";
        ITransactionRequest result = JsonSerializer.Deserialize<ITransactionRequest>(json, Options);
        Assert.IsNotNull(result);
        Assert.IsInstanceOfType(result, typeof(Batch));
    }

    [TestMethod]
    public void Read_UnknownType_ReturnsTransactionRequest()
    {
        string json = @"{
            ""TransactionType"": ""FutureTxType"",
            ""Account"": ""rTest""
        }";
        ITransactionRequest result = JsonSerializer.Deserialize<ITransactionRequest>(json, Options);
        Assert.IsNotNull(result);
    }
}
