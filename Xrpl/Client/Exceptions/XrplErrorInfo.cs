using System;
using System.Collections.Generic;

namespace Xrpl.Client.Exceptions;

public sealed class XrplErrorInfo
{
    public string RawError { get; init; } = string.Empty;
    public int? RawErrorCode { get; init; }
    public string? RawErrorMessage { get; init; }

    public XrplErrorCategory Category { get; init; }
    public XrplErrorSubject Subject { get; init; }

    public string Title { get; init; } = string.Empty;
    public string UserMessage { get; init; } = string.Empty;

    public bool IsRetryable { get; init; }
    public bool IsUserFixable { get; init; }

    public string? Command { get; init; }
    public string? FieldName { get; init; }
    public string? FieldValue { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}