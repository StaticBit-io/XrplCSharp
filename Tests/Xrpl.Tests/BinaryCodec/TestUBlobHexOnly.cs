using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Text.Json.Nodes;

using Xrpl.BinaryCodec;
using Xrpl.Wallet;

namespace XrplTests.BinaryCodec;

/// <summary>
/// A Blob field takes hex and nothing else (#271).
/// </summary>
/// <remarks>
/// A value that was not hex used to be encoded as ASCII, with every non-ASCII character becoming
/// <c>?</c>: a caller signing a transaction from an external source signed bytes other than the
/// value it received and showed, with no signal. rippled and xrpl.js refuse such a value.
/// </remarks>
[TestClass]
public class TestUBlobHexOnly
{
    private const string Seed = "snGHNrPbHrdUcszeuDEigMdC1Lyyd";

    private static Dictionary<string, object> AccountSet(XrplWallet wallet) => new Dictionary<string, object>
    {
        { "TransactionType", "AccountSet" },
        { "Account", wallet.ClassicAddress },
        { "Fee", "12" },
        { "Sequence", 1u },
        { "LastLedgerSequence", 100u },
    };

    private static Dictionary<string, object> WithMemoData(Dictionary<string, object> tx, string memoData)
    {
        tx["Memos"] = new List<object>
        {
            new Dictionary<string, object>
            {
                { "Memo", new Dictionary<string, object> { { "MemoData", memoData } } },
            },
        };
        return tx;
    }

    [DataTestMethod]
    [DataRow("ABC")]
    [DataRow("Привет")]
    [DataRow("72656E7")]
    [DataRow("0x72656E74")]
    public void TestUNonHexMemoDataIsRefused(string memoData)
    {
        XrplWallet wallet = XrplWallet.FromSeed(Seed);
        Dictionary<string, object> tx = WithMemoData(AccountSet(wallet), memoData);

        InvalidJsonException error = Assert.ThrowsExactly<InvalidJsonException>(() => wallet.Sign(tx));
        StringAssert.Contains(error.Message, "MemoData", "the error has to name the field");
    }

    [TestMethod]
    public void TestUNonHexDomainIsRefused()
    {
        XrplWallet wallet = XrplWallet.FromSeed(Seed);
        Dictionary<string, object> tx = AccountSet(wallet);
        tx["Domain"] = "пример.рф";

        InvalidJsonException error = Assert.ThrowsExactly<InvalidJsonException>(() => wallet.Sign(tx));
        StringAssert.Contains(error.Message, "Domain");
    }

    /// <summary>
    /// Hex in either case is the bytes it spells, written back in rippled's UPPERCASE.
    /// </summary>
    [TestMethod]
    public void TestUHexIsDecodedInEitherCase()
    {
        XrplWallet wallet = XrplWallet.FromSeed(Seed);
        string blob = XrplBinaryCodec.Encode(WithMemoData(AccountSet(wallet), "c328ff"));

        JsonNode decoded = XrplBinaryCodec.Decode(blob);
        Assert.AreEqual("C328FF", decoded["Memos"]![0]!["Memo"]!["MemoData"]!.GetValue<string>());
    }

    /// <summary>
    /// An empty string is zero bytes, which is how AccountSet clears a Domain.
    /// </summary>
    [TestMethod]
    public void TestUEmptyStringIsZeroBytes()
    {
        XrplWallet wallet = XrplWallet.FromSeed(Seed);
        Dictionary<string, object> tx = AccountSet(wallet);
        tx["Domain"] = "";

        JsonNode decoded = XrplBinaryCodec.Decode(XrplBinaryCodec.Encode(tx));
        Assert.AreEqual("", decoded["Domain"]!.GetValue<string>());
    }
}
