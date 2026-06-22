using System;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Models.Transactions;
using Xrpl.X402;
using Xrpl.X402.Wire;

namespace Xrpl.X402.Tests.Unit;

[TestClass]
public class ConfigurableFieldNamesTests
{
    [TestMethod]
    public void TestUBuilderHonorsConfiguredFieldNames()
    {
        // Read the id from extra["paymentId"] and write it into memo field "invoiceId" (swapped vs defaults).
        X402ClientOptions opt = new()
        {
            InvoiceIdExtraKey = "paymentId",
            MemoPaymentIdField = "invoiceId"
        };
        PaymentRequirement req = new()
        {
            Asset = "XRP", PayTo = "rM", Amount = "1",
            Extra = new() { ["paymentId"] = JsonDocument.Parse("\"abc-123\"").RootElement }
        };

        Payment p = X402PaymentBuilder.Build(req, "rPayer", opt);

        string json = Encoding.UTF8.GetString(Convert.FromHexString(p.Memos[0].Memo.MemoData));
        StringAssert.Contains(json, "\"invoiceId\":\"abc-123\"");
    }
}
