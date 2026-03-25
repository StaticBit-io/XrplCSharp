using System;
using System.Collections.Generic;

using Newtonsoft.Json.Linq;

using Xrpl.Models.Common;
using Xrpl.Models.Subscriptions;

namespace Xrpl.Client.Exceptions;

public static class XrplErrorClassifier
{
    public static XrplErrorInfo Classify(Exception exception)
    {
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));

        return exception switch
        {
            RippledException rippled => Classify(rippled),
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

    public static XrplErrorInfo Classify(RippledException exception)
    {
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));
        return Classify(exception.Response);
    }

    public static XrplErrorInfo Classify(ErrorResponse response)
    {
        if (response == null) throw new ArgumentNullException(nameof(response));

        var error = response.Error?.Trim() ?? string.Empty;
        var request = ToJObjectSafe(response.Request);

        var command = GetString(request, "command");
        var warnings = ExtractWarnings(response);

        return error switch
        {
            XrplErrorCodes.MalformedCurrency => BuildMalformedCurrency(response, request, command, warnings),

            XrplErrorCodes.ActMalformed => BuildInvalidAccount(response, request, command, warnings),

            XrplErrorCodes.MalformedAddress or XrplErrorCodes.MalformedOwner => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = response.ErrorMessage,
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

            XrplErrorCodes.ActNotFound => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = response.ErrorMessage,
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

            XrplErrorCodes.TxnNotFound => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = response.ErrorMessage,
                Category = XrplErrorCategory.NotFound,
                Subject = XrplErrorSubject.Transaction,
                Title = "Transaction not found",
                UserMessage = "The requested transaction was not found.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                Warnings = warnings
            },

            XrplErrorCodes.InvalidParams or XrplErrorCodes.MalformedRequest or XrplErrorCodes.JsonInvalid or XrplErrorCodes.MissingCommand => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = response.ErrorMessage,
                Category = XrplErrorCategory.BadRequest,
                Subject = XrplErrorSubject.Request,
                Title = "Incorrect request",
                UserMessage = "The server could not process the request due to invalid parameters or structure.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                Warnings = warnings
            },

            XrplErrorCodes.LedgerNotFound => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = response.ErrorMessage,
                Category = XrplErrorCategory.LedgerUnavailable,
                Subject = XrplErrorSubject.Ledger,
                Title = "Ledger not found",
                UserMessage = "The requested ledger was not found or is not available on the current server.",
                IsRetryable = true,
                IsUserFixable = true,
                Command = command,
                Warnings = warnings
            },

            XrplErrorCodes.TooBusy or XrplErrorCodes.NoCurrent or XrplErrorCodes.NoNetwork or XrplErrorCodes.NoClosed or XrplErrorCodes.FailedToForward => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = response.ErrorMessage,
                Category = XrplErrorCategory.TemporaryServerProblem,
                Subject = XrplErrorSubject.Server,
                Title = "Temporary server problem",
                UserMessage = "The XRPL server is temporarily unable to process the request. Try again later.",
                IsRetryable = true,
                IsUserFixable = false,
                Command = command,
                Warnings = warnings
            },

            XrplErrorCodes.UnknownCommand or XrplErrorCodes.DeprecatedFeature or XrplErrorCodes.InvalidApiVersion => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = response.ErrorMessage,
                Category = XrplErrorCategory.UnsupportedRequest,
                Subject = XrplErrorSubject.Request,
                Title = "Command not supported",
                UserMessage = "The current server does not support this command, parameter, or API version.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                Warnings = warnings
            },

            XrplErrorCodes.AmendmentBlocked => new XrplErrorInfo
            {
                RawError = error,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = response.ErrorMessage,
                Category = XrplErrorCategory.ServerState,
                Subject = XrplErrorSubject.Server,
                Title = "Server requires upgrade",
                UserMessage = "The amendment server is blocked and cannot work correctly with the current network state.",
                IsRetryable = false,
                IsUserFixable = false,
                Command = command,
                Warnings = warnings
            },

            _ => BuildUnknown(response, request, command, warnings)
        };
    }

    private static XrplErrorInfo BuildMalformedCurrency(
        ErrorResponse response,
        JObject? request,
        string? command,
        IReadOnlyList<string> warnings)
    {
        var currency = GetString(request, "currency")
                       ?? GetString(request, "ripple_state", "currency");

        return new XrplErrorInfo
        {
            RawError = response.Error ?? string.Empty,
            RawErrorCode = response.ErrorCode,
            RawErrorMessage = response.ErrorMessage,
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

    private static XrplErrorInfo BuildInvalidAccount(
        ErrorResponse response,
        JObject? request,
        string? command,
        IReadOnlyList<string> warnings)
    {
        var account = GetString(request, "account");

        return new XrplErrorInfo
        {
            RawError = response.Error ?? string.Empty,
            RawErrorCode = response.ErrorCode,
            RawErrorMessage = response.ErrorMessage,
            Category = XrplErrorCategory.InvalidInput,
            Subject = XrplErrorSubject.Account,
            Title = "Incorrect account address",
            UserMessage = account == null
                ? "The account address is not in the correct format."
                : $"The account address '{account}' is not in the correct format.",
            IsRetryable = false,
            IsUserFixable = true,
            Command = command,
            FieldName = "account",
            FieldValue = account,
            Warnings = warnings
        };
    }

    private static XrplErrorInfo BuildEntryNotFound(
        ErrorResponse response,
        JObject? request,
        string? command,
        IReadOnlyList<string> warnings)
    {
        if (request?["vault_id"] != null)
        {
            return new XrplErrorInfo
            {
                RawError = response.Error ?? string.Empty,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = response.ErrorMessage,
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
            var currency = GetString(request, "ripple_state", "currency")?.CurrencyReadableName();

            return new XrplErrorInfo
            {
                RawError = response.Error ?? string.Empty,
                RawErrorCode = response.ErrorCode,
                RawErrorMessage = response.ErrorMessage,
                Category = XrplErrorCategory.NotFound,
                Subject = XrplErrorSubject.TrustLine,
                Title = "Trustline not found",
                UserMessage = currency == null
                    ? "Trustline or matching ledger object not found."
                    : $"Trust line for currency '{currency}' not found.",
                IsRetryable = false,
                IsUserFixable = true,
                Command = command,
                FieldName = "currency",
                FieldValue = currency,
                Warnings = warnings
            };
        }

        return new XrplErrorInfo
        {
            RawError = response.Error ?? string.Empty,
            RawErrorCode = response.ErrorCode,
            RawErrorMessage = response.ErrorMessage,
            Category = XrplErrorCategory.NotFound,
            Subject = XrplErrorSubject.Unknown,
            Title = "Object not found",
            UserMessage = response.ErrorMessage ?? "The requested object was not found.",
            IsRetryable = false,
            IsUserFixable = true,
            Command = command,
            Warnings = warnings
        };
    }

    private static XrplErrorInfo BuildUnknown(
        ErrorResponse response,
        JObject? request,
        string? command,
        IReadOnlyList<string> warnings)
    {
        return new XrplErrorInfo
        {
            RawError = response.Error ?? string.Empty,
            RawErrorCode = response.ErrorCode,
            RawErrorMessage = response.ErrorMessage,
            Category = XrplErrorCategory.Unknown,
            Subject = XrplErrorSubject.Unknown,
            Title = "Unknown XRPL error",
            UserMessage = response.ErrorMessage ?? "The XRPL server returned an unknown error.",
            IsRetryable = false,
            IsUserFixable = false,
            Command = command,
            Warnings = warnings
        };
    }

    private static JObject? ToJObjectSafe(object? request)
    {
        if (request == null)
            return null;

        if (request is JObject jObject)
            return jObject;

        try
        {
            return JObject.FromObject(request);
        }
        catch
        {
            return null;
        }
    }

    private static string? GetString(JObject? request, params string[] path)
    {
        if (request == null || path.Length == 0)
            return null;

        JToken? token = request;

        foreach (string segment in path)
        {
            token = token?[segment];
            if (token == null)
                return null;
        }

        return token.Type == JTokenType.Null ? null : token.Value<string>();
    }

    private static IReadOnlyList<string> ExtractWarnings(ErrorResponse response)
    {
        // Подстрой под свою BaseResponse, если там есть Warnings.
        // Сейчас это safe-заглушка.
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
                // информационное сообщение, ответ получен от Clio: обычно это не причина ошибки.
                continue;
            }
            else if (!string.IsNullOrWhiteSpace(msg))
                result.Add(msg!);
        }

        return result;
    }
}