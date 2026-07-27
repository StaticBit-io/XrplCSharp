# definitions.json vs develop drift monitor (design)

**Date:** 2026-07-19
**Status:** approved, pending implementation plan
**Related:** the `diff` command (issue #42 part b, PR #57), protocol-watch (#46), the Models drift test (#42 part a, PR #58)

## Motivation

The `diff` command compares the SDK's local `Base/Xrpl.BinaryCodec/Enums/definitions.json` against a live node's `server_definitions`. It reports facts correctly, but its `node-only` category can't know the *direction* of drift: a field the node has and the SDK lacks is either a genuinely new upstream field (SDK behind) or a node running an older build (node behind). Resolving that requires comparing against rippled `develop` — the canonical bleeding-edge protocol.

A concrete case surfaced this: `diff` against devnet reported `ImmutableFlags` as `node-only`, which looked like "SDK behind." Cross-checking rippled `develop` showed the opposite — `develop` uses `MutableFlags` (which the SDK already has), and devnet was running an older build still calling it `ImmutableFlags`. The SDK was correct; the queried node was stale.

There is no public `develop` node (devnet/testnet/mainnet all lag). The repo already builds a `develop` node — the nightly stand (`.ci-config/docker-compose.batchv11.yml` via `Dockerfile.nightly`, a pinned xrpld `develop` build). This design adds a **weekly scheduled monitor** that runs `diff` against that develop node, so drift *against develop* is caught automatically and correctly-directed, and softens the misleading `node-only` label.

This completes the protocol-sync loop:

- **protocol-watch (#46)** — did develop's `.macro` files move? (weekly, gh-api only)
- **this monitor** — does our `definitions.json` match a develop node's `server_definitions`? (weekly, builds the develop stand)
- **Models drift test (#58)** — do the Models enums match `definitions.json`? (every unit CI run)

## Scope

Two independent, small changes:

1. A new CI workflow `.github/workflows/definitions-watch.yml` — weekly (+ manual) monitor.
2. A one-line label softening in `Tools/GenerateEnums/Definitions/DiffRenderer.cs` + its pin test.

Non-goals: teaching the tool to parse develop's `.macro` files (that reimplements `xrpl-codec-gen`; the whole reason `definitions.json` exists as a cached artifact — deliberately avoided). No per-PR gate (building the develop image is too heavy for every PR). No auto-sync of `definitions.json` (the monitor reports; a human syncs after cross-checking develop). No `Xrpl` package change.

## Component 1 — the monitor workflow

`.github/workflows/definitions-watch.yml`:

- **Triggers:** `schedule` (weekly cron **Monday 07:00 UTC** — one hour after protocol-watch's Monday 06:00 UTC, so the two develop checks don't contend) and `workflow_dispatch` (manual, e.g. after protocol-watch fires).
- **permissions:** `contents: read` (fail-workflow reporting mode — no issue/PR writes).
- **concurrency:** group `definitions-watch`, `cancel-in-progress: false`.
- **Job** (`ubuntu-latest`):
  1. `actions/checkout@v4`.
  2. Start the develop stand: `docker compose -f .ci-config/docker-compose.batchv11.yml up -d --build`. This builds the pinned xrpld `develop` image (`Dockerfile.nightly`) — the heavy step, run weekly.
  3. Wait for readiness: poll `curl -sf http://localhost:5005/ -d '{"method":"server_info"}'` until `complete_ledgers` is present and not `empty` (up to ~30 attempts × 4s), mirroring the existing integration job's wait; on timeout, dump `docker compose ... ps` / `logs` and fail.
  4. `actions/setup-dotnet@v4` (repo `DOTNET_VERSION`).
  5. Run the check: `dotnet run --project Tools/GenerateEnums -- diff http://localhost:5005`.
  6. `docker compose -f .ci-config/docker-compose.batchv11.yml down` under `if: always()`.

**Exit-code gating** (relies on the `diff` command's contract, no extra handling): `0` in sync → green; `1` node-only/mismatch → red (against a develop node this genuinely means "SDK behind develop"); `2` tool error (stand didn't come up, bad response) → red (the check could not run). The `diff` table is printed to the step log, so the failure cause is visible.

**Transport:** HTTP JSON-RPC `http://localhost:5005` (one stateless POST, no WebSocket handshake — fewer flake surfaces). The batchv11 stand exposes 5005 on loopback.

**Known limitation** (documented in the workflow header): "develop" here is the *pinned* xrpld version in `Dockerfile.nightly`, not literally today's develop tip. That is acceptable — the pinned nightly is far ahead of any public network, and "did the develop tip move" is separately covered by protocol-watch's macro diff. When the pinned version is bumped, the monitor automatically checks against the new one.

## Component 2 — label softening

In `Tools/GenerateEnums/Definitions/DiffRenderer.RenderTable`, the `node-only` line changes from a direction-asserting label to a neutral one:

```csharp
// before
sb.AppendLine($"  node-only (SDK behind):   + {n}");
// after
sb.AppendLine($"  node-only (on node, not local):   + {n}");
```

`mismatch` and `local-only (info)` lines are unchanged (they do not assert a direction). Rationale: `node-only` is the fact "present on the node, absent locally"; which side is *behind* depends on which node was queried (a develop node → SDK behind; a lagging network like devnet → node behind). The neutral label stops the tool from asserting a wrong direction on lagging nodes, while the monitor — which queries a develop node — still reads unambiguously.

The existing pin test `TestURenderTable_ShowsCategoriesAndSummary` (in `Tests/Xrpl.GenerateEnums.Test/TestUDiffRenderer.cs`) asserts the table contains the `node-only` entry; update its expected substring to the new label. Do not change the `NewField`/`Summary` assertions.

## Verification

- **Label:** the `TestU` GenerateEnums unit tests pass after the pin update; an ad-hoc `diff` against devnet prints the new neutral label.
- **Workflow:** YAML validity (`yaml.safe_load`); a one-time manual `workflow_dispatch` run after merge — it builds the stand, runs `diff` (exit 0/1/2 per the actual state), and tears the stand down. Described as a plan step, not part of the weekly schedule verification.

## Risk / compatibility

- **CI + one label line only.** No `Xrpl` package or shipping-behavior change; no NuGet impact; `generate` untouched.
- Weekly cost is one nightly-image build + short run; `if: always()` teardown prevents leaked containers.
- Failure mode is a red scheduled run with the diff table in the log (GitHub emails watchers) — the intended develop-drift signal.
