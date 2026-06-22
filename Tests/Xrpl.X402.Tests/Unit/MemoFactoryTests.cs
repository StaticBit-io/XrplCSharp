using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Xrpl.Models.Transactions;
using Xrpl.X402;

namespace Xrpl.X402.Tests.Unit;

[TestClass]
public class MemoFactoryTests
{
    [TestMethod]
    public void TestUBuildsHexEncodedX402Memo()
    {
        MemoWrapper wrapper = X402MemoFactory.Build("inv-123", sessionId: null);

        Assert.AreEqual(ToHex("x402"), wrapper.Memo.MemoType);
        Assert.AreEqual(ToHex("application/json"), wrapper.Memo.MemoFormat);

        string json = Encoding.UTF8.GetString(FromHex(wrapper.Memo.MemoData));
        StringAssert.Contains(json, "\"paymentId\":\"inv-123\"");
    }

    [TestMethod]
    public void TestUIncludesSessionIdWhenProvided()
    {
        MemoWrapper wrapper = X402MemoFactory.Build("inv-9", sessionId: "sess-1");
        string json = Encoding.UTF8.GetString(FromHex(wrapper.Memo.MemoData));
        StringAssert.Contains(json, "\"sessionId\":\"sess-1\"");
    }

    private static string ToHex(string s) => Convert.ToHexString(Encoding.UTF8.GetBytes(s));
    private static byte[] FromHex(string s) => Convert.FromHexString(s);
}
