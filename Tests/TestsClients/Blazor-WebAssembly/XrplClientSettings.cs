using Xrpl.Client;

namespace Blazor_WebAssembly;

public class XrplClientSettings
{
    public string ServerUrl { get; set; } = "wss://s2.ripple.com";
    public int ApiVersion { get; set; } = 2;
    public bool UseCustomPing { get; set; } = true;
    public bool UseCheckHealth { get; set; } = true;
    public RequestFailurePolicy RequestPolicy { get; set; } = RequestFailurePolicy.ImmediateFail;
    public int ConnectionAcquisitionTimeoutSeconds { get; set; } = 30;
    public int MaxReconnectAttempts { get; set; } = 4;
    public bool StopAfterMaxAttempts { get; set; } = false;
    public int ReconnectMaxDelaySeconds { get; set; } = 6;
    public int ReconnectBaseDelaySeconds { get; set; } = 3;
    public int RequestTimeoutSeconds { get; set; } = 40;

    public XrplClient.ClientOptions ToClientOptions() => new()
    {
        ApiVersion = ApiVersion,
        UseCustomPing = UseCustomPing,
        UseCheckHealth = UseCheckHealth,
        RequestPolicy = RequestPolicy,
        ConnectionAcquisitionTimeout = TimeSpan.FromSeconds(ConnectionAcquisitionTimeoutSeconds),
        MaxReconnectAttempts = MaxReconnectAttempts,
        StopAfterMaxAttempts = StopAfterMaxAttempts,
        ReconnectMaxDelay = TimeSpan.FromSeconds(ReconnectMaxDelaySeconds),
        ReconnectBaseDelay = TimeSpan.FromSeconds(ReconnectBaseDelaySeconds),
        RequestTimeout = TimeSpan.FromSeconds(RequestTimeoutSeconds),
    };
}
