# XrplCallResult Plan

## Goal

Introduce a non-throwing call pattern that can represent:

- transport or RPC errors returned as `ErrorResponse`
- successful RPC envelopes with business-level failure in transaction submission
- successful RPC envelopes with accepted or applied transaction results

The first implementation step should preserve the existing throwing API and add safe alternatives next to it.

## Current Problem

The current flow is centered around exceptions. When `RequestManager` receives `status = "error"`, it creates a `RippledException` and rejects the pending task. This is appropriate for the existing API surface, but it makes it harder for callers to:

- inspect `ErrorResponse` without exception control flow
- handle RPC failure and transaction engine result in a unified way
- build higher-level workflows where "known failure" should be represented as data

Relevant code:

- `Xrpl/Client/RequestManager.cs`
- `Xrpl/Client/IXrplClient.cs`
- `Xrpl/Models/Transactions/Submit.cs`

## Important Distinction

XRPL has at least two different success dimensions:

1. RPC-level success
   The server accepted the request envelope and returned `status = "success"`.

2. Transaction engine result success
   A `submit`-style response may still contain a non-success `engine_result` even when the RPC envelope itself is successful.

Because of that, a non-throwing wrapper must not collapse all outcomes into a single `IsSuccess` flag without preserving the underlying semantics.

## Proposed Base Contract

Introduce a generic wrapper:

```csharp
public class XrplCallResult<TResponse>
{
    public bool IsError { get; init; }
    public bool IsRpcSuccess { get; init; }

    public TResponse? Response { get; init; }
    public ErrorResponse? ErrorResponse { get; init; }
    public Exception? Exception { get; init; }
    public XrplErrorInfo? ErrorInfo { get; init; }
}
```

### Semantics

- `IsError`
  True when the call ended with a known failure representation instead of a successful response payload.

- `IsRpcSuccess`
  True only when the XRPL response envelope is valid and `status = "success"`.

- `Response`
  Populated for successful RPC calls.

- `ErrorResponse`
  Populated when rippled returned a structured RPC error payload.

- `Exception`
  Populated when the safe path catches an exception that is not represented only by `ErrorResponse`.

- `ErrorInfo`
  Optional normalized classification produced through `XrplErrorClassifier.Classify(...)` when possible.

## Proposed Submission-Specific Contract

Transaction submission needs additional state because `status = "success"` does not guarantee a successful engine result.

```csharp
public class XrplSubmissionResult<TResponse> : XrplCallResult<TResponse>
    where TResponse : Submit
{
    public string? EngineResult { get; init; }
    public int? EngineResultCode { get; init; }
    public string? EngineResultMessage { get; init; }

    public bool IsQueued { get; init; }
    public bool IsApplied { get; init; }
    public bool IsSubmissionAccepted { get; init; }
}
```

### Submission State Rules

- `IsError`
  True when the call ended with `ErrorResponse` or another exception path before a valid submit response was obtained.

- `IsRpcSuccess`
  True when the submit call returned a valid XRPL success envelope.

- `IsSubmissionAccepted`
  True when `EngineResult` is `tesSUCCESS` or `terQUEUED`.

- `IsApplied`
  True when the submit payload reports that the transaction was applied.

- `IsQueued`
  True when the transaction was queued, or equivalently when `EngineResult` is `terQUEUED`.

This keeps RPC success separate from ledger acceptance.

## API Shape

Keep existing throwing methods intact and add safe variants alongside them.

Examples:

```csharp
Task<AccountInfoResponse> AccountInfo(AccountInfoRequest request, CancellationToken cancellationToken = default);
Task<XrplCallResult<AccountInfoResponse>> AccountInfoSafe(AccountInfoRequest request, CancellationToken cancellationToken = default);

Task<Submit> Submit(SubmitRequest request, CancellationToken cancellationToken = default);
Task<XrplSubmissionResult<Submit>> SubmitSafe(SubmitRequest request, CancellationToken cancellationToken = default);
```

This approach avoids a breaking change in `IXrplClient` and lets existing consumers migrate gradually.

## Recommended First-Stage Implementation

Do not refactor `RequestManager` in the first stage.

Instead:

1. Keep `RequestManager` behavior unchanged.
2. Implement safe wrappers at the client layer by calling existing throwing methods.
3. Catch `RippledException` and map it to `ErrorResponse` plus `ErrorInfo`.
4. Catch other exceptions and expose them through `Exception` and, when possible, `ErrorInfo`.
5. For submit methods, derive submission-specific flags from `Submit.EngineResult`, `Submit.Applied`, and related fields.

This gives a low-risk path with minimal protocol-layer changes.

## Construction Rules

### For non-submit methods

- Successful call:
  - `IsError = false`
  - `IsRpcSuccess = true`
  - `Response = <deserialized response>`

- `RippledException`:
  - `IsError = true`
  - `IsRpcSuccess = false`
  - `ErrorResponse = exception.Response`
  - `Exception = exception`
  - `ErrorInfo = XrplErrorClassifier.Classify(exception)`

- Other exception:
  - `IsError = true`
  - `IsRpcSuccess = false`
  - `Exception = exception`
  - `ErrorInfo = XrplErrorClassifier.Classify(exception)`

### For submit methods

- Successful RPC + `engine_result = tesSUCCESS`
  - `IsError = false`
  - `IsRpcSuccess = true`
  - `IsSubmissionAccepted = true`
  - `IsApplied = response.Applied`
  - `IsQueued = false`

- Successful RPC + `engine_result = terQUEUED`
  - `IsError = false`
  - `IsRpcSuccess = true`
  - `IsSubmissionAccepted = true`
  - `IsQueued = true`

- Successful RPC + any other `engine_result`
  - `IsError = false`
  - `IsRpcSuccess = true`
  - `IsSubmissionAccepted = false`
  - keep `EngineResult*` fields populated for caller-side branching

## Suggested Rollout

1. Add `XrplCallResult<TResponse>` and `XrplSubmissionResult<TResponse>` models.
2. Add safe methods to `IXrplClient` and the concrete client implementation.
3. Implement the wrappers by reusing the current throwing methods.
4. Add unit tests for wrapper construction from:
   - successful RPC responses
   - `RippledException`
   - generic exceptions
   - submit responses with `tesSUCCESS`
   - submit responses with `terQUEUED`
   - submit responses with non-success engine results
5. Optionally evaluate a second-stage refactoring where `RequestManager` can return a richer internal result instead of always rejecting on `status = "error"`.

## Second-Stage Option

If the safe API proves valuable and usage grows, the next step can be an internal protocol result model below `IXrplClient`. That would reduce exception allocation and double-path logic, but it should be deferred until the public contract is validated.

## Expected Benefits

- safer integration code without exception-driven branching for known RPC failures
- normalized access to `ErrorResponse` and `XrplErrorInfo`
- clearer separation between RPC success and transaction acceptance
- incremental adoption with no immediate breaking change
