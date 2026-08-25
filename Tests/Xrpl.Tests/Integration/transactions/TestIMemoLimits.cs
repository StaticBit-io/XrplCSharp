using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Models.Methods;
using Xrpl.Models.Transactions;
using Xrpl.Sugar;
using Xrpl.Wallet;

namespace XrplTests.Xrpl.ClientLib.Integration;

/// <summary>
/// The memo limit the SDK refuses before signing is the one a node actually applies - issue #119.
/// </summary>
/// <remarks>
/// <para>
/// The constant comes from reading rippled's <c>isMemoOkay</c>, and a constant read off a source
/// file can be wrong in two directions that a unit test cannot tell apart. Too strict and the SDK
/// refuses transactions a node would have taken; too loose and the refusal it exists to prevent
/// happens anyway. Both directions are checked here against a real node.
/// </para>
/// <para>
/// The second test signs on the node rather than in the SDK, using <c>submit</c> with a secret.
/// That is deliberate: the SDK's own check would stop the transaction first, and what has to be
/// observed is the node refusing it, in its own words.
/// </para>
/// </remarks>
[TestClass]
public class TestIMemoLimits
{
    private static IXrplClient client;
    private static XrplWallet wallet;

    /// <summary>
    /// The largest <c>MemoData</c> that fits, per <see cref="MemoRules.MaxSerializedLength"/>.
    /// </summary>
    private const int LargestMemoDataInOneMemo = 1019;

    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(TestNodeType.Standalone);
        wallet = await Utils.GenerateFundedWallet(client);
    }

    [ClassCleanup]
    public static void ClassCleanup() => client?.Dispose();

    private static Dictionary<string, object> PaymentWithMemo(int memoDataBytes) => new Dictionary<string, object>
    {
        { "TransactionType", "Payment" },
        { "Account", wallet.ClassicAddress },
        { "Destination", "rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh" },
        { "Amount", "1000" },
        {
            "Memos", new List<object>
            {
                new Dictionary<string, object>
                {
                    {
                        "Memo", new Dictionary<string, object>
                        {
                            { "MemoData", new string('A', memoDataBytes * 2) },
                        }
                    },
                },
            }
        },
    };

    /// <summary>
    /// A memo filling the limit exactly is accepted and reaches a ledger, so the SDK is not
    /// refusing what a node would take.
    /// </summary>
    [TestMethod]
    public async Task TestIMemoAtTheLimitIsAccepted()
    {
        Dictionary<string, object> tx = PaymentWithMemo(LargestMemoDataInOneMemo);

        Submit response = await client.Submit(tx, wallet: wallet);

        Assert.AreEqual(
            "tesSUCCESS",
            response.EngineResult,
            $"A memo of exactly {MemoRules.MaxSerializedLength} serialized bytes must be accepted: " +
            $"refusing it locally would cost consumers transactions the node would have taken. " +
            $"Node said: {response.EngineResultMessage}");
    }

    /// <summary>
    /// One byte over, and the node refuses it - which is what the local check exists to save the
    /// caller from discovering after signing.
    /// </summary>
    [TestMethod]
    public async Task TestIMemoOverTheLimitIsRefusedByTheNode()
    {
        Dictionary<string, object> tx = PaymentWithMemo(LargestMemoDataInOneMemo + 1);
        tx["Fee"] = "12";
        AccountInfo account = (await client.AccountInfo(new AccountInfoRequest(wallet.ClassicAddress))).Result;
        tx["Sequence"] = account.AccountData.Sequence;

        // Signed by the node, not by us: our own rules would refuse this before a signature
        // existed, and the point here is to hear the node's answer.
        Dictionary<string, object> request = new Dictionary<string, object>
        {
            { "command", "submit" },
            { "tx_json", tx },
            { "secret", wallet.Seed },
        };

        string answer;
        try
        {
            Dictionary<string, object> response = await client.Request(request).Typed();
            answer = string.Join(", ", response.Keys) + " => " + string.Join(", ", response.Values);
        }
        catch (Exception error)
        {
            answer = error.Message;
        }

        // The node's own words, not merely the string "memo": the answer echoes the request, whose
        // tx_json carries a Memos field, so a looser assertion would pass without the node having
        // objected to anything. Observed here: "invalidParams - The memo exceeds the maximum
        // allowed size."
        StringAssert.Contains(
            answer,
            "exceeds the maximum allowed size",
            $"The node must refuse a memo one byte past the limit, and say why. It answered: {answer}");
    }
}
