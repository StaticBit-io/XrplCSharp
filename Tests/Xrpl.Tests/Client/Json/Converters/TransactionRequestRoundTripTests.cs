using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client.Json;
using Xrpl.Models.Common;
using Xrpl.Models.Transactions;

namespace XrplTests.Client.Json.Converters;

[TestClass]
public class TestUTransactionRequestRoundTrip
{
    private static readonly JsonSerializerOptions Options = XrplJsonOptions.Default;

    [TestMethod]
    public void RoundTrip_Payment_PreservesData()
    {
        var original = new Payment
        {
            Account = "rSender",
            Destination = "rDest",
            Amount = new Currency { Value = "1000000" }
        };

        string json = JsonSerializer.Serialize(original, original.GetType(), Options);
        ITransactionRequest deserialized = JsonSerializer.Deserialize<ITransactionRequest>(json, Options);

        Assert.IsNotNull(deserialized);
        Assert.IsInstanceOfType(deserialized, typeof(Payment));
        Payment payment = (Payment)deserialized;
        Assert.AreEqual("rSender", payment.Account);
        Assert.AreEqual("rDest", payment.Destination);
    }

    [TestMethod]
    public void RoundTrip_TrustSet_PreservesData()
    {
        var original = new TrustSet
        {
            Account = "rTest",
            LimitAmount = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "100" }
        };

        string json = JsonSerializer.Serialize(original, original.GetType(), Options);
        ITransactionRequest deserialized = JsonSerializer.Deserialize<ITransactionRequest>(json, Options);

        Assert.IsNotNull(deserialized);
        Assert.IsInstanceOfType(deserialized, typeof(TrustSet));
        TrustSet trustSet = (TrustSet)deserialized;
        Assert.AreEqual("rTest", trustSet.Account);
        Assert.IsNotNull(trustSet.LimitAmount);
        Assert.AreEqual("USD", trustSet.LimitAmount.CurrencyCode);
    }

    [TestMethod]
    public void RoundTrip_OfferCreate_PreservesData()
    {
        var original = new OfferCreate
        {
            Account = "rTest",
            TakerPays = new Currency { Value = "1000000" },
            TakerGets = new Currency { CurrencyCode = "USD", Issuer = "rIssuer", Value = "10" }
        };

        string json = JsonSerializer.Serialize(original, original.GetType(), Options);
        ITransactionRequest deserialized = JsonSerializer.Deserialize<ITransactionRequest>(json, Options);

        Assert.IsNotNull(deserialized);
        Assert.IsInstanceOfType(deserialized, typeof(OfferCreate));
        OfferCreate offer = (OfferCreate)deserialized;
        Assert.AreEqual("rTest", offer.Account);
    }

    [TestMethod]
    public void Write_Payment_ViaInterface_SerializesAllFields()
    {
        ITransactionRequest tx = new Payment
        {
            Account = "rSender",
            Destination = "rDest",
            Amount = new Currency { Value = "1000000" }
        };

        string json = JsonSerializer.Serialize(tx, tx.GetType(), Options);

        Assert.IsTrue(json.Contains("\"Account\""), "Missing Account");
        Assert.IsTrue(json.Contains("\"Destination\""), "Missing Destination");
        Assert.IsTrue(json.Contains("\"Amount\""), "Missing Amount");
        Assert.IsTrue(json.Contains("\"TransactionType\""), "Missing TransactionType");
    }

    [TestMethod]
    public void Write_AccountSet_ViaInterface_SerializesAllFields()
    {
        ITransactionRequest tx = new AccountSet
        {
            Account = "rTest",
            SetFlag = AccountSetAsfFlags.asfDefaultRipple
        };

        string json = JsonSerializer.Serialize(tx, tx.GetType(), Options);

        Assert.IsTrue(json.Contains("\"Account\""), "Missing Account");
        Assert.IsTrue(json.Contains("rTest"), "Missing account value");
    }
}
