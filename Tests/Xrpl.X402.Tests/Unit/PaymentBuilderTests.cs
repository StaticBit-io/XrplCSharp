using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using Xrpl.Models.Transactions;
using Xrpl.X402;
using Xrpl.X402.Wire;

namespace Xrpl.X402.Tests.Unit;

[TestClass]
public class PaymentBuilderTests
{
    [TestMethod]
    public void TestUBuildsXrpPaymentWithMemoAndSourceTag()
    {
        PaymentRequirement req = new()
        {
            Scheme = "exact", Network = "xrpl:1", Asset = "XRP",
            PayTo = "rMerchant", Amount = "1000000", MaxTimeoutSeconds = 60,
            Extra = new()
            {
                ["invoiceId"] = JsonDocument.Parse("\"inv-9\"").RootElement,
                ["sourceTag"] = JsonDocument.Parse("804681468").RootElement
            }
        };

        Payment p = X402PaymentBuilder.Build(req, payerAddress: "rPayer");

        Assert.AreEqual("rPayer", p.Account);
        Assert.AreEqual("rMerchant", p.Destination);
        Assert.AreEqual("XRP", p.Amount.CurrencyCode);
        Assert.AreEqual("1000000", p.Amount.Value);
        Assert.AreEqual(804681468u, p.SourceTag);
        Assert.IsNull(p.DestinationTag);
        Assert.AreEqual(1, p.Memos.Count);
    }

    [TestMethod]
    public void TestUDefaultsSourceTagWhenAbsent()
    {
        PaymentRequirement req = new()
        {
            Asset = "XRP", PayTo = "rM", Amount = "1",
            Extra = new() { ["invoiceId"] = JsonDocument.Parse("\"i\"").RootElement }
        };
        Payment p = X402PaymentBuilder.Build(req, "rP");
        Assert.AreEqual(804681468u, p.SourceTag);
    }

    [TestMethod]
    public void TestUThrowsWhenInvoiceIdMissing()
    {
        PaymentRequirement req = new() { Asset = "XRP", PayTo = "rM", Amount = "1" };
        Assert.ThrowsExactly<X402PaymentException>(() => X402PaymentBuilder.Build(req, "rP"));
    }
}
