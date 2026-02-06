# XrplCSharp Connection Guide

This guide explains how to configure and manage WebSocket connections to XRP Ledger nodes using the XrplCSharp library.

## Table of Contents

- [Quick Start](#quick-start)
- [Connection Options](#connection-options)
- [Connection States](#connection-states)
- [Automatic Reconnection](#automatic-reconnection)
- [Keepalive and Connection Health](#keepalive-and-connection-health)
- [MAUI and Mobile Considerations](#maui-and-mobile-considerations)
- [WebAssembly / Blazor Considerations](#webassembly--blazor-considerations)
- [Stream Subscriptions](#stream-subscriptions)
- [Request Handling Policies](#request-handling-policies)
- [Event Handling](#event-handling)
- [Error Handling](#error-handling)
- [Usage Examples](#usage-examples)
- [Best Practices](#best-practices)

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
| `RequestTimeout` | TimeSpan | 40 seconds | Timeout for individual API requests after connection is established |
| `ConnectionAttemptTimeout` | TimeSpan | 20 seconds | Timeout for a single WebSocket connection attempt |
| `ReconnectBaseDelay` | TimeSpan | 2 seconds | Base delay between automatic reconnection attempts |
| `ReconnectMaxDelay` | TimeSpan | 30 seconds | Maximum delay between reconnection attempts (exponential backoff cap) |
| `MaxReconnectAttempts` | int | 5 | Maximum number of reconnection attempts after disconnection |
| `StopAfterMaxAttempts` | bool | true | Whether to stop reconnecting after reaching max attempts |
| `UseCustomPing` | bool | true | Enable custom ping/pong heartbeat to detect connection issues |
| `UseCheckHealth` | bool | false | Lightweight background health check every 20 seconds. Only inspects local WebSocket state via `IsConnected()` — no network requests are sent. If the WebSocket is not connected (Closed/Aborted), automatic reconnection is triggered. Automatically enabled when `UseCustomPing` is `true`. Can be enabled independently for fast disconnect detection without keepalive overhead |
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

## Keepalive and Connection Health

XRPL servers (s1/s2.ripple.com) require application-level activity and will close connections after approximately 60-80 seconds of client silence, regardless of transport-level WebSocket keepalive (RFC 6455 ping/pong frames). The library provides two complementary mechanisms to maintain connection stability.

### UseCustomPing

When enabled (default: `true`), the library sends application-level ping commands every 20 seconds:

- **Active connection** (data received within last 30 seconds): Sends a fire-and-forget `{"command":"ping"}` directly to the WebSocket without awaiting a response. This keeps the server-side connection alive without blocking the thread.
- **Idle connection** (no data for 30+ seconds): Sends a full request-response ping with a 45-second timeout. If no response is received, reconnection is triggered.
- **Inactivity timeout**: If no activity is detected for 60+ seconds, the connection is considered dead and reconnection is triggered.

Enabling `UseCustomPing` automatically enables `UseCheckHealth`.

### UseCheckHealth

When enabled (default: `false`, auto-enabled with `UseCustomPing`), a lightweight background check runs every 20 seconds:

- Inspects local WebSocket state via `IsConnected()` (checks if State == Open)
- If the WebSocket is not connected (Closed, Aborted, etc.), automatic reconnection is triggered immediately
- **No network requests are sent** — this is purely a local state inspection
- The 60-second inactivity timeout only applies when `UseCustomPing` is also enabled

### Recommended Configurations

| Scenario | UseCustomPing | UseCheckHealth | Behavior |
|----------|--------------|----------------|----------|
| Full protection (default) | `true` | auto-enabled | Keepalive pings + disconnect detection + inactivity timeout |
| Disconnect detection only | `false` | `true` | Fast detection of WebSocket state changes, no keepalive overhead |
| No monitoring | `false` | `false` | No health checks. Connection may silently die after ~60-80 seconds of inactivity |

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

## WebAssembly / Blazor Considerations

When using XrplCSharp in Blazor WebAssembly applications, the library adapts to the single-threaded browser environment.

### Single-Threaded Environment

WebAssembly runs on the browser's main thread. Unlike Desktop and MAUI:
- `Task.Run()` does not create real background threads — all work executes on the same thread
- `Channel<T>` does not provide true concurrent processing
- All async operations are cooperative — they yield control via `await`

### No RFC 6455 Keepalive

Browser WebSocket API does not support transport-level keepalive (RFC 6455 ping/pong frames). This means:
- `KeepAliveInterval` has no effect in WebAssembly
- Application-level keepalive via `UseCustomPing` is the only way to prevent server-side timeout
- Without `UseCustomPing`, XRPL servers will close the connection after ~60-80 seconds of silence

### Stream Message Processing

Stream messages (transactions, ledger events) are processed using fire-and-forget pattern:
- `ProcessStreamMessageFireAndForgetAsync()` is used instead of `Channel<T>`
- The receive loop is not blocked — stream processing is scheduled via `ConfigureAwait(false)`
- Response messages (with `"id"` property) are always prioritized over stream messages via `IsLikelyResponse()` fast-path string scanning

### ReceiveAsync Behavior

In WebAssembly, `ReceiveAsync` uses only the general cancellation token without an artificial timeout. Connection health is monitored by `UseCheckHealth` which checks WebSocket state every 20 seconds. If the WebSocket transitions to Closed or Aborted state, reconnection is triggered automatically.

### Best Practices for Blazor WebAssembly

```csharp
var client = new XrplClient(url, new XrplClient.ClientOptions
{
    // Required for stable connections — prevents server-side timeout
    UseCustomPing = true,
    
    // Recommended for Blazor — fail fast and handle in UI
    RequestPolicy = RequestFailurePolicy.ImmediateFail,
    
    // Reasonable reconnect attempts
    MaxReconnectAttempts = 10,
    StopAfterMaxAttempts = false
});
```

---

## Stream Subscriptions

The library supports real-time subscription to XRPL streams (transactions, ledger events) via `client.Subscribe()`.

### Basic Usage

```csharp
// Subscribe to transaction and ledger streams
var subscribeRequest = new SubscribeRequest
{
    Streams = new List<string> { "transactions", "ledger" }
};

client.connection.OnTransaction += (tx) =>
{
    Console.WriteLine($"Transaction: {tx.Transaction.TransactionType}");
};

client.connection.OnLedgerClosed += (ledger) =>
{
    Console.WriteLine($"Ledger: {ledger.LedgerIndex}");
};

await client.Subscribe(subscribeRequest);

// Unsubscribe when done
await client.Unsubscribe(new UnsubscribeRequest
{
    Streams = new List<string> { "transactions", "ledger" }
});
```

### High-Volume Stream Processing

On mainnet (s1/s2.ripple.com), transaction streams produce 200+ messages per second. The library uses fast-path message processing to handle this:

1. **`IsLikelyResponse()`** scans each message for the `"id"` property using pure string scanning (no JSON parsing)
2. **Response messages** (with `"id"`) are processed immediately via `requestManager.HandleResponse()`
3. **Stream messages** (without `"id"`) are processed asynchronously — via `Channel<T>` on Desktop/MAUI, or fire-and-forget on WebAssembly

### Known Limitation: Request Timeouts Under Heavy Stream Load

When subscribed to high-volume streams on mainnet, request-response commands (`server_info`, `account_info`, `unsubscribe`, etc.) may experience timeouts. This occurs because:

1. The XRPL server buffers outgoing stream messages
2. The response to your request is queued behind hundreds of stream messages in the server's send buffer
3. Even though the client-side fast-path prioritizes responses, the response cannot physically arrive at the client until the server sends it through the buffer
4. The default `RequestTimeout` (40 seconds) may expire before the response is delivered

**Workarounds:**
- Increase `RequestTimeout` for operations during active subscriptions
- Use `ImmediateFail` policy and implement retry logic with longer timeouts
- Consider using separate client instances for subscriptions and request-response operations

> **Note:** A future version will implement a Response-Seeking Drain mode or dual-WebSocket architecture to fully resolve this limitation.

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
