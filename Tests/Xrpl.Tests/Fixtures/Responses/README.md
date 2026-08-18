# Live mainnet response corpus

Six JSON-RPC responses captured from mainnet (`https://xrplcluster.com`), used by
`TestUResponseFidelity` (`Tests/Xrpl.Tests/Models/TestUResponseFidelity.cs`) to guard the
round-trip accuracy this SDK reached at levels 0–3: zero fabricated members, and every
dropped member accounted for by name and reason.

## Why files, not code

Earlier the same check was run by hand with throwaway console projects, each deleted after
the measurement. That made the result — 156 fabricated members on a ten-transaction
`account_tx`, now zero — a one-time fact instead of a standing guarantee. A file corpus
turns the manual diff into something CI runs on every push.

## Provenance

- **Snapshot date:** 2026-08-17
- **Node:** `https://xrplcluster.com`
- **Transport:** HTTP JSON-RPC (`POST /`, `Content-Type: application/json`)
- **`api_version`:** 2 for every request
- **Envelope:** each file is the full HTTP response body — `{"result": {...}}` — exactly as
  the node returned it, with no reformatting or field removal.

This is why `status` sits *inside* `result` in every file here: that is where HTTP JSON-RPC
puts it. The WebSocket envelope `XrplClient` normally talks to carries `status` as a sibling
of `result` instead, surfaced through `XrplResponse<T>.Status` — so a fidelity check driven
by these files must not expect the model to carry `status` at all (see the exceptions table
in `TestUResponseFidelity`).

## Files

| File | Request | Why this response |
|---|---|---|
| `tx_raw.json` | `tx`, hash `E08D6E9754...`, `api_version: 2` | A `Payment` with `DeliverMax`, an issued-currency amount, a multi-path `Paths` array, and metadata with `PreviousFields`/`FinalFields` on both an `AccountRoot` and a `RippleState` — the exact shape the API v1→v2 `Amount`/`DeliverMax` rename (level 3) targets. |
| `tx_binary_raw.json` | Same transaction, `binary: true` | Same transaction as `tx_raw.json`, but rippled's binary-mode envelope: `meta_blob`/`tx_blob` hex strings replace `meta`/`tx_json` entirely. Exercises the sibling-field branch `TransactionSummary` reads separately from the JSON-mode fields. |
| `account_tx_raw.json` | `account_tx`, 10 results, `api_version: 2` | The richest file (36 KB): ten transactions mixing `EscrowCreate`, `EscrowCancel` and `Payment`, with `ModifiedNode`/`CreatedNode`/`DeletedNode` entries touching `AccountRoot`, `RippleState`, `Escrow` and `DirectoryNode`, plus `Memos`, `Condition`, `CancelAfter`/`FinishAfter`. This is the file the original 156-member count was measured against. |
| `account_info_raw.json` | `account_info` | `account_data` (`AccountRoot`) plus the full `account_flags` object — every named account flag in one response. |
| `account_objects_raw.json` | `account_objects` | Ten `RippleState` objects, paginated (`marker` present) and returned with `warning: "load"` — the one other exception besides `status` (see below). |
| `ledger_raw.json` | `ledger` (headers only, no `transactions`/`accounts` expansion) | The ledger header shape (`LOLedger`/`LedgerEntity`) on its own, without transaction or account-state payloads mixed in. |

## Known, accepted gaps

`TestUResponseFidelity` enforces **zero** fabricated (added) members — no exceptions, ever.
Dropped members are checked against an explicit, reasoned exception list inside the test
itself; anything not on that list fails the build. Do not duplicate that list here — it
would drift. Read `KnownLostMembers` in `TestUResponseFidelity.cs` for the current, accurate
set.

## Updating this corpus

Swapping or adding a file here is a deliberate action, not routine refresh — these fixtures
exist to catch a *model* regression, not to track mainnet drift (see
`plans/2026-08-17-raw-response-level4.md`, "Что этот уровень сознательно не делает"). Before
replacing a file:

1. Re-run `TestUResponseFidelity` against the new capture and re-derive the exception list —
   do not carry the old list forward unreviewed.
2. Update the file's row in the table above with what changed and why the new capture is
   needed.
3. Keep `api_version: 2` and the raw, unedited HTTP JSON-RPC envelope unless the very point
   of the new fixture is to test a different envelope shape (state that explicitly if so).
