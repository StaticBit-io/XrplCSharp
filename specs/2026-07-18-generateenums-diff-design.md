# GenerateEnums v2 — `diff` command (design)

**Issue:** [#42](https://github.com/StaticBit-io/XrplCSharp/issues/42) (part b only; part a — Models-layer enum generation — is a separate later effort)
**Date:** 2026-07-18
**Status:** approved, pending implementation plan

## Motivation

Auditing the SDK's `Base/Xrpl.BinaryCodec/Enums/definitions.json` against a live rippled node is a manual ritual (ad-hoc scripts drove the 10.5.x–10.7.0 protocol-completeness passes). This adds a first-class `diff` command to the existing `Tools/GenerateEnums` console tool: fetch a node's `server_definitions`, compare it against the local `definitions.json`, and report the drift. The pure comparison core is also the intended engine for a future scheduled amendment/protocol monitor.

Non-goals for this effort:
- **Part (a)** of #42 (generating `Xrpl.Models` `TransactionType`/`LedgerEntryType` from `definitions.json`) — separate later effort.
- **Transaction/ledger formats** (`TRANSACTION_FORMATS`/`LEDGER_ENTRY_FORMATS` optionality) — these are NOT part of `server_definitions`; they are macro-derived and already covered by the protocol-watch workflow (#46).
- **Auto-writing `definitions.json`** — `diff` only reads and reports.

## Diffable surface

`definitions.json` and a node's `server_definitions` response share the exact same shape — five sections (the node adds a `hash` field, which is ignored):

| Section | Shape | Count (current) |
|---|---|---|
| `FIELDS` | array of `[name, {type, nth, isSigningField, isSerialized, isVLEncoded, ...}]` | 379 |
| `TYPES` | `name -> code` | 31 |
| `LEDGER_ENTRY_TYPES` | `name -> code` | 32 |
| `TRANSACTION_RESULTS` | `name -> code` (TER codes) | 197 |
| `TRANSACTION_TYPES` | `name -> code` | 83 |

All five are diffed and shown in every output mode.

## Architecture (approach B — small, well-bounded units)

```
Tools/GenerateEnums/
  Program.cs                       — thin command dispatcher: generate (default) / diff
  Generation/
    EnumGenerator.cs               — existing generation logic, moved verbatim (no behavior change)
  Definitions/
    Definitions.cs                 — typed model of the 5 sections + Parse(JsonElement); shared
    ServerDefinitionsClient.cs     — transport-agnostic fetch (ws/http by URL scheme) -> Definitions
    DefinitionsDiff.cs             — pure: Compare(local, server) -> DiffResult; no I/O
    DiffRenderer.cs                — DiffResult -> human table | --json
  README.md                        — updated: two commands
```

Unit responsibilities and dependencies:

- **`Definitions`** — parses JSON (a local file OR a node's `.result`) into one typed form. Depends only on `System.Text.Json`. `FIELDS` is an array of pairs; the other four are flat `name->code` maps.
- **`ServerDefinitionsClient`** — makes one `server_definitions` request to a node, returns the raw JSON handed to `Definitions.Parse`. Depends on `ClientWebSocket`/`HttpClient` (both built into .NET — zero NuGet deps).
- **`DefinitionsDiff`** — compares two `Definitions`, returns a categorized structure. **Zero I/O, zero network** — hence unit-testable. This is the future monitor engine.
- **`DiffRenderer`** — turns a `DiffResult` into a human-readable table or JSON. Formatting only.
- **`Program.cs`** — parses argv, routes to a command, maps a `DiffResult` to an exit code.

The existing generation logic moves from `Program.cs` into `Generation/EnumGenerator.cs` verbatim (a move, not a refactor) so `Program.cs` becomes a clean dispatcher and both commands sit side by side. `generate` behavior is unchanged.

## Data model

```csharp
sealed record Definitions(
    IReadOnlyDictionary<string, FieldDef> Fields,             // FIELDS
    IReadOnlyDictionary<string, int>      Types,              // TYPES
    IReadOnlyDictionary<string, int>      LedgerEntryTypes,   // LEDGER_ENTRY_TYPES
    IReadOnlyDictionary<string, int>      TransactionResults, // TRANSACTION_RESULTS (TER codes)
    IReadOnlyDictionary<string, int>      TransactionTypes);  // TRANSACTION_TYPES

sealed record FieldDef(string Type, int Nth, bool IsSigningField, bool IsSerialized, bool IsVLEncoded);
```

`Definitions.Parse(JsonElement)` accepts either a local-file root or a node response's `.result` (identical shape; a `hash` field, if present, is ignored).

## Diff algorithm

`DefinitionsDiff.Compare(local, server)` → `DiffResult`. For each of the five sections, three categories, keyed by member **name** (name is the stable key; a differing code/nth lands in `Mismatch`):

| Category | Meaning | Counts toward drift (exit code)? |
|---|---|---|
| `NodeOnly` | node has it, local lacks it → SDK behind, sync needed | **yes** |
| `Mismatch` | same name, different `type`/`nth`/flags/`code` → potential bug | **yes** |
| `LocalOnly` | local has it, node lacks it → SDK ahead (normal vs a lagging node) OR a typo | no (informational) |

For `FIELDS`, every differing `FieldDef` property produces its own `Mismatch` row. For the four flat sections, only `code` is compared.

```csharp
sealed record DiffResult(IReadOnlyList<SectionDiff> Sections)
{
    public bool HasDrift => Sections.Any(s => s.NodeOnly.Count > 0 || s.Mismatch.Count > 0);
}
sealed record SectionDiff(
    string Section,
    IReadOnlyList<string>   NodeOnly,
    IReadOnlyList<string>   LocalOnly,
    IReadOnlyList<Mismatch> Mismatch);
sealed record Mismatch(string Name, string Field, string Local, string Server); // e.g. ("Sponsor","nth","31","32")
```

Rationale for the exit-code semantics: querying a release/mainnet node while the local `definitions.json` tracks `develop` legitimately makes local a superset (`LocalOnly` entries for not-yet-active amendments). Treating `LocalOnly` as drift would produce false CI failures against a lagging node, so it is informational only. `NodeOnly` and `Mismatch` are the real "SDK needs updating / something is wrong" signals.

## Transport

`ServerDefinitionsClient.FetchAsync(url, timeout, ct)` picks transport by URL scheme:

- `ws://` / `wss://` → `ClientWebSocket`: connect → send `{"id":1,"command":"server_definitions"}` → read the full message (loop `ReceiveAsync` until `EndOfMessage`, since the large payload is fragmented across frames) → close. Payload in `.result`.
- `http://` / `https://` → `HttpClient` POST `{"method":"server_definitions","params":[{}]}`. Payload in `.result`.

Default timeout 15s (`CancellationTokenSource`), overridable with `--timeout <sec>`. An unknown scheme, a network failure, or a missing `.result` is a clear error → **exit 2** (tool error, distinct from "drift found" = exit 1).

## CLI surface

```
dotnet run --project Tools/GenerateEnums                      # generate (default, unchanged)
dotnet run --project Tools/GenerateEnums -- generate [path] [--force]
dotnet run --project Tools/GenerateEnums -- diff <url> [--json] [--definitions <path>] [--timeout <sec>]
```

- `diff` with no URL → usage + exit 2.
- `--definitions` overrides the local `definitions.json` path (default: the same path `generate` uses).
- Backward compatibility: the bare invocation and `generate` behave exactly as before.

**Exit codes:** `0` = in sync, `1` = drift found (node-ahead / mismatch), `2` = tool error (network / arguments).

## Output

Human table — per section, only non-empty categories:

```
FIELDS
  node-only (SDK behind):   + NewField (Blob, nth 42)
  mismatch:                 ~ Sponsor: nth 31 -> 32
  local-only (info):        - OldField
TRANSACTION_TYPES … (no differences)
Summary: drift in 1/5 sections — 1 node-only, 1 mismatch, 1 local-only.
```

`--json` — emits the `DiffResult` as-is (for the future monitor).

## Tests

New MSTest project `Tests/Xrpl.GenerateEnums.Test/` (matching the repo's other test projects), method names filter-compatible with the unit CI job (`TestUDiff*`):

- `DefinitionsDiff` on synthetic inputs: node-only, local-only, mismatch of each `FieldDef` property, identical (no drift), empty sections.
- `HasDrift`: local-only only → false; node-only / mismatch → true.
- `Definitions.Parse`: a local file and a mocked node `.result` produce the identical typed form.

Transport (network) is not unit-tested. Tests run under the `TestU` filter and join the existing CI unit job.

## Risk / compatibility

- **No NuGet impact** — `Tools` only; the shipping packages are untouched.
- **`generate` unchanged** — its logic is moved verbatim, not modified; existing generated-file output is byte-identical.
- The new test project is added to the solution and the `TestU` filter, so it runs in CI with zero new infrastructure.
