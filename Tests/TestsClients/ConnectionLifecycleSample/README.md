# Connection lifecycle sample

Every way an `XrplClient` connection can end, and what a consumer is supposed to do about each.

Nothing here submits a transaction or needs a funded account — the subject is the connection
itself. The sample runs eight scenarios in order, prints the status stream each one produced, and
then prints the type the caller got and the one sentence that says how to react to it.

## Run it

One node is all it needs. The scenarios that require a server which is **not** answering bind a
loopback port and release it, so they need nothing running.

Against the CI stand (the default, `ws://127.0.0.1:6006`):

```bash
docker compose -f .ci-config/docker-compose.ci.yml up -d
```

```bash
dotnet run --project Tests/TestsClients/ConnectionLifecycleSample
```

Against any other node — pass its URL:

```bash
dotnet run --project Tests/TestsClients/ConnectionLifecycleSample -- wss://s.altnet.rippletest.net:51233
```

The whole run takes under a minute: most of it is the reconnect backoff of the scenarios that are
supposed to fail.

Prefer `127.0.0.1` over `localhost` for a local node. On a dual-stack host `localhost` resolves to
`::1` first while Docker publishes on IPv4 only, so every connection pays a failed IPv6 attempt —
harmless with the default attempt timeout, fatal with the short ones this sample uses to stay
quick.

## Reading the output

Two kinds of line. Indented four spaces is the status stream, exactly as `OnConnectionStatus`
delivers it:

```
    status: RestoringConnection - Reconnecting in 0.4 seconds... (attempt #2)
    status: Disconnected [stopped: ReconnectExhausted] - Reconnection stopped after 2 attempts.
```

`[stopped: ...]` appears only when the client has stopped. A `Disconnected` without it is a
failure the client is about to retry, which is why the first handshake failure against a server
that is down names no reason: naming one would tell a consumer to fail over while this node is
still being dialled.

Indented two spaces is what the caller got:

```
  ReconnectExhaustedException after 2 of 2 attempts
    -> this endpoint is not answering. Fail over to another server.
```

## The scenarios

| Scenario | Ending | What it is for |
|---|---|---|
| A client that was never told to connect | `NotConnectingException` | "Nothing is in progress" is an answer, not an error |
| An endpoint that is down | `ReconnectExhaustedException` | The one ending that justifies failing over. Needs `StopAfterMaxAttempts` |
| A connection that comes up | `ConnectionWaitOutcome.Connected` | The value-returning wait, with no `catch` |
| A request with no connection | `RequestRefusedException` | `RequestFailurePolicy` is a decision, not an accident |
| A switch to a node that is down | `ReconnectExhaustedException`, then recovery | A stopped client stays stopped, and one call brings it back |
| The consumer disconnects | `ClientDisconnectedException` | Never fail over here — the client was asked to be down |
| A broken `OnConnected` handler | `ConnectHandlerFailedException` | The node is fine and this side is broken. The handler's own exception is the `InnerException` |
| A switch a `Disconnect` overtook | `ConnectionSupersededException` | The overtaken operation names the winner instead of a bare cancellation |

## What to take from it

`Report` in `Program.cs` is the part worth copying: one `catch` per ending, each with a different
reaction, and not one line of it reads message text. That is the whole point of the typed
connection outcomes — before them all of these arrived as the same `NotConnectedException`, and
telling them apart meant matching on strings that were free to change.

The same endings are readable three ways, and all three agree:

- as an exception from `WaitForConnectionAsync`, `Connect`, `ChangeServer` or a request;
- as a value from `WaitForConnectionOutcomeAsync`;
- as `ConnectionStatusInfo.StopReason` on the status stream.
