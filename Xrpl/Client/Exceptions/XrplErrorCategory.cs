namespace Xrpl.Client.Exceptions;

public enum XrplErrorCategory
{
    Unknown,
    InvalidInput,
    NotFound,
    BadRequest,
    LedgerUnavailable,
    TemporaryServerProblem,
    UnsupportedRequest,
    ServerState,
}