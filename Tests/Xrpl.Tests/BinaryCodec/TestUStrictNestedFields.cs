using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Collections.Generic;
using System.Text.Json.Nodes;

using Xrpl.BinaryCodec;
using Xrpl.BinaryCodec.Types;
using Xrpl.Models.Utils;
using Xrpl.Wallet;

namespace XrplTests.BinaryCodec;

/// <summary>
/// A member this codec does not know is refused at every level of a transaction being signed, not
/// only at the top.
/// </summary>
/// <remarks>
/// The top level always failed loudly. One level down it did not: nested objects reached
/// <c>StObject.FromJson</c> through a dispatch table whose delegate carries no strictness flag, so
/// they went through the lenient overload and the member was dropped without a word. A caller could
/// be shown a transaction carrying a member - a typo, or a field from an amendment newer than this
/// SDK's <c>definitions.json</c> - sign it, and put it in the ledger without that member. Nothing
/// reported anything.
/// <para>
/// rippled refuses such a transaction outright: <c>STParsedJSON::parseObject</c> recurses and
/// answers <c>unknownField</c> at every level.
/// </para>
/// </remarks>
[TestClass]
public class TestUStrictNestedFields
{
    private const string Seed = "snGHNrPbHrdUcszeuDEigMdC1Lyyd";

    private static Dictionary<string, object> Payment(XrplWallet wallet) => new Dictionary<string, object>
    {
        { "TransactionType", "Payment" },
        { "Account", wallet.ClassicAddress },
        { "Destination", "rQ3fNyLjbvcDaPNS4EAJY8aT9zR3uGk17c" },
        { "Amount", "1000" },
        { "Fee", "12" },
        { "Sequence", 1u },
        { "LastLedgerSequence", 100u },
    };

    /// <summary>
    /// A member inside an object inside an array - the shape the report was filed against.
    /// </summary>
    /// <remarks>
    /// <c>Memos</c> is an array of objects, so <c>Memos[0].Memo.MemoBogusField</c> sits two levels
    /// below the top and needs both the array and the object to carry the flag down.
    /// </remarks>
    [TestMethod]
    public void TestUUnknownFieldInsideAMemoIsRefused()
    {
        XrplWallet wallet = XrplWallet.FromSeed(Seed);
        Dictionary<string, object> tx = Payment(wallet);
        tx["Memos"] = new List<object>
        {
            new Dictionary<string, object>
            {
                {
                    "Memo", new Dictionary<string, object>
                    {
                        { "MemoData", "72656E74" },
                        { "MemoBogusField", "1" },
                    }
                },
            },
        };

        InvalidJsonException error = Assert.ThrowsExactly<InvalidJsonException>(
            () => wallet.Sign(tx),
            "signing accepted a member the codec does not know, and would have dropped it from the blob");

        StringAssert.Contains(error.Message, "MemoBogusField",
            "the error has to name the member, or a caller cannot tell which one it was");
    }

    /// <summary>
    /// The top level, which always behaved - kept so the recursion cannot be "fixed" by moving the
    /// check downward.
    /// </summary>
    [TestMethod]
    public void TestUUnknownFieldAtTheTopLevelIsRefused()
    {
        XrplWallet wallet = XrplWallet.FromSeed(Seed);
        Dictionary<string, object> tx = Payment(wallet);
        tx["BogusTop"] = "1";

        InvalidJsonException error = Assert.ThrowsExactly<InvalidJsonException>(() => wallet.Sign(tx));
        StringAssert.Contains(error.Message, "BogusTop");
    }

    /// <summary>
    /// One level down, without an array in the way.
    /// </summary>
    [TestMethod]
    public void TestUUnknownFieldInsideAPlainObjectIsRefused()
    {
        XrplWallet wallet = XrplWallet.FromSeed(Seed);
        Dictionary<string, object> tx = Payment(wallet);
        tx["Amount"] = new Dictionary<string, object>
        {
            { "currency", "USD" },
            { "issuer", "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" },
            { "value", "100" },
        };
        tx["Memos"] = new List<object>
        {
            new Dictionary<string, object>
            {
                { "Memo", new Dictionary<string, object> { { "MemoData", "72656E74" } } },
                { "MemoBogusSibling", "1" },
            },
        };

        InvalidJsonException error = Assert.ThrowsExactly<InvalidJsonException>(() => wallet.Sign(tx));
        StringAssert.Contains(error.Message, "MemoBogusSibling");
    }

    /// <summary>
    /// A signed transaction with nothing unknown in it still signs.
    /// </summary>
    /// <remarks>
    /// Without this, a check that refused everything would pass every test above while making the
    /// SDK unable to sign at all.
    /// </remarks>
    [TestMethod]
    public void TestUATransactionWithKnownFieldsOnlyStillSigns()
    {
        XrplWallet wallet = XrplWallet.FromSeed(Seed);
        Dictionary<string, object> tx = Payment(wallet);
        tx["Memos"] = new List<object>
        {
            new Dictionary<string, object>
            {
                { "Memo", new Dictionary<string, object> { { "MemoData", "72656E74" } } },
            },
        };

        SignatureResult signed = wallet.Sign(tx);

        Assert.IsFalse(string.IsNullOrEmpty(signed.TxBlob), "a well-formed transaction must still sign");
        Assert.IsFalse(string.IsNullOrEmpty(signed.Hash));
    }

    /// <summary>
    /// The id an outer Batch signs over is computed strictly.
    /// </summary>
    /// <remarks>
    /// The worst of the three shapes in the report. <c>ComputeInnerTxId</c> parsed leniently, so a
    /// member the codec did not know was dropped from the bytes being hashed - and that hash is
    /// what the outer Batch signature commits to. The signature fixed an inner transaction other
    /// than the one the caller was shown, with nothing anywhere saying so.
    /// <para>
    /// Strict here does not mean signing-only: an id covers the whole transaction, so filtering to
    /// signing fields would hash something else again.
    /// </para>
    /// </remarks>
    [TestMethod]
    public void TestUInnerBatchTxIdRefusesAnUnknownField()
    {
        JsonObject inner = new JsonObject
        {
            ["TransactionType"] = "Payment",
            ["Account"] = "rQ3fNyLjbvcDaPNS4EAJY8aT9zR3uGk17c",
            ["Destination"] = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
            ["Amount"] = "1000",
            ["Fee"] = "0",
            ["Sequence"] = 1,
            ["SigningPubKey"] = "",
            ["FutureField"] = "1",
        };

        InvalidJsonException error = Assert.ThrowsExactly<InvalidJsonException>(
            () => inner.ComputeInnerTxId(),
            "the id the outer signature commits to was computed over a transaction with the member silently removed");

        StringAssert.Contains(error.Message, "FutureField");
    }

    /// <summary>
    /// An inner transaction with nothing unknown in it still gets an id.
    /// </summary>
    [TestMethod]
    public void TestUInnerBatchTxIdStillComputedForKnownFields()
    {
        JsonObject inner = new JsonObject
        {
            ["TransactionType"] = "Payment",
            ["Account"] = "rQ3fNyLjbvcDaPNS4EAJY8aT9zR3uGk17c",
            ["Destination"] = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
            ["Amount"] = "1000",
            ["Fee"] = "0",
            ["Sequence"] = 1,
            ["SigningPubKey"] = "",
        };

        string id = inner.ComputeInnerTxId();

        Assert.AreEqual(64, id.Length, "a transaction id is 32 bytes of hex");
    }

    /// <summary>
    /// A member the codec does not know inside a path step is refused.
    /// </summary>
    /// <remarks>
    /// Path steps are not <c>StObject</c>s - <c>PathHop</c> parses them itself, reading four named
    /// members and, until now, walking past anything else. So the recursion through objects and
    /// arrays did not reach them: a member here was still dropped from the bytes being signed.
    /// </remarks>
    [TestMethod]
    public void TestUUnknownMemberInAPathStepIsRefused()
    {
        XrplWallet wallet = XrplWallet.FromSeed(Seed);
        Dictionary<string, object> tx = Payment(wallet);
        tx["Amount"] = new Dictionary<string, object>
        {
            { "currency", "USD" },
            { "issuer", "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" },
            { "value", "100" },
        };
        tx["Paths"] = new List<object>
        {
            new List<object>
            {
                new Dictionary<string, object>
                {
                    { "account", "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" },
                    { "bogus_step_member", "1" },
                },
            },
        };

        InvalidJsonException error = Assert.ThrowsExactly<InvalidJsonException>(() => wallet.Sign(tx));
        StringAssert.Contains(error.Message, "bogus_step_member");
    }

    /// <summary>
    /// A path step carrying <c>type</c> still parses, because that is what the node sends.
    /// </summary>
    /// <remarks>
    /// The trap in refusing unknown members here. <c>ripple_path_find</c> answers with a
    /// <c>type</c> on every step, this SDK declares it on <c>PathStep</c> and emits it back out of
    /// <c>PathHop.ToJson</c>, so a path taken from a response and put into a payment carries it.
    /// The byte is synthesised from which of account, currency and issuer are present, so the
    /// member is redundant rather than unknown - refusing it would break the ordinary
    /// path-finding flow, and this test is what says so out loud.
    /// </remarks>
    [TestMethod]
    public void TestUPathStepFromTheNodeStillSigns()
    {
        XrplWallet wallet = XrplWallet.FromSeed(Seed);
        Dictionary<string, object> tx = Payment(wallet);
        tx["Amount"] = new Dictionary<string, object>
        {
            { "currency", "USD" },
            { "issuer", "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" },
            { "value", "100" },
        };
        tx["Paths"] = new List<object>
        {
            new List<object>
            {
                new Dictionary<string, object>
                {
                    { "account", "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" },
                    { "type", 1 },
                    { "type_hex", "0000000000000001" },
                },
            },
        };

        SignatureResult signed = wallet.Sign(tx);

        Assert.IsFalse(string.IsNullOrEmpty(signed.TxBlob),
            "a path taken from a ripple_path_find answer must still sign");
    }

    /// <summary>
    /// The MPT form of an Issue counts its members, like the two forms beside it always did.
    /// </summary>
    [TestMethod]
    public void TestUUnknownMemberInAnMptIssueIsRefused()
    {
        JsonObject issue = new JsonObject
        {
            ["mpt_issuance_id"] = "00000012D444B0B85E1FB7C22C0B7A8CE9C5AA5CE68B96A3",
            ["bogus"] = "1",
        };

        Assert.ThrowsExactly<InvalidJsonException>(
            () => Issue.FromJson(issue),
            "the MPT form was the one shape of Issue that walked past a member it did not know");
    }

    /// <summary>
    /// An XChainBridge carries exactly its four members.
    /// </summary>
    [TestMethod]
    public void TestUUnknownMemberInAnXChainBridgeIsRefused()
    {
        JsonObject bridge = new JsonObject
        {
            ["LockingChainDoor"] = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
            ["LockingChainIssue"] = new JsonObject { ["currency"] = "XRP" },
            ["IssuingChainDoor"] = "rQ3fNyLjbvcDaPNS4EAJY8aT9zR3uGk17c",
            ["IssuingChainIssue"] = new JsonObject { ["currency"] = "XRP" },
            ["bogus"] = "1",
        };

        Assert.ThrowsExactly<InvalidJsonException>(() => XChainBridgeType.FromJson(bridge));
    }

    /// <summary>
    /// Swapping one of the four members for an unknown one is still refused.
    /// </summary>
    /// <remarks>
    /// Counting alone would let this through: three of the four plus one member that does not
    /// belong also comes to four. The missing one would then reach <c>AccountId.FromJson</c> as
    /// null, and the caller would be told whatever that makes of nothing instead of which member
    /// is wrong.
    /// </remarks>
    [TestMethod]
    public void TestUXChainBridgeWithTheRightCountButTheWrongMembersIsRefused()
    {
        JsonObject bridge = new JsonObject
        {
            ["LockingChainDoor"] = "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh",
            ["LockingChainIssue"] = new JsonObject { ["currency"] = "XRP" },
            ["IssuingChainDoor"] = "rQ3fNyLjbvcDaPNS4EAJY8aT9zR3uGk17c",
            ["bogus"] = "1",
        };

        InvalidJsonException error = Assert.ThrowsExactly<InvalidJsonException>(
            () => XChainBridgeType.FromJson(bridge),
            "four members of the wrong names passed a check that only counted them");

        StringAssert.Contains(error.Message, "IssuingChainIssue",
            "the error should say which members a bridge is supposed to have");
    }

    /// <summary>
    /// <c>Encode</c> stays lenient, deliberately.
    /// </summary>
    /// <remarks>
    /// The asymmetry is the decision, not an oversight. <c>Encode</c> is used to read as much as to
    /// write: <c>IsSigned</c>, <c>IsAccountDelete</c> and <c>GetLastLedgerSequence</c> run a
    /// transaction through the codec to answer a question about it, and <c>HashSignedTx</c> hashes
    /// transactions that came from the node. Making it strict would turn "does this look signed?"
    /// into an exception, and would fail on any response carrying a field newer than this SDK's
    /// <c>definitions.json</c> - the forward compatibility the raw-JSON work exists to keep.
    /// </remarks>
    [TestMethod]
    public void TestUEncodeStillDropsUnknownFieldsWithoutComplaint()
    {
        XrplWallet wallet = XrplWallet.FromSeed(Seed);
        Dictionary<string, object> tx = Payment(wallet);
        tx["SigningPubKey"] = "";
        tx["BogusTop"] = "1";

        string blob = XrplBinaryCodec.Encode(tx);

        Assert.IsFalse(string.IsNullOrEmpty(blob),
            "Encode answers a question about a transaction; it must not start throwing on unknown members");
    }
}
