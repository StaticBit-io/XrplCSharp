# XrplCSharp Connection Guide

This guide explains how to configure and manage WebSocket connections to XRP Ledger nodes using the XrplCSharp library.

## Table of Contents

- [Quick Start](#quick-start)
- [Connection Options](#connection-options)
- [Connection States](#connection-states)
- [Automatic Reconnection](#automatic-reconnection)
- [Request Handling Policies](#request-handling-policies)
- [Event Handling](#event-handling)
- [Error Handling](#error-handling)
- [Usage Examples](#usage-examples)

---

## Quick Start

```csharp
using Xrpl.Client;

// Create client with default settings
var client = new XrplClient("wss://s.altnet.rippletest.net:51233");

// Connect
await client.Connect();

// Make requests
var response = await client.Request(new AccountInfoRequest { Account = "rAddress..." });

// Disconnect when done
await client.Disconnect();
```

---

## Connection Options

Configure connection behavior by passing `ClientOptions` when creating the client:

```csharp
var client = new XrplClient("wss://s.altnet.rippletest.net:51233", new XrplClient.ClientOptions
{
    RequestTimeout = TimeSpan.FromSeconds(30),
    MaxReconnectAttempts = 10,
    StopAfterMaxAttempts = false
});
```

### Available Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `RequestTimeout` | TimeSpan | 20 seconds | Timeout for individual API requests after connection is established |
| `ConnectionAttemptTimeout` | TimeSpan | 20 seconds | Timeout for a single WebSocket connection attempt |
| `ReconnectBaseDelay` | TimeSpan | 2 seconds | Base delay between automatic reconnection attempts |
| `ReconnectMaxDelay` | TimeSpan | 30 seconds | Maximum delay between reconnection attempts (exponential backoff cap) |
| `MaxReconnectAttempts` | int | 5 | Maximum number of reconnection attempts after disconnection |
| `StopAfterMaxAttempts` | bool | true | Whether to stop reconnecting after reaching max attempts |
| `UseCustomPing` | bool | true | Enable custom ping/pong heartbeat to detect connection issues |
| `RequestPolicy` | RequestFailurePolicy | WaitForConnection | How to handle requests when disconnected |
| `ConnectionAcquisitionTimeout` | TimeSpan | 5 minutes | Maximum time to wait for connection when using WaitForConnection policy |

---

## Connection States

The connection can be in one of four states, accessible via `client.connection.CurrentConnectionState`:

| State | Description |
|-------|-------------|
| `Disconnected` | Not connected. Initial state, after user disconnect, or after max retry attempts exceeded |
| `Connecting` | Establishing initial connection |
| `Connected` | Successfully connected and ready for requests |
| `RestoringConnection` | Attempting to restore connection after unexpected disconnection |

### State Diagram

```
                    ┌─────────────────┐
                    │  Disconnected   │ (initial)
                    └────────┬────────┘
                             │ Connect()
                             ▼
                    ┌─────────────────┐
                    │   Connecting    │
                    └────────┬────────┘
                             │ success
                             ▼
                    ┌─────────────────┐
         ┌─────────│    Connected    │◄────────┐
         │         └────────┬────────┘         │
         │                  │ connection lost  │ success
         │                  ▼                  │
         │         ┌─────────────────┐         │
         │         │RestoringConnection│───────┘
         │         └────────┬────────┘
         │                  │ max attempts or user Disconnect()
         │                  ▼
         │         ┌─────────────────┐
         └────────►│  Disconnected   │
   user Disconnect()└────────────────┘
```

---

## Automatic Reconnection

When the connection is unexpectedly lost (server restart, network issues), the client automatically attempts to reconnect.

### Backoff Algorithm

The delay between reconnection attempts uses exponential backoff with jitter:

```
delay = min(ReconnectBaseDelay * 2^(attempt-1), ReconnectMaxDelay) + random_jitter
```

Example with default settings:
- Attempt 1: ~2 seconds
- Attempt 2: ~4 seconds
- Attempt 3: ~8 seconds
- Attempt 4: ~16 seconds
- Attempt 5: ~30 seconds (capped at max)

### Reconnection Behavior

| Scenario | Behavior |
|----------|----------|
| Server closes connection | Auto-reconnect starts |
| Network timeout detected | Auto-reconnect starts |
| User calls `Disconnect()` | No auto-reconnect, state becomes `Disconnected` |
| Max attempts exceeded (`StopAfterMaxAttempts = true`) | Stops reconnecting, state becomes `Disconnected` |
| Max attempts exceeded (`StopAfterMaxAttempts = false`) | Continues trying with warning messages |

### Manual Reconnection

After max attempts are exhausted (with `StopAfterMaxAttempts = true`), you can manually reconnect:

```csharp
// After connection permanently failed
await client.Connect();
```

This resets the attempt counter and starts fresh.

### Fast Reconnect

For certain scenarios, the library uses **fast reconnect** (3-5 seconds) instead of exponential backoff:

| Scenario | Behavior | Time |
|----------|----------|------|
| `ChangeServer()` called | Immediate switch to new server | 3-5 seconds |
| Ping timeout (no pong for 15s) | Immediate reconnect to same server | 3-5 seconds |
| Network drop (IOException, SocketException) | Immediate reconnect when network restored | 3-5 seconds |

Fast reconnect differs from standard reconnect:
- **No exponential backoff** - connects immediately
- **Session isolation** - old session is retired, new session created
- **Pending requests cancelled** - avoids stale responses from old connection
- **ReconnectInfo available** - `CurrentAttempt = 1` during fast reconnect

```csharp
client.connection.OnConnectionStatus += (status) =>
{
    if (status.ConnectionState == XrpConnectionState.RestoringConnection)
    {
        // ReconnectInfo is always available during reconnect (including fast reconnect)
        Console.WriteLine($"Reconnecting: attempt {status.Reconnect?.CurrentAttempt}");
    }
};
```

---

## MAUI and Mobile Considerations

When using XrplCSharp in MAUI or mobile applications, the library provides special handling for mobile network conditions.

### No Critical Error Logging

The library suppresses Critical-level logging for common mobile network exceptions:
- `ObjectDisposedException` - socket disposed during reconnect
- `IOException` - network I/O errors
- `SocketException` - low-level socket failures
- `TaskCanceledException` - operations cancelled during disconnect
- DNS failures on iOS (e.g., "nodename nor servname provided")

This prevents your app's global exception handlers from being flooded with expected network events.

### Automatic Network Recovery

When network connectivity is restored (e.g., switching from WiFi to cellular), the library:
1. Detects the network drop via ping timeout or socket exception
2. Initiates fast reconnect (not slow exponential backoff)
3. Reconnects within 3-5 seconds
4. Emits `RestoringConnection` → `Connected` status updates

### iOS-Specific Handling

The library recognizes iOS-specific network errors:
- DNS resolution failures with HRESULT `0xFFFDFFFF`
- Generic failures with HRESULT `0x80004005` (E_FAIL)
- Exception message patterns like "nodename nor servname"

These are treated as recoverable network drops, not critical errors.

### Best Practices for Mobile

```csharp
var client = new XrplClient(url, new XrplClient.ClientOptions
{
    // Mobile networks are unreliable - be patient
    MaxReconnectAttempts = 50,
    StopAfterMaxAttempts = false,
    
    // Wait for connection - mobile may temporarily lose connectivity
    RequestPolicy = RequestFailurePolicy.WaitForConnection,
    ConnectionAcquisitionTimeout = TimeSpan.FromMinutes(5),
    
    // Keep ping enabled for proactive failure detection
    UseCustomPing = true
});

// Monitor connection for UI updates
client.connection.OnConnectionStatus += (status) =>
{
    MainThread.BeginInvokeOnMainThread(() =>
    {
        UpdateConnectionIndicator(status.ConnectionState);
    });
};
```

---

## Request Handling Policies

The `RequestPolicy` option controls how requests behave when the connection is not available.

### ImmediateFail

Requests immediately throw `NotConnectedException` if not connected:

```csharp
var client = new XrplClient(url, new XrplClient.ClientOptions
{
    RequestPolicy = RequestFailurePolicy.ImmediateFail
});

try
{
    var response = await client.Request(...);
}
catch (NotConnectedException)
{
    // Handle disconnection
}
```

**Use case:** When you need immediate feedback and handle retries yourself.

### WaitForConnection (Default)

Requests wait for the connection to be established (up to `ConnectionAcquisitionTimeout`):

```csharp
var client = new XrplClient(url, new XrplClient.ClientOptions
{
    RequestPolicy = RequestFailurePolicy.WaitForConnection,
    ConnectionAcquisitionTimeout = TimeSpan.FromMinutes(2)
});

// This will wait for connection if disconnected
var response = await client.Request(...);
```

**Use case:** For 24/7 bots and services that should survive temporary network issues.

---

## Event Handling

### Connection Status Events

Subscribe to `OnConnectionStatus` to receive real-time connection state updates:

```csharp
client.connection.OnConnectionStatus += (status) =>
{
    Console.WriteLine($"State: {status.ConnectionState}");
    Console.WriteLine($"Message: {status.Message}");
    Console.WriteLine($"Severity: {status.Severity}");
    
    if (status.Reconnect != null)
    {
        Console.WriteLine($"Attempt: {status.Reconnect.CurrentAttempt}/{status.Reconnect.MaxAttempts}");
        Console.WriteLine($"Next retry in: {status.Reconnect.RemainingDelay.TotalSeconds}s");
    }
};
```

### ConnectionStatusInfo Properties

| Property | Type | Description |
|----------|------|-------------|
| `ConnectionState` | XrpConnectionState | Current connection state |
| `Message` | string | Human-readable status message |
| `Severity` | ConnectionCloseSeverity | Info, Warning, or Error |
| `Reconnect` | ReconnectInfo? | Reconnection details (null if not reconnecting) |

### ReconnectInfo Properties

| Property | Type | Description |
|----------|------|-------------|
| `CurrentAttempt` | int | Current reconnection attempt number |
| `MaxAttempts` | int | Maximum configured attempts |
| `RemainingDelay` | TimeSpan | Time until next reconnection attempt |

### Other Events

```csharp
// Called when connection is established
client.connection.OnConnected += () => { ... };

// Called when connection is lost
client.connection.OnDisconnect += (code, reason) => { ... };

// Called on errors
client.connection.OnError += (errorCode, errorMessage, message, error) => { ... };
```

---

## Error Handling

### Common Exceptions

| Exception | When Thrown |
|-----------|-------------|
| `NotConnectedException` | Request made while disconnected (with ImmediateFail policy) |
| `TimeoutException` | Request timeout exceeded (`RequestTimeout`) |
| `DisconnectedException` | Connection lost while request was pending |
| `XrplException` | XRPL protocol errors |

### Handling Disconnections

```csharp
client.connection.OnConnectionStatus += (status) =>
{
    switch (status.ConnectionState)
    {
        case XrpConnectionState.Disconnected:
            if (status.Severity == ConnectionCloseSeverity.Error)
            {
                // Permanent failure - may need manual intervention
                LogError(status.Message);
            }
            break;
            
        case XrpConnectionState.RestoringConnection:
            // Auto-reconnect in progress
            LogInfo($"Reconnecting... attempt {status.Reconnect?.CurrentAttempt}");
            break;
    }
};
```

---

## Usage Examples

### Basic Connection with Status Monitoring

```csharp
var client = new XrplClient("wss://s.altnet.rippletest.net:51233");

client.connection.OnConnectionStatus += (status) =>
{
    Console.WriteLine($"[{status.ConnectionState}] {status.Message}");
};

await client.Connect();
// ... use client ...
await client.Disconnect();
```

### 24/7 Bot Configuration

```csharp
var client = new XrplClient("wss://s.altnet.rippletest.net:51233", new XrplClient.ClientOptions
{
    // Never give up reconnecting
    MaxReconnectAttempts = 100,
    StopAfterMaxAttempts = false,
    
    // Wait for connection on requests
    RequestPolicy = RequestFailurePolicy.WaitForConnection,
    ConnectionAcquisitionTimeout = TimeSpan.FromMinutes(10),
    
    // Longer timeouts for stability
    RequestTimeout = TimeSpan.FromSeconds(60),
    ConnectionAttemptTimeout = TimeSpan.FromSeconds(30)
});
```

### Fail-Fast Configuration

```csharp
var client = new XrplClient("wss://s.altnet.rippletest.net:51233", new XrplClient.ClientOptions
{
    // Fail immediately if not connected
    RequestPolicy = RequestFailurePolicy.ImmediateFail,
    
    // Limited retry attempts
    MaxReconnectAttempts = 3,
    StopAfterMaxAttempts = true,
    
    // Quick timeouts
    RequestTimeout = TimeSpan.FromSeconds(10),
    ConnectionAttemptTimeout = TimeSpan.FromSeconds(5)
});
```

### Switching Servers

```csharp
// Switch to a different server (disconnects and reconnects)
await client.connection.ChangeServer("wss://s1.ripple.com:443");
```

---

## Best Practices

1. **Always subscribe to `OnConnectionStatus`** to monitor connection health
2. **Use `WaitForConnection` policy** for services that need resilience
3. **Set `StopAfterMaxAttempts = false`** for 24/7 applications
4. **Handle `Disconnected` state** in your UI to show connection status
5. **Don't call `Connect()` repeatedly** - it's safe but unnecessary (idempotent)
6. **Use `CancellationToken`** for graceful shutdown
