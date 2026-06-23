using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using Xrpl.Models.Transactions;
using Xrpl.X402;
using Xrpl.X402.Wire;

namespace Xrpl.X402.Tests.Unit;

[TestClass]
public class PaymentBuilderTests
{
    // 64-hex invoiceId for InvoiceIdField (default) mode tests
    private const string HexInvoiceId = "A7F9C76B2EAC41A9B2D500AA76B8FA1800000000000000000000000000000001";

    [TestMethod]
    public void TestUBuildsXrpPaymentWithInvoiceIdFieldAndSourceTag()
    {
        // Default mode: InvoiceIdField — sets Payment.InvoiceID, no Memo
        PaymentRequirement req = new()
        {
            Scheme = "exact", Network = "xrpl:1", Asset = "XRP",
            PayTo = "rMerchant", Amount = "1000000", MaxTimeoutSeconds = 60,
            Extra = new()
            {
                ["invoiceId"] = JsonDocument.Parse($"\"{HexInvoiceId}\"").RootElement,
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
        Assert.AreEqual(HexInvoiceId.ToUpperInvariant(), p.InvoiceID);
        Assert.IsNull(p.Memos, "InvoiceIdField mode must not add Memos");
    }

    [TestMethod]
    public void TestUBuildsXrpPaymentWithMemoModeAndSourceTag()
    {
        // Memo mode: no InvoiceID field, adds a Memo
        X402ClientOptions memoOptions = new() { IntentBinding = X402IntentBinding.Memo };
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

        Payment p = X402PaymentBuilder.Build(req, payerAddress: "rPayer", memoOptions);

        Assert.AreEqual("rPayer", p.Account);
        Assert.AreEqual("rMerchant", p.Destination);
        Assert.AreEqual("XRP", p.Amount.CurrencyCode);
        Assert.AreEqual("1000000", p.Amount.Value);
        Assert.AreEqual(804681468u, p.SourceTag);
        Assert.IsNull(p.DestinationTag);
        Assert.AreEqual(1, p.Memos.Count);
        Assert.IsNull(p.InvoiceID, "Memo mode must not set InvoiceID");
    }

    [TestMethod]
    public void TestUDefaultsSourceTagWhenAbsent()
    {
        // Use Memo mode so non-hex invoiceId is accepted
        X402ClientOptions memoOptions = new() { IntentBinding = X402IntentBinding.Memo };
        PaymentRequirement req = new()
        {
            Asset = "XRP", PayTo = "rM", Amount = "1",
            Extra = new() { ["invoiceId"] = JsonDocument.Parse("\"i\"").RootElement }
        };
        Payment p = X402PaymentBuilder.Build(req, "rP", memoOptions);
        Assert.AreEqual(804681468u, p.SourceTag);
    }

    [TestMethod]
    public void TestUThrowsWhenInvoiceIdMissing()
    {
        PaymentRequirement req = new() { Asset = "XRP", PayTo = "rM", Amount = "1" };
        Assert.ThrowsExactly<X402PaymentException>(() => X402PaymentBuilder.Build(req, "rP"));
    }

    [TestMethod]
    public void TestUThrowsWhenInvoiceIdNotHexInInvoiceIdFieldMode()
    {
        // Default InvoiceIdField mode rejects non-hex invoiceId
        PaymentRequirement req = new()
        {
            Asset = "XRP", PayTo = "rM", Amount = "1",
            Extra = new() { ["invoiceId"] = JsonDocument.Parse("\"not-hex-at-all\"").RootElement }
        };
        X402PaymentException ex = Assert.ThrowsExactly<X402PaymentException>(() => X402PaymentBuilder.Build(req, "rP"));
        Assert.AreEqual("invalid_requirement", ex.Reason);
    }

    [TestMethod]
    public void TestUInvoiceIdFieldModeUppercasesHexValue()
    {
        string lowerHex = "a7f9c76b2eac41a9b2d500aa76b8fa1800000000000000000000000000000001";
        PaymentRequirement req = new()
        {
            Asset = "XRP", PayTo = "rM", Amount = "1",
            Extra = new() { ["invoiceId"] = JsonDocument.Parse($"\"{lowerHex}\"").RootElement }
        };
        Payment p = X402PaymentBuilder.Build(req, "rP");
        Assert.AreEqual(lowerHex.ToUpperInvariant(), p.InvoiceID);
    }
}
