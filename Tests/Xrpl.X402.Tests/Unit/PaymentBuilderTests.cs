using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Models.Transactions;
using Xrpl.X402;
using Xrpl.X402.Wire;

namespace Xrpl.X402.Tests.Unit;

[TestClass]
public class PaymentBuilderTests
{
    // Plain-string invoiceId — any non-empty string is valid now (not required to be 64-hex)
    private const string PlainInvoiceId = "inv-9";
    private const string PlainInvoiceId2 = "inv-live-001";

    // Helper: compute expected InvoiceID = SHA-256(UTF-8(s)).ToUpperHex
    private static string ExpectedInvoiceIdField(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    // Helper: compute expected MemoData = UTF-8(s).ToUpperHex
    private static string ExpectedMemoData(string s)
        => Convert.ToHexString(Encoding.UTF8.GetBytes(s));

    // ── Default mode: Both ────────────────────────────────────────────────────

    [TestMethod]
    public void TestUDefaultBothModeSetsInvoiceIdFieldAndMemo()
    {
        // Default IntentBinding = Both: sets Payment.InvoiceID = SHA-256(inv) AND Memo.MemoData = UTF8-hex(inv)
        PaymentRequirement req = new()
        {
            Scheme = "exact", Network = "xrpl:1", Asset = "XRP",
            PayTo = "rMerchant", Amount = "1000000", MaxTimeoutSeconds = 60,
            Extra = new()
            {
                ["invoiceId"] = JsonDocument.Parse($"\"{PlainInvoiceId}\"").RootElement,
            }
        };

        Payment p = X402PaymentBuilder.Build(req, payerAddress: "rPayer");

        Assert.AreEqual("rPayer", p.Account);
        Assert.AreEqual("rMerchant", p.Destination);
        Assert.AreEqual("1000000", p.Amount.Value);

        // InvoiceID field = SHA-256(UTF-8("inv-9")) uppercase
        Assert.AreEqual(ExpectedInvoiceIdField(PlainInvoiceId), p.InvoiceID,
            "InvoiceID must be SHA-256(UTF-8(inv)) uppercase hex");

        // Memo: only MemoData, no MemoType or MemoFormat
        Assert.IsNotNull(p.Memos, "Both mode must set Memos");
        Assert.AreEqual(1, p.Memos.Count);
        Assert.AreEqual(ExpectedMemoData(PlainInvoiceId), p.Memos[0].Memo.MemoData,
            "MemoData must be UTF-8 hex of raw invoiceId");
        Assert.IsNull(p.Memos[0].Memo.MemoType, "MemoType must be null (t54 uses only MemoData)");
        Assert.IsNull(p.Memos[0].Memo.MemoFormat, "MemoFormat must be null (t54 uses only MemoData)");
    }

    [TestMethod]
    public void TestUDefaultBothModeNoSourceTagWhenAbsentFromExtra()
    {
        // No sourceTag in extra — SourceTag must NOT be set (t54: no hardcoded default)
        PaymentRequirement req = new()
        {
            Asset = "XRP", PayTo = "rM", Amount = "1",
            Extra = new() { ["invoiceId"] = JsonDocument.Parse($"\"{PlainInvoiceId}\"").RootElement }
        };
        Payment p = X402PaymentBuilder.Build(req, "rP");
        Assert.IsNull(p.SourceTag, "SourceTag must be null when not in extra");
    }

    [TestMethod]
    public void TestUSourceTagSetFromExtraWhenPresent()
    {
        // sourceTag present in extra => must be applied to Payment
        PaymentRequirement req = new()
        {
            Asset = "XRP", PayTo = "rM", Amount = "1",
            Extra = new()
            {
                ["invoiceId"] = JsonDocument.Parse($"\"{PlainInvoiceId}\"").RootElement,
                ["sourceTag"] = JsonDocument.Parse("12345").RootElement
            }
        };
        Payment p = X402PaymentBuilder.Build(req, "rP");
        Assert.AreEqual(12345u, p.SourceTag, "SourceTag must be read from extra.sourceTag");
    }

    [TestMethod]
    public void TestUDestinationTagSetFromExtraWhenPresent()
    {
        PaymentRequirement req = new()
        {
            Asset = "XRP", PayTo = "rM", Amount = "1",
            Extra = new()
            {
                ["invoiceId"] = JsonDocument.Parse($"\"{PlainInvoiceId}\"").RootElement,
                ["destinationTag"] = JsonDocument.Parse("99").RootElement
            }
        };
        Payment p = X402PaymentBuilder.Build(req, "rP");
        Assert.AreEqual(99u, p.DestinationTag, "DestinationTag must be read from extra.destinationTag");
    }

    // ── SHA-256 correctness ───────────────────────────────────────────────────

    [TestMethod]
    public void TestUInvoiceIdFieldEqualsExpectedSha256()
    {
        // Compute SHA-256("inv-9") in-test and compare
        string expected = ExpectedInvoiceIdField(PlainInvoiceId);
        PaymentRequirement req = new()
        {
            Asset = "XRP", PayTo = "rM", Amount = "1",
            Extra = new() { ["invoiceId"] = JsonDocument.Parse($"\"{PlainInvoiceId}\"").RootElement }
        };
        Payment p = X402PaymentBuilder.Build(req, "rP");
        Assert.AreEqual(expected, p.InvoiceID,
            "Payment.InvoiceID must equal SHA-256(UTF-8(invoiceId)) uppercase hex");
    }

    [TestMethod]
    public void TestUInvoiceIdFieldEqualsExpectedSha256ForLiveLikeId()
    {
        string expected = ExpectedInvoiceIdField(PlainInvoiceId2);
        PaymentRequirement req = new()
        {
            Asset = "XRP", PayTo = "rM", Amount = "1",
            Extra = new() { ["invoiceId"] = JsonDocument.Parse($"\"{PlainInvoiceId2}\"").RootElement }
        };
        Payment p = X402PaymentBuilder.Build(req, "rP");
        Assert.AreEqual(expected, p.InvoiceID);
    }

    // ── InvoiceIdField-only mode ──────────────────────────────────────────────

    [TestMethod]
    public void TestUInvoiceIdFieldModeOnlySetsInvoiceIdField()
    {
        X402ClientOptions opt = new() { IntentBinding = X402IntentBinding.InvoiceIdField };
        PaymentRequirement req = new()
        {
            Asset = "XRP", PayTo = "rM", Amount = "1",
            Extra = new() { ["invoiceId"] = JsonDocument.Parse($"\"{PlainInvoiceId}\"").RootElement }
        };
        Payment p = X402PaymentBuilder.Build(req, "rP", opt);

        Assert.AreEqual(ExpectedInvoiceIdField(PlainInvoiceId), p.InvoiceID);
        Assert.IsNull(p.Memos, "InvoiceIdField mode must not add Memos");
    }

    // ── Memo-only mode ────────────────────────────────────────────────────────

    [TestMethod]
    public void TestUMemoModeOnlySetsMemory()
    {
        X402ClientOptions opt = new() { IntentBinding = X402IntentBinding.Memo };
        PaymentRequirement req = new()
        {
            Asset = "XRP", PayTo = "rM", Amount = "1",
            Extra = new() { ["invoiceId"] = JsonDocument.Parse($"\"{PlainInvoiceId}\"").RootElement }
        };
        Payment p = X402PaymentBuilder.Build(req, "rP", opt);

        Assert.IsNull(p.InvoiceID, "Memo mode must not set InvoiceID");
        Assert.IsNotNull(p.Memos);
        Assert.AreEqual(1, p.Memos.Count);
        Assert.AreEqual(ExpectedMemoData(PlainInvoiceId), p.Memos[0].Memo.MemoData);
        Assert.IsNull(p.Memos[0].Memo.MemoType);
        Assert.IsNull(p.Memos[0].Memo.MemoFormat);
    }

    // ── IOU / RLUSD ───────────────────────────────────────────────────────────

    [TestMethod]
    public void TestUIouPaymentSetsSendMax()
    {
        const string Rlusd = "524C555344000000000000000000000000000000";
        PaymentRequirement req = new()
        {
            Scheme = "exact", Network = "xrpl:1",
            Asset = Rlusd, PayTo = "rMerchant", Amount = "2.5", MaxTimeoutSeconds = 60,
            Extra = new()
            {
                ["invoiceId"] = JsonDocument.Parse($"\"{PlainInvoiceId}\"").RootElement,
                ["issuer"]    = JsonDocument.Parse("\"rIssuer\"").RootElement
            }
        };

        Payment p = X402PaymentBuilder.Build(req, "rPayer");

        Assert.IsNotNull(p.SendMax, "IOU payment must set SendMax");
        Assert.AreEqual(Rlusd, p.SendMax.CurrencyCode);
        Assert.AreEqual("rIssuer", p.SendMax.Issuer);
        Assert.AreEqual("2.5", p.SendMax.Value);
        // Amount and SendMax must match
        Assert.AreEqual(p.Amount.CurrencyCode, p.SendMax.CurrencyCode);
        Assert.AreEqual(p.Amount.Value, p.SendMax.Value);
    }

    [TestMethod]
    public void TestUXrpPaymentNoSendMax()
    {
        PaymentRequirement req = new()
        {
            Asset = "XRP", PayTo = "rM", Amount = "1000000",
            Extra = new() { ["invoiceId"] = JsonDocument.Parse($"\"{PlainInvoiceId}\"").RootElement }
        };
        Payment p = X402PaymentBuilder.Build(req, "rP");
        Assert.IsNull(p.SendMax, "XRP payment must not set SendMax");
    }

    // ── Error cases ───────────────────────────────────────────────────────────

    [TestMethod]
    public void TestUThrowsWhenInvoiceIdMissing()
    {
        PaymentRequirement req = new() { Asset = "XRP", PayTo = "rM", Amount = "1" };
        Assert.ThrowsExactly<X402PaymentException>(() => X402PaymentBuilder.Build(req, "rP"));
    }

    // ── BuildWithInvoiceId returns resolved inv ────────────────────────────────

    [TestMethod]
    public void TestUBuildWithInvoiceIdReturnsRawInvoiceId()
    {
        PaymentRequirement req = new()
        {
            Asset = "XRP", PayTo = "rM", Amount = "1",
            Extra = new() { ["invoiceId"] = JsonDocument.Parse($"\"{PlainInvoiceId}\"").RootElement }
        };
        (Payment _, string inv) = X402PaymentBuilder.BuildWithInvoiceId(req, "rP");
        Assert.AreEqual(PlainInvoiceId, inv, "BuildWithInvoiceId must return the raw invoiceId string");
    }
}
