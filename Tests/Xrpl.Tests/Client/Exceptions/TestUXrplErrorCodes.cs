using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Client.Json;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Subscriptions;

namespace Xrpl.Tests.ClientLib;

/// <summary>
/// <see cref="XrplErrorCodes"/> against rippled's own error table, and every code in it against
/// <see cref="XrplErrorClassifier"/>: a code the classifier does not know falls to
/// <see cref="XrplErrorCategory.Unknown"/>, which reports it as not retryable.
/// </summary>
[TestClass]
public class TestUXrplErrorCodes
{
    /// <summary>
    /// The codes of the <c>{RpcXxx, "code", "text", status}</c> table in
    /// <c>src/libxrpl/protocol/ErrorCodes.cpp</c> at rippled 3.4.0.
    /// </summary>
    private static readonly string[] RippledCodes =
    {
        "actMalformed", "actNotFound", "alreadyMultisig", "alreadySingleSig", "amendmentBlocked",
        "badCredentials", "badFeature", "badIssuer", "badKeyType", "badMarket", "badSecret", "badSeed",
        "badSyntax", "channelAmtMalformed", "channelMalformed", "commandMissing", "dbDeserialization",
        "delegateActNotFound", "deprecated", "domainMalformed", "dstActMalformed", "dstActMissing",
        "dstActNotFound", "dstAmtMalformed", "dstAmtMissing", "dstIsrMalformed", "entryNotFound",
        "excessiveLgrRange", "forbidden", "highFee", "internal", "invalidHotWallet", "invalidLgrRange",
        "invalidParams", "issueMalformed", "json_rpc", "lgrIdxMalformed", "lgrIdxsInvalid", "lgrNotFound",
        "lgrNotValidated", "malformedStream", "masterDisabled", "noClosed", "noCurrent", "noEvents",
        "noNetwork", "noPathRequest", "noPermission", "notEnabled", "notImpl", "notReady", "notSupported",
        "notSynced", "objectNotFound", "oracleMalformed", "publicMalformed", "sendMaxMalformed",
        "signingMalformed", "slowDown", "srcActMalformed", "srcActMissing", "srcActNotFound",
        "srcCurMalformed", "srcIsrMalformed", "tooBusy", "transactionSigned", "txnNotFound",
        "unexpectedLedgerType", "unknownCmd", "unlBlocked", "wrongNetwork",
    };

    private static IEnumerable<string> SdkCodes() => typeof(XrplErrorCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!);

    [TestMethod]
    public void EveryRippledCodeHasAConstant()
    {
        HashSet<string> known = SdkCodes().ToHashSet(StringComparer.Ordinal);
        string[] missing = RippledCodes.Where(code => !known.Contains(code)).ToArray();

        Assert.IsEmpty(missing, "codes rippled 3.4.0 answers with and XrplErrorCodes lacks: " + string.Join(", ", missing));
    }

    [TestMethod]
    public void NoConstantClassifiesAsUnknown()
    {
        string[] unknown = SdkCodes()
            .Where(code => Response(code).Classify().Category == XrplErrorCategory.Unknown)
            .ToArray();

        Assert.IsEmpty(unknown, "constants the classifier does not map: " + string.Join(", ", unknown));
    }

    [TestMethod]
    public void SlowDown_IsATemporaryProblemWorthRetrying()
    {
        XrplErrorInfo info = Response(XrplErrorCodes.SlowDown).Classify();

        Assert.AreEqual(XrplErrorCategory.TemporaryServerProblem, info.Category);
        Assert.IsTrue(info.IsRetryable);
    }

    [TestMethod]
    [DataRow(XrplErrorCodes.UnlBlocked)]
    [DataRow(XrplErrorCodes.WrongNetwork)]
    [DataRow(XrplErrorCodes.Internal)]
    public void ServerState_IsNotRetryable(string code)
    {
        XrplErrorInfo info = Response(code).Classify();

        Assert.AreEqual(XrplErrorCategory.ServerState, info.Category);
        Assert.IsFalse(info.IsRetryable);
    }

    [TestMethod]
    public void AnErrorWithoutStatus_IsARippledException()
    {
        // Some responses carry error but no status. They reach the caller as the same exception
        // the status-bearing ones do, so Classify() applies to both.
        RequestManager manager = new RequestManager();
        RequestManager.XrplGRequest pending = manager.CreateGRequest<LOLedgerData, LedgerDataRequest>(
            new LedgerDataRequest { Limit = 1 },
            Timeout.InfiniteTimeSpan);

        manager.HandleResponse($"{{\"id\":\"{pending.Id:D}\",\"type\":\"response\",\"error\":\"slowDown\",\"error_message\":\"You are placing too much load on the server.\"}}");

        RippledException exception = Assert.ThrowsExactly<RippledException>(() => pending.Promise.GetAwaiter().GetResult());
        Assert.AreEqual("slowDown - You are placing too much load on the server.", exception.Message);
        Assert.IsTrue(exception.Classify().IsRetryable);
    }

    /// <summary>An error response built through the wire form, as the request manager builds it.</summary>
    private static ErrorResponse Response(string code)
    {
        byte[] frame = JsonSerializer.SerializeToUtf8Bytes(
            new { error = code, error_message = code, request = new { command = "account_info" } },
            XrplJsonOptions.Default);
        ErrorResponse response = JsonSerializer.Deserialize<ErrorResponse>(frame, XrplJsonOptions.Default)!;
        response.AttachFrame(frame);
        return response;
    }
}
