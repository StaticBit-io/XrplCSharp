# Error Classifier

## Overview

`XrplErrorClassifier` converts exceptions and XRPL error payloads into a normalized `XrplErrorInfo` object that can be used by application code, logs, telemetry, or user-facing error handling.

This is useful when the caller needs a stable interpretation layer instead of branching on raw rippled error strings.

Relevant code:

- `Xrpl/Client/Exceptions/XrplErrorClassifier.cs`
- `Xrpl/Client/Exceptions/XrplErrorInfo.cs`
- `Xrpl/Models/Subscriptions/ErrorResponse.cs`

## Available Overloads

`XrplErrorClassifier` currently exposes three overloads:

### `Classify(Exception exception)`

Use this overload when the catch block handles any exception type. If the exception is actually a `RippledException`, the classifier delegates to the XRPL-specific path. Otherwise, it returns a generic `XrplErrorInfo` with `Category = Unknown` and `Subject = Unknown`.

This is the recommended default overload for most application code because it works well with a single `catch (Exception e)` block.

### `Classify(RippledException exception)`

Use this overload when the caller already knows that the failure came from a rippled `status = "error"` response. The classifier reads the embedded `ErrorResponse` and forwards to the response-based overload.

### `Classify(ErrorResponse response)`

Use this overload when an `ErrorResponse` is already available without going through exception handling. This path maps XRPL `error` values to a normalized `XrplErrorInfo`.

## How Classification Works

### For regular `Exception`

For a non-`RippledException`, the classifier does not try to infer XRPL-specific semantics. It returns a generic `XrplErrorInfo`:

- `RawError` is populated from the exception type name
- `RawErrorMessage` is populated from the exception message
- `Category` and `Subject` remain `Unknown`
- `Title` is set to `internal error`
- `UserMessage` echoes the original exception message

This behavior keeps non-XRPL failures visible without pretending that the library understands their business meaning.

### For `RippledException` and `ErrorResponse`

For rippled-originated failures, the classifier reads the XRPL error payload and maps:

- `error`
- `error_code`
- `error_message`
- request metadata
- warnings

into a single `XrplErrorInfo` instance.

The resulting object provides a stable interpretation such as invalid input, missing field, object not found, ledger state issue, unsupported feature, or retryable server condition.

## `XrplErrorInfo` Fields

`XrplErrorInfo` contains both raw transport data and normalized metadata:

- `RawError`
  The original error token, or the exception type name for non-XRPL exceptions.

- `RawErrorCode`
  Optional numeric or textual code provided by rippled.

- `RawErrorMessage`
  Original human-readable error message from the server or exception.

- `Category`
  High-level error class such as invalid input, not found, rate limiting, or unknown failure.

- `Subject`
  The main area affected by the error, for example request, account, ledger, address, or transaction.

- `Title`
  Short normalized caption suitable for logs or UI labels.

- `UserMessage`
  Human-readable explanation intended for caller-side presentation.

- `IsRetryable`
  Indicates whether retry logic may make sense.

- `IsUserFixable`
  Indicates whether the caller can likely correct request data and retry.

- `Command`
  XRPL command name, when it can be extracted from the request payload.

- `FieldName`
  Name of the request field most directly associated with the error, when known.

- `FieldValue`
  The value associated with the problematic field, when it can be extracted safely.

- `Warnings`
  Warning tokens carried by the response, if present.

## Recommended Usage Pattern

In most application code, a single catch block is enough:

```csharp
try
{
    var info = await client.AccountInfo(new("rKeiNfRJcDBUhu4rcjQjGLWqa4"));
}
catch (Exception e)
{
    XrplErrorInfo errInfo = XrplErrorClassifier.Classify(e);
}
```

This pattern keeps error handling simple while still preserving XRPL-aware classification when the exception is a `RippledException`.

If a caller needs special handling for raw `RippledException`, it can still catch that type separately. However, it is not required just to obtain `XrplErrorInfo`.

## Example: Generic Exception

```csharp
try
{
    throw new NotImplementedException("Test exception");
}
catch (Exception e)
{
    XrplErrorInfo errInfo = XrplErrorClassifier.Classify(e);
}
```

In this example, the classifier returns a generic normalized structure for a non-XRPL exception.

## Example: XRPL Call Failure

```csharp
try
{
    var info = await client.AccountInfo(new("rKeiNfRJcDBUhu4rcjQjGLWqa4"));
}
catch (Exception e)
{
    XrplErrorInfo errInfo = XrplErrorClassifier.Classify(e);
}
```

If the underlying failure is a rippled error response, the same catch block still produces XRPL-specific classification.

## When to Catch `RippledException` Separately

Use a dedicated `catch (RippledException e)` only when the caller explicitly needs:

- direct access to `e.Response`
- different control flow for XRPL RPC errors versus infrastructure exceptions
- custom logging of raw response payloads before normalization

Otherwise, `catch (Exception e)` followed by `XrplErrorClassifier.Classify(e)` is usually sufficient.
