namespace Xrpl.Client.Exceptions;

public static class XrplErrorCodes
{
    public const string MalformedCurrency = "malformedCurrency";
    public const string ActMalformed = "actMalformed";
    public const string MalformedAddress = "malformedAddress";
    public const string MalformedOwner = "malformedOwner";
    public const string EntryNotFound = "entryNotFound";
    public const string ActNotFound = "actNotFound";
    public const string TxnNotFound = "txnNotFound";
    public const string InvalidParams = "invalidParams";
    public const string MalformedRequest = "malformedRequest";
    public const string JsonInvalid = "jsonInvalid";
    public const string MissingCommand = "missingCommand";
    public const string LedgerNotFound = "lgrNotFound";
    public const string TooBusy = "tooBusy";
    public const string NoCurrent = "noCurrent";
    public const string NoNetwork = "noNetwork";
    public const string NoClosed = "noClosed";
    public const string FailedToForward = "failedToForward";
    public const string UnknownCommand = "unknownCmd";
    public const string DeprecatedFeature = "deprecatedFeature";
    public const string InvalidApiVersion = "invalid_API_version";
    public const string AmendmentBlocked = "amendmentBlocked";
}
