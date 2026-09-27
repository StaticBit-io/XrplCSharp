using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

using Xrpl.Client.Json;
using Xrpl.Models.Common;
using Xrpl.Models.Subscriptions;

namespace Xrpl.Client.Exceptions;

public static class XrplErrorClassifier
{
    public static XrplErrorInfo Classify(this Exception exception)
    {
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));

        return exception switch
        {
            RippledException rippled => rippled.Classify(),
            _ => new XrplErrorInfo
            {
                RawError = exception.GetType().Name,
                RawErrorMessage = exception.Message,
                Category = XrplErrorCategory.Unknown,
                Subject = XrplErrorSubject.Unknown,
                Title = "internal error",
                UserMessage = exception.Message,
                IsRetryable = false,
                IsUserFixable = false
            }
        };
    }

    public static XrplErrorInfo Classify(this RippledException exception)
    {
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));

        if (exception.Response == null)
            return new XrplErrorInfo
            {
                RawError = string.Empty,
                RawErrorMessage = exception.Message,
                Category = XrplErrorCategory.Unknown,
                Subject = XrplErrorSubject.Unknown,
                Title = "internal error",
                UserMessage = exception.Message,
                IsRetryable = false,
                IsUserFixable = false
            };

        return exception.Response.Classify();
    }

    public static XrplErrorInfo Classify(this ErrorResponse response)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));

        var error = response.Error?.Trim() ?? string.Empty;
        var rawMessage = response.ErrorMessage ?? response.ErrorException;
        var request = ToJsonObjectSafe(response.RawRequest);

        var command = GetString(request, "command");
        var warnings = ExtractWarnings(response);

        return error switch
        {
            XrplErrorCodes.MalformedCurrency => BuildMalformedCurrency(response, request, command, warnings),

            XrplErrorCodes.MalformedDocumentId => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.InvalidInput,
                Subject = XrplErrorSubject.Request,
                Title = "Incorrect document id",
                UserMessage = "The oracle document id in the request is not in the correct format.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                FieldName = "oracle_document_id",
                FieldValue = GetValueText(request, "oracle", "oracle_document_id"),
                Warnings = warnings
            },

            // This code is treated neutrally because different XRPL methods may use it
            // for different malformed request values such as addresses or markers.
            XrplErrorCodes.ActMalformed => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.InvalidInput,
                Subject = XrplErrorSubject.Request,
                Title = "Incorrect request value",
                UserMessage = "One of the request values is not in the correct format.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                Warnings = warnings
            },

            XrplErrorCodes.SourceAccountMalformed => BuildInvalidAccountAddress(
                response,
                request,
                command,
                warnings,
                "Incorrect source account address",
                "source account address",
                "The source account address is not in the correct format.",
                "source_account",
                "account"),

            XrplErrorCodes.DestinationAccountMalformed => BuildInvalidAccountAddress(
                response,
                request,
                command,
                warnings,
                "Incorrect destination account address",
                "destination account address",
                "The destination account address is not in the correct format.",
                "destination_account",
                "destination"),

            XrplErrorCodes.MalformedAddress or XrplErrorCodes.MalformedOwner => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.InvalidInput,
                Subject = XrplErrorSubject.Address,
                Title = "Incorrect address",
                UserMessage = "One of the addresses in the request is not in the correct format.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                Warnings = warnings
            },

            XrplErrorCodes.EntryNotFound => BuildEntryNotFound(response, request, command, warnings),

            XrplErrorCodes.ObjectNotFound => BuildObjectNotFound(response, command, warnings),

            XrplErrorCodes.UnexpectedLedgerType => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.InvalidInput,
                Subject = XrplErrorSubject.Ledger,
                Title = "Unexpected ledger entry type",
                UserMessage = "The provided ledger entry identifier does not match the expected ledger entry type.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                Warnings = warnings
            },

            XrplErrorCodes.ActNotFound => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.NotFound,
                Subject = XrplErrorSubject.Account,
                Title = "Account not found",
                UserMessage = "The specified account was not found in the selected ledger or is not activated.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                FieldName = "account",
                FieldValue = GetString(request, "account"),
                Warnings = warnings
            },

            XrplErrorCodes.SourceAccountMissing => BuildMissingRequiredField(
                response,
                command,
                warnings,
                XrplErrorSubject.Account,
                "Source account is missing",
                "The request does not include the required source account.",
                "source_account"),

            XrplErrorCodes.DestinationAccountMissing => BuildMissingRequiredField(
                response,
                command,
                warnings,
                XrplErrorSubject.Account,
                "Destination account is missing",
                "The request does not include the required destination account.",
                "destination_account"),

            XrplErrorCodes.SourceAccountNotFound => BuildAccountNotFound(
                response,
                request,
                command,
                warnings,
                "Source account not found",
                "The source account was not found in the selected ledger or is not activated.",
                "source_account",
                "account"),

            XrplErrorCodes.DestinationAccountNotFound => BuildAccountNotFound(
                response,
                request,
                command,
                warnings,
                "Destination account not found",
                "The destination account was not found in the selected ledger or is not activated.",
                "destination_account",
                "destination"),

            XrplErrorCodes.TxnNotFound => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.NotFound,
                Subject = XrplErrorSubject.Transaction,
                Title = "Transaction not found",
                UserMessage = "The requested transaction was not found.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                Warnings = warnings
            },

            XrplErrorCodes.InvalidParams
                or XrplErrorCodes.MalformedRequest
                or XrplErrorCodes.JsonInvalid
                or XrplErrorCodes.CommandMissing
                or XrplErrorCodes.UnknownOption
                or XrplErrorCodes.WsTextRequired => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.BadRequest,
                Subject = XrplErrorSubject.Request,
                Title = "Incorrect request",
                UserMessage = "The server could not process the request due to invalid parameters or structure.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                Warnings = warnings
            },

            XrplErrorCodes.DestinationAmountMissing => BuildMissingRequiredField(
                response,
                command,
                warnings,
                XrplErrorSubject.Request,
                "Destination amount is missing",
                "The request does not include the required destination amount.",
                "destination_amount"),

            XrplErrorCodes.LedgerIndexMalformed => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.BadRequest,
                Subject = XrplErrorSubject.Ledger,
                Title = "Incorrect ledger selector",
                UserMessage = "The ledger selector in the request is not in the correct format.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                FieldName = GetFirstFieldName(request, "ledger_hash", "ledger_index"),
                FieldValue = GetFirstValueText(request, "ledger_hash", "ledger_index"),
                Warnings = warnings
            },

            XrplErrorCodes.ExcessiveLedgerRange
                or XrplErrorCodes.InvalidLedgerRange
                or XrplErrorCodes.LedgerIndicesInvalid => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.BadRequest,
                Subject = XrplErrorSubject.Request,
                Title = error == XrplErrorCodes.LedgerIndicesInvalid
                    ? "Incorrect ledger indexes"
                    : "Incorrect ledger range",
                UserMessage = error == XrplErrorCodes.LedgerIndicesInvalid
                    ? "The provided ledger indexes are not valid for this request."
                    : "The ledger range in the request is invalid or exceeds the supported size.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                FieldName = "ledger_range",
                FieldValue = BuildLedgerRange(request),
                Warnings = warnings
            },

            XrplErrorCodes.LedgerNotFound => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.LedgerUnavailable,
                Subject = XrplErrorSubject.Ledger,
                Title = "Ledger not found",
                UserMessage = "The requested ledger was not found or is not available on the current server.",
                IsRetryable = true,
                IsUserFixable = true,
                Command = command,
                Warnings = warnings
            },

            XrplErrorCodes.LedgerNotValidated => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.LedgerUnavailable,
                Subject = XrplErrorSubject.Ledger,
                Title = "Ledger not validated",
                UserMessage = "The requested ledger is not validated yet on the current server.",
                IsRetryable = true,
                IsUserFixable = true,
                Command = command,
                Warnings = warnings
            },

            XrplErrorCodes.TooBusy
                or XrplErrorCodes.NoCurrent
                or XrplErrorCodes.NoNetwork
                or XrplErrorCodes.NoClosed
                or XrplErrorCodes.NotReady
                or XrplErrorCodes.NotSynced
                or XrplErrorCodes.FailedToForward => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.TemporaryServerProblem,
                Subject = XrplErrorSubject.Server,
                Title = "Temporary server problem",
                UserMessage = "The XRPL server is temporarily unable to process the request. Try again later.",
                IsRetryable = true,
                IsUserFixable = false,
                Command = command,
                Warnings = warnings
            },

            XrplErrorCodes.NoEvents => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.UnsupportedRequest,
                Subject = XrplErrorSubject.Request,
                Title = "Streaming not supported",
                UserMessage = "The current transport does not support subscriptions or event streams.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                Warnings = warnings
            },

            XrplErrorCodes.NoPermission => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.UnsupportedRequest,
                Subject = XrplErrorSubject.Request,
                Title = "Permission required",
                UserMessage = "The server requires elevated permissions for this request or option.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                Warnings = warnings
            },

            XrplErrorCodes.UnknownCommand
                or XrplErrorCodes.Deprecated
                or XrplErrorCodes.InvalidApiVersion
                or XrplErrorCodes.NotEnabled
                or XrplErrorCodes.NotImplemented
                or XrplErrorCodes.NotSupported => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.UnsupportedRequest,
                Subject = XrplErrorSubject.Request,
                Title = "Command not supported",
                UserMessage = "The current server does not support this command, parameter, or API version.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                Warnings = warnings
            },

            XrplErrorCodes.InvalidHotWallet => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.InvalidInput,
                Subject = XrplErrorSubject.Address,
                Title = "Incorrect hot wallet",
                UserMessage = "One or more hot wallet addresses are not valid for the requested issuing account.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                FieldName = "hotwallet",
                FieldValue = GetValueText(request, "hotwallet"),
                Warnings = warnings
            },

            XrplErrorCodes.PublicMalformed => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.InvalidInput,
                Subject = XrplErrorSubject.Request,
                Title = "Incorrect public key",
                UserMessage = "The public key in the request is not in the correct format.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                FieldName = "public_key",
                FieldValue = GetFirstValueText(request, "public_key"),
                Warnings = warnings
            },

            XrplErrorCodes.SendMaxMalformed => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.InvalidInput,
                Subject = XrplErrorSubject.Request,
                Title = "Incorrect SendMax amount",
                UserMessage = "The SendMax value in the request is not in the correct format.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                FieldName = "send_max",
                FieldValue = GetFirstValueText(request, "send_max", "SendMax"),
                Warnings = warnings
            },

            XrplErrorCodes.IssueMalformed => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.InvalidInput,
                Subject = XrplErrorSubject.Request,
                Title = "Incorrect issue",
                UserMessage = "The issue in the request is not in the correct format.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                FieldName = "issue",
                FieldValue = GetFirstValueText(request, "issue"),
                Warnings = warnings
            },

            XrplErrorCodes.SourceCurrencyMalformed or XrplErrorCodes.DestinationAmountMalformed => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.InvalidInput,
                Subject = XrplErrorSubject.Request,
                Title = "Incorrect order book asset",
                UserMessage = "One of the order book assets in the request is not in the correct format.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                FieldName = error == XrplErrorCodes.SourceCurrencyMalformed ? "taker_pays" : "taker_gets",
                FieldValue = error == XrplErrorCodes.SourceCurrencyMalformed
                    ? GetValueText(request, "taker_pays")
                    : GetValueText(request, "taker_gets"),
                Warnings = warnings
            },

            XrplErrorCodes.SourceIssuerMalformed or XrplErrorCodes.DestinationIssuerMalformed => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.InvalidInput,
                Subject = XrplErrorSubject.Address,
                Title = "Incorrect issuer address",
                UserMessage = "One of the issuer addresses in the request is not in the correct format.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                FieldName = error == XrplErrorCodes.SourceIssuerMalformed ? "taker_pays.issuer" : "taker_gets.issuer",
                FieldValue = error == XrplErrorCodes.SourceIssuerMalformed
                    ? GetValueText(request, "taker_pays", "issuer")
                    : GetValueText(request, "taker_gets", "issuer"),
                Warnings = warnings
            },

            XrplErrorCodes.BadMarket => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.NotFound,
                Subject = XrplErrorSubject.Request,
                Title = "Market not found",
                UserMessage = "The requested market does not exist or is not available for an order book query.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                Warnings = warnings
            },

            XrplErrorCodes.AmendmentBlocked => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.ServerState,
                Subject = XrplErrorSubject.Server,
                Title = "Server requires upgrade",
                UserMessage = "The amendment server is blocked and cannot work correctly with the current network state.",
                IsRetryable = false,
                IsUserFixable = false,
                Command = command,
                Warnings = warnings
            },

            XrplErrorCodes.SlowDown => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.TemporaryServerProblem,
                XrplErrorSubject.Server,
                "Too many requests",
                "The server asks to reduce the request rate. Wait and try again.",
                isRetryable: true,
                isUserFixable: false),

            XrplErrorCodes.JsonRpc => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.TemporaryServerProblem,
                XrplErrorSubject.Server,
                "Transport error",
                "The server could not forward the request over JSON-RPC. Try again later.",
                isRetryable: true,
                isUserFixable: false),

            XrplErrorCodes.HighFee => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.TemporaryServerProblem,
                XrplErrorSubject.Transaction,
                "Fee above the limit",
                "The current transaction fee exceeds the limit set for signing. Try again when the load drops, or raise the limit.",
                isRetryable: true,
                isUserFixable: true),

            XrplErrorCodes.UnlBlocked => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.ServerState,
                XrplErrorSubject.Server,
                "Validator list expired",
                "The server's validator list has expired, so it cannot follow the network until it gets a current one.",
                isRetryable: false,
                isUserFixable: false),

            XrplErrorCodes.WrongNetwork => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.ServerState,
                XrplErrorSubject.Server,
                "Wrong network",
                "The server belongs to a different network than the request expects. Connect to a server of the intended network.",
                isRetryable: false,
                isUserFixable: true),

            XrplErrorCodes.Internal => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.ServerState,
                XrplErrorSubject.Server,
                "Internal server error",
                "The server failed while processing the request. The same request is unlikely to succeed on the same server; another server may answer it.",
                isRetryable: false,
                isUserFixable: false),

            XrplErrorCodes.DatabaseDeserialization => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.ServerState,
                XrplErrorSubject.Server,
                "Server database error",
                "The server could not read data from its database.",
                isRetryable: false,
                isUserFixable: false),

            XrplErrorCodes.Forbidden => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.UnsupportedRequest,
                XrplErrorSubject.Request,
                "Access denied",
                "The server refused the credentials of this connection.",
                isRetryable: false,
                isUserFixable: true),

            XrplErrorCodes.DelegateAccountNotFound => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.NotFound,
                XrplErrorSubject.Account,
                "Delegate account not found",
                "The delegate account was not found in the selected ledger.",
                isRetryable: false,
                isUserFixable: true),

            XrplErrorCodes.NoPathRequest => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.NotFound,
                XrplErrorSubject.Request,
                "No path request",
                "There is no pathfinding request in progress on this connection.",
                isRetryable: false,
                isUserFixable: true),

            XrplErrorCodes.MasterDisabled => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.InvalidInput,
                XrplErrorSubject.Account,
                "Master key disabled",
                "The account has disabled its master key; sign with its regular key or signer list.",
                isRetryable: false,
                isUserFixable: true),

            XrplErrorCodes.BadCredentials => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.InvalidInput,
                XrplErrorSubject.Account,
                "Credentials not accepted",
                "The credentials do not exist, are not accepted, or have expired.",
                isRetryable: false,
                isUserFixable: true),

            XrplErrorCodes.BadSecret or XrplErrorCodes.BadSeed or XrplErrorCodes.BadKeyType => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.InvalidInput,
                XrplErrorSubject.Account,
                "Incorrect signing key",
                "The secret, seed or key type in the request cannot be used to sign for the account.",
                isRetryable: false,
                isUserFixable: true),

            XrplErrorCodes.BadIssuer => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.InvalidInput,
                XrplErrorSubject.Address,
                "Incorrect issuer",
                "The issuer account in the request is not in the correct format.",
                isRetryable: false,
                isUserFixable: true),

            XrplErrorCodes.BadFeature => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.InvalidInput,
                XrplErrorSubject.Request,
                "Unknown amendment",
                "The amendment in the request is unknown or invalid.",
                isRetryable: false,
                isUserFixable: true),

            XrplErrorCodes.ChannelMalformed or XrplErrorCodes.ChannelAmountMalformed => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.InvalidInput,
                XrplErrorSubject.Request,
                "Incorrect payment channel",
                "The payment channel or its amount in the request is not in the correct format.",
                isRetryable: false,
                isUserFixable: true),

            XrplErrorCodes.DomainMalformed or XrplErrorCodes.OracleMalformed => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.InvalidInput,
                XrplErrorSubject.Request,
                "Incorrect ledger object selector",
                "The domain or oracle in the request is not in the correct format.",
                isRetryable: false,
                isUserFixable: true),

            XrplErrorCodes.AlreadyMultisig or XrplErrorCodes.AlreadySingleSig => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.BadRequest,
                XrplErrorSubject.Transaction,
                "Transaction already signed",
                "The transaction already carries a signature of the other kind: single and multi-signatures cannot be combined.",
                isRetryable: false,
                isUserFixable: true),

            XrplErrorCodes.SigningMalformed or XrplErrorCodes.TransactionSigned => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.BadRequest,
                XrplErrorSubject.Transaction,
                "Incorrect signing request",
                "The transaction cannot be signed in this form, or it is already signed where an unsigned one is expected.",
                isRetryable: false,
                isUserFixable: true),

            XrplErrorCodes.BadSyntax or XrplErrorCodes.MalformedStream => Plain(
                response,
                error,
                rawMessage,
                command,
                warnings,
                XrplErrorCategory.BadRequest,
                XrplErrorSubject.Request,
                "Malformed request",
                "The request or one of its streams is not in the correct format.",
                isRetryable: false,
                isUserFixable: true),

            _ => BuildUnknown(response, request, command, warnings)
        };
    }

    private static XrplErrorInfo Plain(
        ErrorResponse response,
        string error,
        string? rawMessage,
        string? command,
        IReadOnlyList<string> warnings,
        XrplErrorCategory category,
        XrplErrorSubject subject,
        string title,
        string userMessage,
        bool isRetryable,
        bool isUserFixable) => new XrplErrorInfo
    {
        RawError = error,
        RawErrorCode = response.ErrorCode,
        RawErrorMessage = rawMessage,
        Category = category,
        Subject = subject,
        Title = title,
        UserMessage = userMessage,
        IsRetryable = isRetryable,
        IsUserFixable = isUserFixable,
        Command = command,
        Warnings = warnings
    };

    private static XrplErrorInfo BuildMalformedCurrency(
        ErrorResponse response,
        JsonObject? request,
        string? command,
        IReadOnlyList<string> warnings)
    {
        string? rawMessage = response.ErrorMessage ?? response.ErrorException;
        var currency = GetString(request, "currency")
                       ?? GetString(request, "ripple_state", "currency");

        return new XrplErrorInfo
        {
            RawError = response.Error ?? string.Empty,
            RawErrorCode = response.ErrorCode,
            RawErrorMessage = rawMessage,
            Category = XrplErrorCategory.InvalidInput,
            Subject = XrplErrorSubject.Currency,
            Title = "Incorrect currency code",
            UserMessage = currency == null
                ? "The currency code is not in the correct format."
                : $"The currency code '{currency}' is not in the correct format.",
            IsRetryable = false,
            IsUserFixable = true,
            Command = command,
            FieldName = "currency",
            FieldValue = currency,
            Warnings = warnings
        };
    }

    private static XrplErrorInfo BuildInvalidAccountAddress(
        ErrorResponse response,
        JsonObject? request,
        string? command,
        IReadOnlyList<string> warnings,
        string title,
        string fieldLabel,
        string fallbackMessage,
        params string[] fieldNames)
    {
        string? rawMessage = response.ErrorMessage ?? response.ErrorException;
        string? account = GetFirstString(request, fieldNames);

        return new XrplErrorInfo
        {
            RawError = response.Error ?? string.Empty,
            RawErrorCode = response.ErrorCode,
            RawErrorMessage = rawMessage,
            Category = XrplErrorCategory.InvalidInput,
            Subject = XrplErrorSubject.Account,
            Title = title,
            UserMessage = account == null
                ? fallbackMessage
                : $"The {fieldLabel} '{account}' is not in the correct format.",
            IsRetryable = false,
            IsUserFixable = true,
            Command = command,
            FieldName = GetFirstFieldName(request, fieldNames),
            FieldValue = account,
            Warnings = warnings
        };
    }

    private static XrplErrorInfo BuildMissingRequiredField(
        ErrorResponse response,
        string? command,
        IReadOnlyList<string> warnings,
        XrplErrorSubject subject,
        string title,
        string userMessage,
        string fieldName)
    {
        string? rawMessage = response.ErrorMessage ?? response.ErrorException;
        return new XrplErrorInfo
        {
            RawError = response.Error ?? string.Empty,
            RawErrorCode = response.ErrorCode,
            RawErrorMessage = rawMessage,
            Category = XrplErrorCategory.BadRequest,
            Subject = subject,
            Title = title,
            UserMessage = userMessage,
            IsRetryable = false,
            IsUserFixable = true,
            Command = command,
            FieldName = fieldName,
            Warnings = warnings
        };
    }

    private static XrplErrorInfo BuildAccountNotFound(
        ErrorResponse response,
        JsonObject? request,
        string? command,
        IReadOnlyList<string> warnings,
        string title,
        string userMessage,
        params string[] fieldNames)
    {
        string? rawMessage = response.ErrorMessage ?? response.ErrorException;
        return new XrplErrorInfo
        {
            RawError = response.Error ?? string.Empty,
            RawErrorCode = response.ErrorCode,
            RawErrorMessage = rawMessage,
            Category = XrplErrorCategory.NotFound,
            Subject = XrplErrorSubject.Account,
            Title = title,
            UserMessage = userMessage,
            IsRetryable = false,
            IsUserFixable = true,
            Command = command,
            FieldName = GetFirstFieldName(request, fieldNames),
            FieldValue = GetFirstString(request, fieldNames),
            Warnings = warnings
        };
    }

    private static XrplErrorInfo BuildEntryNotFound(
        ErrorResponse response,
        JsonObject? request,
        string? command,
        IReadOnlyList<string> warnings)
    {
        string? rawMessage = response.ErrorMessage ?? response.ErrorException;
        if (request?["vault_id"] != null)
        {
            return new XrplErrorInfo
            {
                RawError = response.Error ?? string.Empty,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.NotFound,
                Subject = XrplErrorSubject.Vault,
                Title = "Vault not found",
                UserMessage = "The requested vault not found.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                FieldName = "vault_id",
                FieldValue = GetString(request, "vault_id"),
                Warnings = warnings
            };
        }

        if (request?["ripple_state"] != null)
        {
            var currency = GetString(request, "ripple_state", "currency");
            var readable = currency?.CurrencyReadableName();
            return new XrplErrorInfo
            {
                RawError = response.Error ?? string.Empty,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = rawMessage,
                Category = XrplErrorCategory.NotFound,
                Subject = XrplErrorSubject.TrustLine,
                Title = "Trustline not found",
                UserMessage = currency == null
                    ? "Trustline or matching ledger object not found."
                    : $"Trustline for currency '{readable}' not found.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                FieldName = "currency",
                FieldValue = currency,
                Warnings = warnings
            };
        }

        return BuildObjectNotFound(response, command, warnings);
    }

    private static XrplErrorInfo BuildObjectNotFound(
        ErrorResponse response,
        string? command,
        IReadOnlyList<string> warnings)
    {
        string rawMessage = response.ErrorMessage ?? response.ErrorException;
        return new XrplErrorInfo
        {
            RawError = response.Error ?? string.Empty,
            RawErrorCode = response.ErrorCode,
            RawErrorMessage = rawMessage,
            Category = XrplErrorCategory.NotFound,
            Subject = XrplErrorSubject.Unknown,
            Title = "Object not found",
            UserMessage = rawMessage ?? "The requested object was not found.",
            IsRetryable = false,
            IsUserFixable = true,
            Command = command,
            Warnings = warnings
        };
    }

    private static XrplErrorInfo BuildUnknown(
        ErrorResponse response,
        JsonObject? request,
        string? command,
        IReadOnlyList<string> warnings)
    {
        string rawMessage = response.ErrorMessage ?? response.ErrorException;
        return new XrplErrorInfo
        {
            RawError = response.Error ?? string.Empty,
            RawErrorCode = response.ErrorCode,
            RawErrorMessage = rawMessage,
            Category = XrplErrorCategory.Unknown,
            Subject = XrplErrorSubject.Unknown,
            Title = "Unknown XRPL error",
            UserMessage = rawMessage ?? "The XRPL server returned an unknown error.",
            IsRetryable = false,
            IsUserFixable = false,
            Command = command,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Parses the request echo straight out of its captured bytes.
    /// </summary>
    /// <remarks>
    /// <paramref name="request"/> used to arrive as a <see cref="JsonElement"/> (or, for a
    /// hand-built response, an arbitrary object serialized to reach one); now it is the raw
    /// window <see cref="ErrorResponse.RawRequest"/> captured off the wire, parsed once here
    /// instead of once by System.Text.Json to build the element and again by this method.
    /// </remarks>
    private static JsonObject? ToJsonObjectSafe(RawJson request)
    {
        if (request.IsEmpty)
            return null;

        try
        {
            return JsonNode.Parse(request.Span) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonObject? request, params string[] path)
    {
        if (request == null || path.Length == 0)
            return null;

        JsonNode? token = request;

        foreach (string segment in path)
        {
            if (token is not JsonObject obj)
                return null;
            token = obj[segment];
            if (token == null)
                return null;
        }

        if (token is JsonValue value && value.TryGetValue<string>(out string? str))
            return str;

        return token.GetValueKind() == JsonValueKind.Null ? null : token.ToString();
    }

    private static string? GetFirstString(JsonObject? request, params string[] fieldNames)
    {
        if (request == null || fieldNames.Length == 0)
            return null;

        foreach (string fieldName in fieldNames)
        {
            string? value = GetString(request, fieldName);
            if (value != null)
                return value;
        }

        return null;
    }

    private static string? GetValueText(JsonObject? request, params string[] path)
    {
        if (request == null || path.Length == 0)
            return null;

        JsonNode? token = request;

        foreach (string segment in path)
        {
            if (token is not JsonObject obj)
                return null;
            token = obj[segment];
            if (token == null)
                return null;
        }

        if (token.GetValueKind() == JsonValueKind.Null)
            return null;

        if (token is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string? str))
            return str;

        return token.ToJsonString();
    }

    private static string? GetFirstValueText(JsonObject? request, params string[] fieldNames)
    {
        if (request == null || fieldNames.Length == 0)
            return null;

        foreach (string fieldName in fieldNames)
        {
            string? value = GetValueText(request, fieldName);
            if (value != null)
                return value;
        }

        return null;
    }

    private static string? GetFirstFieldName(JsonObject? request, params string[] fieldNames)
    {
        if (fieldNames.Length == 0)
            return null;

        if (request == null)
            return fieldNames[0];

        foreach (string fieldName in fieldNames)
        {
            if (request[fieldName] != null)
                return fieldName;
        }

        return fieldNames[0];
    }

    private static string? BuildLedgerRange(JsonObject? request)
    {
        string? minLedger = GetValueText(request, "min_ledger")
                            ?? GetValueText(request, "ledger_index_min");
        string? maxLedger = GetValueText(request, "max_ledger")
                            ?? GetValueText(request, "ledger_index_max");

        if (minLedger == null && maxLedger == null)
            return null;

        return $"{minLedger ?? "?"}..{maxLedger ?? "?"}";
    }

    private static IReadOnlyList<string> ExtractWarnings(ErrorResponse response)
    {
        // Collect the warning messages worth surfacing; an absent or empty
        // Warnings list yields an empty result rather than null.
        var result = new List<string>();

        if (response.Warnings is not { Count: > 0 } warnings)
        {
            return result;
        }

        foreach (var warning in warnings)
        {
            var id = warning.Id;
            var msg = warning.Message;

            if (id == 2001)
            {
                // An informational message: an answer came from Clio, which is usually not the cause of an error.
                continue;
            }
            else if (!string.IsNullOrWhiteSpace(msg))
                result.Add(msg!);
        }

        return result;
    }
}