using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json.Nodes;

using Xrpl.Client;
using Xrpl.Client.Exceptions;
using Xrpl.Models.Common;
using Xrpl.Models.Ledger;
using Xrpl.Models.Methods;
using Xrpl.Models.Subscriptions;
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
        Assert.AreEqual(XrplErrorSubject.Request, info.Subject);
        Assert.AreEqual("Incorrect request value", info.Title);
        Assert.AreEqual("account_info", info.Command);
        Assert.IsNull(info.FieldName);
        Assert.IsNull(info.FieldValue);
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

[TestClass]
public class TestUXrplErrorClassifier
{
    [DataTestMethod]
    [DataRow(XrplErrorCodes.WsTextRequired, XrplErrorCategory.BadRequest, XrplErrorSubject.Request, "Incorrect request")]
    [DataRow(XrplErrorCodes.UnknownOption, XrplErrorCategory.BadRequest, XrplErrorSubject.Request, "Incorrect request")]
    [DataRow(XrplErrorCodes.UnexpectedLedgerType, XrplErrorCategory.InvalidInput, XrplErrorSubject.Ledger, "Unexpected ledger entry type")]
    [DataRow(XrplErrorCodes.NoPermission, XrplErrorCategory.UnsupportedRequest, XrplErrorSubject.Request, "Permission required")]
    [DataRow(XrplErrorCodes.NoEvents, XrplErrorCategory.UnsupportedRequest, XrplErrorSubject.Request, "Streaming not supported")]
    [DataRow(XrplErrorCodes.BadMarket, XrplErrorCategory.NotFound, XrplErrorSubject.Request, "Market not found")]
    public void Classify_DocumentedErrorCode_ReturnsExpectedBaseMapping(
        string errorCode,
        XrplErrorCategory expectedCategory,
        XrplErrorSubject expectedSubject,
        string expectedTitle)
    {
        ErrorResponse response = CreateErrorResponse(
            errorCode,
            new
            {
                command = "book_offers"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(errorCode, info.RawError);
        Assert.AreEqual(expectedCategory, info.Category);
        Assert.AreEqual(expectedSubject, info.Subject);
        Assert.AreEqual(expectedTitle, info.Title);
        Assert.AreEqual("book_offers", info.Command);
        Assert.IsFalse(info.IsRetryable);
        Assert.IsTrue(info.IsUserFixable);
    }

    [DataTestMethod]
    [DataRow(XrplErrorCodes.Deprecated)]
    [DataRow(XrplErrorCodes.InvalidApiVersion)]
    [DataRow(XrplErrorCodes.NotEnabled)]
    [DataRow(XrplErrorCodes.NotImplemented)]
    [DataRow(XrplErrorCodes.NotSupported)]
    public void Classify_UnsupportedRequestFamily_ReturnsExpectedMapping(string errorCode)
    {
        ErrorResponse response = CreateErrorResponse(
            errorCode,
            new
            {
                command = "server_info"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(errorCode, info.RawError);
        Assert.AreEqual(XrplErrorCategory.UnsupportedRequest, info.Category);
        Assert.AreEqual(XrplErrorSubject.Request, info.Subject);
        Assert.AreEqual("Command not supported", info.Title);
        Assert.AreEqual("server_info", info.Command);
        Assert.IsFalse(info.IsRetryable);
        Assert.IsTrue(info.IsUserFixable);
    }

    [DataTestMethod]
    [DataRow(XrplErrorCodes.TooBusy)]
    [DataRow(XrplErrorCodes.NotReady)]
    [DataRow(XrplErrorCodes.NotSynced)]
    public void Classify_TemporaryServerProblemFamily_ReturnsExpectedMapping(string errorCode)
    {
        ErrorResponse response = CreateErrorResponse(
            errorCode,
            new
            {
                command = "ledger"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(errorCode, info.RawError);
        Assert.AreEqual(XrplErrorCategory.TemporaryServerProblem, info.Category);
        Assert.AreEqual(XrplErrorSubject.Server, info.Subject);
        Assert.AreEqual("Temporary server problem", info.Title);
        Assert.AreEqual("ledger", info.Command);
        Assert.IsTrue(info.IsRetryable);
        Assert.IsFalse(info.IsUserFixable);
    }

    [DataTestMethod]
    [DataRow(XrplErrorCodes.ExcessiveLedgerRange)]
    [DataRow(XrplErrorCodes.InvalidLedgerRange)]
    public void Classify_LedgerRangeError_ReturnsExpectedMapping(string errorCode)
    {
        ErrorResponse response = CreateErrorResponse(
            errorCode,
            new
            {
                command = "tx",
                min_ledger = 10,
                max_ledger = 5000
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(errorCode, info.RawError);
        Assert.AreEqual(XrplErrorCategory.BadRequest, info.Category);
        Assert.AreEqual(XrplErrorSubject.Request, info.Subject);
        Assert.AreEqual("Incorrect ledger range", info.Title);
        Assert.AreEqual("ledger_range", info.FieldName);
        Assert.AreEqual("10..5000", info.FieldValue);
        Assert.AreEqual("tx", info.Command);
        Assert.IsFalse(info.IsRetryable);
        Assert.IsTrue(info.IsUserFixable);
    }

    [TestMethod]
    public void Classify_LedgerIndicesInvalid_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.LedgerIndicesInvalid,
            new
            {
                command = "account_tx",
                ledger_index_min = 900,
                ledger_index_max = 100
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.LedgerIndicesInvalid, info.RawError);
        Assert.AreEqual(XrplErrorCategory.BadRequest, info.Category);
        Assert.AreEqual(XrplErrorSubject.Request, info.Subject);
        Assert.AreEqual("Incorrect ledger indexes", info.Title);
        Assert.AreEqual("ledger_range", info.FieldName);
        Assert.AreEqual("900..100", info.FieldValue);
        Assert.AreEqual("account_tx", info.Command);
    }

    [TestMethod]
    public void Classify_LedgerRangeError_WithoutBounds_ReturnsNullFieldValue()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.InvalidLedgerRange,
            new
            {
                command = "tx"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.InvalidLedgerRange, info.RawError);
        Assert.AreEqual("ledger_range", info.FieldName);
        Assert.IsNull(info.FieldValue);
    }

    [DataTestMethod]
    [DataRow(XrplErrorCodes.MalformedAddress)]
    [DataRow(XrplErrorCodes.MalformedOwner)]
    public void Classify_MalformedAddressFamily_ReturnsExpectedMapping(string errorCode)
    {
        ErrorResponse response = CreateErrorResponse(
            errorCode,
            new
            {
                command = "ledger_entry"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(errorCode, info.RawError);
        Assert.AreEqual(XrplErrorCategory.InvalidInput, info.Category);
        Assert.AreEqual(XrplErrorSubject.Address, info.Subject);
        Assert.AreEqual("Incorrect address", info.Title);
        Assert.AreEqual("ledger_entry", info.Command);
    }

    [TestMethod]
    public void Classify_MalformedCurrency_WithTopLevelCurrency_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.MalformedCurrency,
            new
            {
                command = "book_offers",
                currency = "BAD"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.MalformedCurrency, info.RawError);
        Assert.AreEqual(XrplErrorCategory.InvalidInput, info.Category);
        Assert.AreEqual(XrplErrorSubject.Currency, info.Subject);
        Assert.AreEqual("Incorrect currency code", info.Title);
        Assert.AreEqual("currency", info.FieldName);
        Assert.AreEqual("BAD", info.FieldValue);
        Assert.AreEqual("The currency code 'BAD' is not in the correct format.", info.UserMessage);
    }

    [TestMethod]
    public void Classify_MalformedCurrency_WithRippleStateCurrency_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.MalformedCurrency,
            new
            {
                command = "ledger_entry",
                ripple_state = new
                {
                    currency = "USD"
                }
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual("USD", info.FieldValue);
        Assert.AreEqual("The currency code 'USD' is not in the correct format.", info.UserMessage);
    }

    [TestMethod]
    public void Classify_MalformedCurrency_WithoutCurrency_ReturnsFallbackMessage()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.MalformedCurrency,
            new
            {
                command = "book_offers"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.IsNull(info.FieldValue);
        Assert.AreEqual("The currency code is not in the correct format.", info.UserMessage);
    }

    [TestMethod]
    public void Classify_MalformedDocumentId_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.MalformedDocumentId,
            new
            {
                command = "ledger_entry",
                oracle = new
                {
                    account = "rOracle",
                    oracle_document_id = "bad-id"
                }
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.MalformedDocumentId, info.RawError);
        Assert.AreEqual(XrplErrorCategory.InvalidInput, info.Category);
        Assert.AreEqual(XrplErrorSubject.Request, info.Subject);
        Assert.AreEqual("Incorrect document id", info.Title);
        Assert.AreEqual("oracle_document_id", info.FieldName);
        Assert.AreEqual("bad-id", info.FieldValue);
        Assert.AreEqual("ledger_entry", info.Command);
    }

    [TestMethod]
    public void Classify_InvalidHotWallet_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.InvalidHotWallet,
            new
            {
                command = "gateway_balances",
                account = "rIssuer",
                hotwallet = new[] { "rHot1", "rHot2" }
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.InvalidHotWallet, info.RawError);
        Assert.AreEqual(XrplErrorCategory.InvalidInput, info.Category);
        Assert.AreEqual(XrplErrorSubject.Address, info.Subject);
        Assert.AreEqual("Incorrect hot wallet", info.Title);
        Assert.AreEqual("hotwallet", info.FieldName);
        Assert.AreEqual("[\"rHot1\",\"rHot2\"]", info.FieldValue);
        Assert.AreEqual("gateway_balances", info.Command);
    }

    [TestMethod]
    public void Classify_ObjectNotFound_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.ObjectNotFound,
            new
            {
                command = "ledger_entry"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.ObjectNotFound, info.RawError);
        Assert.AreEqual(XrplErrorCategory.NotFound, info.Category);
        Assert.AreEqual(XrplErrorSubject.Unknown, info.Subject);
        Assert.AreEqual("Object not found", info.Title);
        Assert.AreEqual("ledger_entry", info.Command);
    }

    [TestMethod]
    public void Classify_LedgerNotValidated_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.LedgerNotValidated,
            new
            {
                command = "ledger"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.LedgerNotValidated, info.RawError);
        Assert.AreEqual(XrplErrorCategory.LedgerUnavailable, info.Category);
        Assert.AreEqual(XrplErrorSubject.Ledger, info.Subject);
        Assert.AreEqual("Ledger not validated", info.Title);
        Assert.IsTrue(info.IsRetryable);
        Assert.IsTrue(info.IsUserFixable);
    }

    [TestMethod]
    public void Classify_SourceAccountMalformed_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.SourceAccountMalformed,
            new
            {
                command = "deposit_authorized",
                source_account = "badSource"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.SourceAccountMalformed, info.RawError);
        Assert.AreEqual(XrplErrorCategory.InvalidInput, info.Category);
        Assert.AreEqual(XrplErrorSubject.Account, info.Subject);
        Assert.AreEqual("Incorrect source account address", info.Title);
        Assert.AreEqual("source_account", info.FieldName);
        Assert.AreEqual("badSource", info.FieldValue);
    }

    [TestMethod]
    public void Classify_DestinationAccountMalformed_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.DestinationAccountMalformed,
            new
            {
                command = "deposit_authorized",
                destination_account = "badDestination"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.DestinationAccountMalformed, info.RawError);
        Assert.AreEqual("destination_account", info.FieldName);
        Assert.AreEqual("badDestination", info.FieldValue);
        Assert.AreEqual("Incorrect destination account address", info.Title);
    }

    [TestMethod]
    public void Classify_SourceAccountMissing_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.SourceAccountMissing,
            new
            {
                command = "deposit_authorized"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.SourceAccountMissing, info.RawError);
        Assert.AreEqual(XrplErrorCategory.BadRequest, info.Category);
        Assert.AreEqual(XrplErrorSubject.Account, info.Subject);
        Assert.AreEqual("Source account is missing", info.Title);
        Assert.AreEqual("source_account", info.FieldName);
        Assert.IsNull(info.FieldValue);
    }

    [TestMethod]
    public void Classify_DestinationAccountMissing_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.DestinationAccountMissing,
            new
            {
                command = "deposit_authorized"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.DestinationAccountMissing, info.RawError);
        Assert.AreEqual(XrplErrorCategory.BadRequest, info.Category);
        Assert.AreEqual(XrplErrorSubject.Account, info.Subject);
        Assert.AreEqual("Destination account is missing", info.Title);
        Assert.AreEqual("destination_account", info.FieldName);
        Assert.IsNull(info.FieldValue);
    }

    [TestMethod]
    public void Classify_SourceAccountNotFound_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.SourceAccountNotFound,
            new
            {
                command = "deposit_authorized",
                source_account = "rSource"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.SourceAccountNotFound, info.RawError);
        Assert.AreEqual(XrplErrorCategory.NotFound, info.Category);
        Assert.AreEqual(XrplErrorSubject.Account, info.Subject);
        Assert.AreEqual("Source account not found", info.Title);
        Assert.AreEqual("source_account", info.FieldName);
        Assert.AreEqual("rSource", info.FieldValue);
    }

    [TestMethod]
    public void Classify_DestinationAccountNotFound_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.DestinationAccountNotFound,
            new
            {
                command = "deposit_authorized",
                destination = "rDestination"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.DestinationAccountNotFound, info.RawError);
        Assert.AreEqual("destination", info.FieldName);
        Assert.AreEqual("rDestination", info.FieldValue);
        Assert.AreEqual("Destination account not found", info.Title);
    }

    [TestMethod]
    public void Classify_DestinationAmountMissing_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.DestinationAmountMissing,
            new
            {
                command = "ripple_path_find"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.DestinationAmountMissing, info.RawError);
        Assert.AreEqual(XrplErrorCategory.BadRequest, info.Category);
        Assert.AreEqual(XrplErrorSubject.Request, info.Subject);
        Assert.AreEqual("Destination amount is missing", info.Title);
        Assert.AreEqual("destination_amount", info.FieldName);
    }

    [TestMethod]
    public void Classify_LedgerIndexMalformed_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.LedgerIndexMalformed,
            new
            {
                command = "ledger",
                ledger_hash = "badLedgerHash"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.LedgerIndexMalformed, info.RawError);
        Assert.AreEqual(XrplErrorCategory.BadRequest, info.Category);
        Assert.AreEqual(XrplErrorSubject.Ledger, info.Subject);
        Assert.AreEqual("Incorrect ledger selector", info.Title);
        Assert.AreEqual("ledger_hash", info.FieldName);
        Assert.AreEqual("badLedgerHash", info.FieldValue);
    }

    [TestMethod]
    public void Classify_PublicMalformed_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.PublicMalformed,
            new
            {
                command = "channel_verify",
                public_key = "badKey"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.PublicMalformed, info.RawError);
        Assert.AreEqual(XrplErrorCategory.InvalidInput, info.Category);
        Assert.AreEqual(XrplErrorSubject.Request, info.Subject);
        Assert.AreEqual("Incorrect public key", info.Title);
        Assert.AreEqual("public_key", info.FieldName);
        Assert.AreEqual("badKey", info.FieldValue);
    }

    [TestMethod]
    public void Classify_SendMaxMalformed_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.SendMaxMalformed,
            new
            {
                command = "ripple_path_find",
                send_max = new
                {
                    currency = "USD",
                    value = "bad"
                }
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.SendMaxMalformed, info.RawError);
        Assert.AreEqual("send_max", info.FieldName);
        Assert.AreEqual("{\"currency\":\"USD\",\"value\":\"bad\"}", info.FieldValue);
        Assert.AreEqual("Incorrect SendMax amount", info.Title);
    }

    [TestMethod]
    public void Classify_IssueMalformed_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.IssueMalformed,
            new
            {
                command = "book_offers",
                issue = new
                {
                    currency = "USD",
                    issuer = "badIssuer"
                }
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.IssueMalformed, info.RawError);
        Assert.AreEqual("issue", info.FieldName);
        Assert.AreEqual("{\"currency\":\"USD\",\"issuer\":\"badIssuer\"}", info.FieldValue);
        Assert.AreEqual("Incorrect issue", info.Title);
    }

    [TestMethod]
    public void Classify_SourceCurrencyMalformed_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.SourceCurrencyMalformed,
            new
            {
                command = "book_offers",
                taker_pays = new
                {
                    currency = "BAD"
                }
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.SourceCurrencyMalformed, info.RawError);
        Assert.AreEqual(XrplErrorCategory.InvalidInput, info.Category);
        Assert.AreEqual(XrplErrorSubject.Request, info.Subject);
        Assert.AreEqual("Incorrect order book asset", info.Title);
        Assert.AreEqual("taker_pays", info.FieldName);
        Assert.AreEqual("{\"currency\":\"BAD\"}", info.FieldValue);
        Assert.AreEqual("book_offers", info.Command);
    }

    [TestMethod]
    public void Classify_DestinationAmountMalformed_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.DestinationAmountMalformed,
            new
            {
                command = "book_offers",
                taker_gets = new
                {
                    currency = "BAD"
                }
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.DestinationAmountMalformed, info.RawError);
        Assert.AreEqual("taker_gets", info.FieldName);
        Assert.AreEqual("{\"currency\":\"BAD\"}", info.FieldValue);
        Assert.AreEqual("book_offers", info.Command);
    }

    [TestMethod]
    public void Classify_SourceIssuerMalformed_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.SourceIssuerMalformed,
            new
            {
                command = "book_offers",
                taker_pays = new
                {
                    currency = "USD",
                    issuer = "badIssuer"
                }
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.SourceIssuerMalformed, info.RawError);
        Assert.AreEqual(XrplErrorCategory.InvalidInput, info.Category);
        Assert.AreEqual(XrplErrorSubject.Address, info.Subject);
        Assert.AreEqual("Incorrect issuer address", info.Title);
        Assert.AreEqual("taker_pays.issuer", info.FieldName);
        Assert.AreEqual("badIssuer", info.FieldValue);
        Assert.AreEqual("book_offers", info.Command);
    }

    [TestMethod]
    public void Classify_DestinationIssuerMalformed_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.DestinationIssuerMalformed,
            new
            {
                command = "book_offers",
                taker_gets = new
                {
                    currency = "USD",
                    issuer = "badIssuer"
                }
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.DestinationIssuerMalformed, info.RawError);
        Assert.AreEqual("taker_gets.issuer", info.FieldName);
        Assert.AreEqual("badIssuer", info.FieldValue);
        Assert.AreEqual("book_offers", info.Command);
    }

    [TestMethod]
    public void Classify_EntryNotFound_WithVaultId_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.EntryNotFound,
            new
            {
                command = "ledger_entry",
                vault_id = "vault-1"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.EntryNotFound, info.RawError);
        Assert.AreEqual(XrplErrorCategory.NotFound, info.Category);
        Assert.AreEqual(XrplErrorSubject.Vault, info.Subject);
        Assert.AreEqual("Vault not found", info.Title);
        Assert.AreEqual("vault_id", info.FieldName);
        Assert.AreEqual("vault-1", info.FieldValue);
    }

    [TestMethod]
    public void Classify_EntryNotFound_WithoutTrustLineCurrency_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.EntryNotFound,
            new
            {
                command = "ledger_entry",
                ripple_state = new
                {
                    currency = (string?)null
                }
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCategory.NotFound, info.Category);
        Assert.AreEqual(XrplErrorSubject.TrustLine, info.Subject);
        Assert.AreEqual("Trustline not found", info.Title);
        Assert.AreEqual("Trustline or matching ledger object not found.", info.UserMessage);
        Assert.IsNull(info.FieldValue);
    }

    [TestMethod]
    public void Classify_ActMalformed_WithMarkerRequest_ReturnsNeutralMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.ActMalformed,
            new
            {
                command = "account_lines",
                marker = "bad-marker"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.ActMalformed, info.RawError);
        Assert.AreEqual(XrplErrorCategory.InvalidInput, info.Category);
        Assert.AreEqual(XrplErrorSubject.Request, info.Subject);
        Assert.AreEqual("Incorrect request value", info.Title);
        Assert.IsNull(info.FieldName);
        Assert.IsNull(info.FieldValue);
        Assert.AreEqual("account_lines", info.Command);
    }

    [TestMethod]
    public void Classify_AmendmentBlocked_ReturnsExpectedMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.AmendmentBlocked,
            new
            {
                command = "server_info"
            });

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual(XrplErrorCodes.AmendmentBlocked, info.RawError);
        Assert.AreEqual(XrplErrorCategory.ServerState, info.Category);
        Assert.AreEqual(XrplErrorSubject.Server, info.Subject);
        Assert.AreEqual("Server requires upgrade", info.Title);
        Assert.IsFalse(info.IsRetryable);
        Assert.IsFalse(info.IsUserFixable);
    }

    [TestMethod]
    public void Classify_UnknownError_ReturnsFallbackMapping()
    {
        ErrorResponse response = CreateErrorResponse(
            "totallyUnknownRippledError",
            new
            {
                command = "ledger"
            },
            "unexpected");

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual("totallyUnknownRippledError", info.RawError);
        Assert.AreEqual(XrplErrorCategory.Unknown, info.Category);
        Assert.AreEqual(XrplErrorSubject.Unknown, info.Subject);
        Assert.AreEqual("Unknown XRPL error", info.Title);
        Assert.AreEqual("unexpected", info.UserMessage);
    }

    [TestMethod]
    public void Classify_NonRippledException_ReturnsUnknownMapping()
    {
        InvalidOperationException exception = new InvalidOperationException("boom");

        XrplErrorInfo info = XrplErrorClassifier.Classify(exception);

        Assert.AreEqual(nameof(InvalidOperationException), info.RawError);
        Assert.AreEqual(XrplErrorCategory.Unknown, info.Category);
        Assert.AreEqual(XrplErrorSubject.Unknown, info.Subject);
        Assert.AreEqual("internal error", info.Title);
        Assert.AreEqual("boom", info.UserMessage);
    }

    [TestMethod]
    public void Classify_NullException_ThrowsArgumentNullException()
    {
        AssertArgumentNullException(() => XrplErrorClassifier.Classify((Exception)null!));
    }

    [TestMethod]
    public void Classify_NullRippledException_ThrowsArgumentNullException()
    {
        AssertArgumentNullException(() => XrplErrorClassifier.Classify((RippledException)null!));
    }

    [TestMethod]
    public void Classify_NullErrorResponse_ThrowsArgumentNullException()
    {
        AssertArgumentNullException(() => XrplErrorClassifier.Classify((ErrorResponse)null!));
    }

    [TestMethod]
    public void Classify_Warnings_FiltersInformationalEntries()
    {
        List<RippleResponseWarning> warnings = new List<RippleResponseWarning>
        {
            CreateWarning(2001, "From Clio"),
            CreateWarning(42, "server warning"),
            CreateWarning(43, string.Empty)
        };

        ErrorResponse response = CreateErrorResponse(
            XrplErrorCodes.InvalidParams,
            new
            {
                command = "account_info"
            },
            warnings: warnings);

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        CollectionAssert.AreEqual(new[] { "server warning" }, (System.Collections.ICollection)info.Warnings);
    }

    [TestMethod]
    public void Classify_JObjectRequest_UsesRequestDirectly()
    {
        JsonObject request = JsonNode.Parse("{\"command\":\"gateway_balances\",\"hotwallet\":[\"rHot1\"]}")!.AsObject();
        ErrorResponse response = CreateErrorResponse(XrplErrorCodes.InvalidHotWallet, request);

        XrplErrorInfo info = XrplErrorClassifier.Classify(response);

        Assert.AreEqual("gateway_balances", info.Command);
        Assert.AreEqual("hotwallet", info.FieldName);
        Assert.AreEqual("[\"rHot1\"]", info.FieldValue);
    }

    private static ErrorResponse CreateErrorResponse(
        string errorCode,
        object? request = null,
        string? errorMessage = null,
        List<RippleResponseWarning>? warnings = null)
    {
        return new ErrorResponse
        {
            Error = errorCode,
            ErrorMessage = errorMessage ?? errorCode,
            Request = request ?? new { },
            Warnings = warnings
        };
    }

    private static RippleResponseWarning CreateWarning(uint id, string message)
    {
        return new RippleResponseWarning
        {
            Id = id,
            Message = message
        };
    }

    private static void AssertArgumentNullException(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentNullException)
        {
            return;
        }

        throw new AssertFailedException("Expected ArgumentNullException was not thrown.");
    }
}
