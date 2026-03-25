using System;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Wallet;

using XrplTests.Xrpl.ClientLib.Integration;

namespace XrplTests.Client.Exceptions;

[TestClass]
[DoNotParallelize]
public class TestIXrplErrorClassifier
{
    public TestContext TestContext { get; set; } = null!;
    public static IXrplClient client = null!;

    private static readonly TestNodeType nodeType = TestNodeType.Standalone;

    [ClassInitialize]
    public static async Task MyClassInitializeAsync(TestContext testContext)
    {
        client = await IntegrationTestConfig.CreateClientAsync(nodeType);
    }

    [ClassCleanup]
    public static void AfterAllTests()
    {
        client.Dispose();
    }

    [TestMethod]
    public async Task Classify_ActNotFound_ReturnsExpectedInfo()
    {
        XrplWallet wallet = XrplWallet.Generate();
        AccountInfoRequest request = new AccountInfoRequest(wallet.ClassicAddress)
        {
            Strict = true,
            LedgerIndex = CreateValidatedLedgerIndex()
        };

        RippledException exception = await CatchRippledException(() => client.AccountInfo(request));
        XrplErrorInfo info = XrplErrorClassifier.Classify(exception);

        Assert.AreEqual(XrplErrorCodes.ActNotFound, info.RawError);
        Assert.AreEqual(XrplErrorCategory.NotFound, info.Category);
        Assert.AreEqual(XrplErrorSubject.Account, info.Subject);
        Assert.AreEqual("Account not found", info.Title);
        Assert.AreEqual("account_info", info.Command);
        Assert.AreEqual("account", info.FieldName);
        Assert.AreEqual(wallet.ClassicAddress, info.FieldValue);
        Assert.IsFalse(info.IsRetryable);
        Assert.IsTrue(info.IsUserFixable);
    }

    [TestMethod]
    public async Task Classify_ActMalformed_ReturnsExpectedInfo()
    {
        const string invalidAccount = "not_a_valid_address";

        AccountInfoRequest request = new AccountInfoRequest(invalidAccount)
        {
            Strict = true,
            LedgerIndex = CreateValidatedLedgerIndex()
        };

        RippledException exception = await CatchRippledException(() => client.AccountInfo(request));
        XrplErrorInfo info = XrplErrorClassifier.Classify(exception);

        Assert.AreEqual(XrplErrorCodes.ActMalformed, info.RawError);
        Assert.AreEqual(XrplErrorCategory.InvalidInput, info.Category);
        Assert.AreEqual(XrplErrorSubject.Account, info.Subject);
        Assert.AreEqual("Incorrect account address", info.Title);
        Assert.AreEqual("account_info", info.Command);
        Assert.AreEqual("account", info.FieldName);
        Assert.AreEqual(invalidAccount, info.FieldValue);
        Assert.IsFalse(info.IsRetryable);
        Assert.IsTrue(info.IsUserFixable);
    }

    [TestMethod]
    public async Task Classify_TxnNotFound_ReturnsExpectedInfo()
    {
        const string transactionHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        TxRequest request = new TxRequest(transactionHash);

        RippledException exception = await CatchRippledException(() => client.Tx(request));
        XrplErrorInfo info = XrplErrorClassifier.Classify(exception);

        Assert.AreEqual(XrplErrorCodes.TxnNotFound, info.RawError);
        Assert.AreEqual(XrplErrorCategory.NotFound, info.Category);
        Assert.AreEqual(XrplErrorSubject.Transaction, info.Subject);
        Assert.AreEqual("Transaction not found", info.Title);
        Assert.AreEqual("tx", info.Command);
        Assert.IsFalse(info.IsRetryable);
        Assert.IsTrue(info.IsUserFixable);
    }

    [TestMethod]
    public async Task Classify_LgrNotFound_ReturnsExpectedInfo()
    {
        LedgerRequest request = new LedgerRequest
        {
            LedgerIndex = new LedgerIndex(999_999_999u)
        };

        RippledException exception = await CatchRippledException(() => client.Ledger(request));
        XrplErrorInfo info = XrplErrorClassifier.Classify(exception);

        Assert.AreEqual(XrplErrorCodes.LedgerNotFound, info.RawError);
        Assert.AreEqual(XrplErrorCategory.LedgerUnavailable, info.Category);
        Assert.AreEqual(XrplErrorSubject.Ledger, info.Subject);
        Assert.AreEqual("Ledger not found", info.Title);
        Assert.AreEqual("ledger", info.Command);
        Assert.IsTrue(info.IsRetryable);
        Assert.IsTrue(info.IsUserFixable);
    }

    [TestMethod]
    public async Task Classify_InvalidParams_ReturnsExpectedInfo()
    {
        BaseRequest request = new BaseRequest
        {
            Command = "account_info"
        };

        RippledException exception = await CatchRippledException(() => client.AnyRequest(request));
        XrplErrorInfo info = XrplErrorClassifier.Classify(exception);

        Assert.AreEqual(XrplErrorCodes.InvalidParams, info.RawError);
        Assert.AreEqual(XrplErrorCategory.BadRequest, info.Category);
        Assert.AreEqual(XrplErrorSubject.Request, info.Subject);
        Assert.AreEqual("Incorrect request", info.Title);
        Assert.AreEqual("account_info", info.Command);
        Assert.IsFalse(info.IsRetryable);
        Assert.IsTrue(info.IsUserFixable);
    }

    [TestMethod]
    public async Task Classify_UnknownCmd_ReturnsExpectedInfo()
    {
        BaseRequest request = new BaseRequest
        {
            Command = "not_a_real_command"
        };

        RippledException exception = await CatchRippledException(() => client.AnyRequest(request));
        XrplErrorInfo info = XrplErrorClassifier.Classify(exception);

        Assert.AreEqual(XrplErrorCodes.UnknownCommand, info.RawError);
        Assert.AreEqual(XrplErrorCategory.UnsupportedRequest, info.Category);
        Assert.AreEqual(XrplErrorSubject.Request, info.Subject);
        Assert.AreEqual("Command not supported", info.Title);
        Assert.AreEqual("not_a_real_command", info.Command);
        Assert.IsFalse(info.IsRetryable);
        Assert.IsTrue(info.IsUserFixable);
    }

    [TestMethod]
    public async Task Classify_EntryNotFound_ForTrustLine_ReturnsExpectedInfo()
    {
        XrplWallet wallet1 = XrplWallet.Generate();
        XrplWallet wallet2 = XrplWallet.Generate();

        LedgerEntryRequest request = new LedgerEntryRequest
        {
            LedgerIndex = CreateValidatedLedgerIndex(),
            RippleState = new RippleStateQuery
            {
                Addresses = new[] { wallet1.ClassicAddress, wallet2.ClassicAddress },
                Currency = "USD"
            }
        };

        RippledException exception = await CatchRippledException(() => client.LedgerEntry(request));
        XrplErrorInfo info = XrplErrorClassifier.Classify(exception);

        Assert.AreEqual(XrplErrorCodes.EntryNotFound, info.RawError);
        Assert.AreEqual(XrplErrorCategory.NotFound, info.Category);
        Assert.AreEqual(XrplErrorSubject.TrustLine, info.Subject);
        Assert.AreEqual("Trustline not found", info.Title);
        Assert.AreEqual("ledger_entry", info.Command);
        Assert.AreEqual("currency", info.FieldName);
        Assert.AreEqual("USD", info.FieldValue);
        Assert.IsFalse(info.IsRetryable);
        Assert.IsTrue(info.IsUserFixable);
    }

    [TestMethod]
    public async Task Classify_EntryNotFound_ForUnknownObject_ReturnsExpectedInfo()
    {
        LedgerEntryRequest request = new LedgerEntryRequest
        {
            LedgerIndex = CreateValidatedLedgerIndex(),
            Index = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
        };

        RippledException exception = await CatchRippledException(() => client.LedgerEntry(request));
        XrplErrorInfo info = XrplErrorClassifier.Classify(exception);

        Assert.AreEqual(XrplErrorCodes.EntryNotFound, info.RawError);
        Assert.AreEqual(XrplErrorCategory.NotFound, info.Category);
        Assert.AreEqual(XrplErrorSubject.Unknown, info.Subject);
        Assert.AreEqual("Object not found", info.Title);
        Assert.AreEqual("ledger_entry", info.Command);
        Assert.IsFalse(info.IsRetryable);
        Assert.IsTrue(info.IsUserFixable);
    }

    private static LedgerIndex CreateValidatedLedgerIndex()
    {
        return new LedgerIndex(LedgerIndexType.Validated);
    }

    private static async Task<RippledException> CatchRippledException(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (RippledException exception)
        {
            return exception;
        }

        throw new AssertFailedException("Expected RippledException was not thrown.");
    }

    private static async Task<RippledException> CatchRippledException<T>(Func<Task<T>> action)
    {
        try
        {
            await action();
        }
        catch (RippledException exception)
        {
            return exception;
        }

        throw new AssertFailedException("Expected RippledException was not thrown.");
    }
}
