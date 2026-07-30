# Standalone Node Guide

How to run a local `rippled`/`xrpld` node in standalone mode for development and integration testing — and why a freshly started node answers `temDISABLED` to almost everything until you activate amendments.

For a production node connected to the live network see the [Mainnet Node Guide](MainnetNode-Guide.md).

> **TL;DR**: `rippled -a --start` alone is **not enough**. A bare standalone node starts with (almost) no amendments enabled, so `AMMCreate`, `NFTokenMint`, `MPTokenIssuanceCreate` and most modern transaction types fail with `temDISABLED`. Use the ready-made Docker stands from `.ci-config/` in this repository, or configure amendment activation yourself as described below.

## Quick start (recommended)

The repository ships two ready-to-use Docker Compose stands. Both include a `ledger-acceptor` sidecar that closes a ledger every 4 seconds.

### Stand 1: release node (rippled 3.2.0) — used by CI

```bash
docker compose -f .ci-config/docker-compose.ci.yml up -d

# run integration tests
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestI"

docker compose -f .ci-config/docker-compose.ci.yml down
```

### Stand 2: nightly develop node — for unreleased amendments

Amendments that exist only on the rippled `develop` branch (e.g. `BatchV1_1`, `PermissionDelegationV1_1`) are absent from release images. The second stand builds a pinned nightly `xrpld` and enables them at genesis:

```bash
# both stands use the same ports — stop the first one before starting this one
docker compose -f .ci-config/docker-compose.ci.yml down

docker compose -f .ci-config/docker-compose.batchv11.yml up -d --build
dotnet test Tests/Xrpl.Tests/Xrpl.Tests.csproj --settings test.runsettings --filter "TestIBatch|TestIDelegateSet"
docker compose -f .ci-config/docker-compose.batchv11.yml down
```

### Ports (both stands)

| Port | Purpose |
|------|---------|
| 5005 | JSON-RPC (admin) |
| 5006 | JSON-RPC (admin, used by the ledger-acceptor) |
| 6006 | WebSocket (admin) — integration tests connect here (`ws://localhost:6006`) |
| 6007 | WebSocket with `admin_user`/`admin_password` — used only by `TestIAdminCredentials` |

All ports are published on **loopback only** (`127.0.0.1`): the stand is reachable from the same machine and not from the network. That is deliberate — every stanza in the config sets `admin = 0.0.0.0`, so each port hands the admin role (`stop`, `connect`, `feature`, `validation_seed`) to anyone who can reach it, and only 6007 asks for credentials. If you genuinely need to reach the node from another host, widen the binding in the compose file as a conscious decision, not to make something work.

Port 6007 (`[port_ws_admin_auth]`) exists to prove that admin commands over WS open up only when `ClientOptions.AdminUser`/`AdminPassword` are set. rippled carries those credentials **inside the request JSON** — it never checks a Basic header on the ws handshake (its `user`/`password` port settings apply to plain HTTP JSON-RPC only). No other test uses this port.

## Why you get `temDISABLED`

Every feature gated by an [amendment](https://xrpl.org/amendments.html) (AMM, NFTs, MPTs, Batch, …) checks at transaction level whether the amendment is **enabled in the ledger**. On mainnet amendments are enabled by validator voting; in standalone mode **there is no voting** — no validators, no majorities, so nothing ever activates on its own.

A bare `rippled -a --start` creates a genesis ledger where only amendments with `VoteBehavior::DefaultYes` (a handful of fixes) are active. Everything else is inactive, and the node answers:

```json
{ "engine_result": "temDISABLED", "engine_result_message": "The transaction requires logic that is currently disabled." }
```

This is not a bug in your transaction — the node simply does not have the feature turned on.

### How to check what is active

```bash
# status of one amendment (admin RPC)
curl -s -X POST http://localhost:5005/ -d '{"method":"feature","params":[{"feature":"AMM"}]}'
# => { "...": { "enabled": true|false, "supported": true, ... } }

# full list of enabled amendments straight from the ledger
curl -s -X POST http://localhost:5005/ -d '{"method":"ledger_entry","params":[{"index":"7DB0788C020F02780A673DC74757F23823FA3014C1866E72CC4CD8B226CD6EF4","ledger_index":"validated"}]}'
```

`7DB0788C…D6EF4` is the well-known index of the `Amendments` ledger object. If your amendment's id is not in its `Amendments` array — you will get `temDISABLED`.

An amendment id is `sha512half` of its name:

```python
import hashlib
hashlib.sha512(b"AMM").digest()[:32].hex().upper()
# 8CC0774A3BF66D1D22E76BBDA8E8A232E6B6313834301B3B23E8601196AE6455
```

## Activating amendments at genesis

The config section that does this **depends on the rippled version**:

### rippled ≤ 3.2.x (release images): `[features]` section

Plain amendment names, one per line. See `.ci-config/rippled.cfg` for a full working set aligned with 3.2.0:

```ini
[features]
AMM
Clawback
MPTokensV1
NFTokenMintOffer
...
```

Started with `rippled -a --start`, the node enables everything listed at genesis.

**Pitfalls:**
- An **unknown name** (a typo, or an amendment this binary doesn't have) makes the node crash at startup with `Unknown feature: X in config file`.
- **Retired amendments** (pre-2.0 ones like `MultiSign`, `Escrow`, `DepositAuth`, `fix1368`, …) are permanently baked into the protocol and **must not be listed** — newer binaries reject them as unknown.

### rippled `develop` / 3.3.x (nightly `xrpld`): `[amendments]` section

On the `develop` branch the `[features]` config section **no longer registers genesis up-votes**. Genesis amendments come from the `[amendments]` section with lines of the form `<hash> <name>`:

```ini
[amendments]
8CC0774A3BF66D1D22E76BBDA8E8A232E6B6313834301B3B23E8601196AE6455 AMM
9F287AED3CDB50A7BD1ACEC24296A30C9B5230CCD136219317AC790E3B884377 BatchV1_1
...
```

See `.ci-config/rippled.batchv11.cfg` for a complete example. Note that the `feature` admin RPC cannot activate an amendment on a running standalone node either — with `VoteBehavior::DefaultNo` an un-vetoed amendment still votes "no", and there is no voting anyway. **The `[amendments]` section at `--start` is the only way.**

### Nightly image specifics

- On `develop`, rippled was renamed to **`xrpld`**; the nightly apt channel at `repos.ripple.com` publishes the `xrpld` package.
- **Pin the version** (`ARG XRPLD_VERSION` in `.ci-config/Dockerfile.nightly`): the build-timestamp format shrank from 14 to 12 digits in mid-2026, so Debian version ordering ranks older builds above newer ones and an unpinned `apt install xrpld` fetches a stale build.

## Advancing the ledger

In standalone mode ledgers do not close by themselves. Every transaction stays in the open ledger until you call:

```bash
curl -s -X POST http://localhost:5006/ -d '{"method":"ledger_accept"}'
```

The compose stands run a `ledger-acceptor` sidecar doing this every 4 seconds, so `SubmitAndWait` works out of the box. If you run a node manually and forget this, transactions will sit in `terQUEUED`/pending state forever.

## Funding accounts

There is no faucet in standalone mode. The genesis account holds all 100 billion XRP:

| | |
|---|---|
| Address | `rHb9CJAWyB4rj91VRWn96DkukG4bwdtyTh` |
| Seed | `snoPBrXtMeMyMHUVTgbuqAfg1SUTb` (the well-known `masterpassphrase` seed) |

Fund test wallets with a regular `Payment` from it — see `Tests/Xrpl.Tests/Integration/Utils.cs` (`FundAccount`) for a reference implementation with retries.

```csharp
XrplWallet master = XrplWallet.FromSeed("snoPBrXtMeMyMHUVTgbuqAfg1SUTb", null, "secp256k1");
```

Note the genesis key is **secp256k1**, not Ed25519.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `temDISABLED` on AMM/NFT/MPT/Batch/… | Amendment not enabled on the node | Activate at genesis: `[features]` (≤3.2.x) or `[amendments]` (develop); use the `.ci-config` stands |
| Node crashes: `Unknown feature: X in config file` | Name typo, amendment missing from this binary, or a retired amendment listed | Fix the name; match the list to the binary version; remove retired amendments |
| Node crashes: `Invalid entry 'X' in [amendments]` | `[amendments]` lines must be `<hash> <name>`, not bare names | Prefix each name with its sha512half hash |
| `actNotFound` / `terNO_ACCOUNT` | Test account never funded | Send a Payment from the genesis account first |
| Transaction stuck, `SubmitAndWait` times out | No one calls `ledger_accept` | Run the ledger-acceptor sidecar (included in the compose stands) |
| `feature` RPC shows `"vetoed": false` but `"enabled": false` forever | Un-vetoing is not voting; standalone has no voting at all | Amendments activate only via the genesis config sections |
| Amendment-gated integration tests are skipped (inconclusive) | `AmendmentGuard` detected the amendment is inactive on this node | Expected on the release stand; run the nightly stand for those tests |

## How the SDK's integration tests handle this

Amendment-dependent test classes (e.g. `TestIBatch`, `TestIDelegateSet`) use `Tests/Xrpl.Tests/Integration/AmendmentGuard.cs`: `ClassInitialize` reads the `Amendments` ledger object and marks the class inconclusive when the required amendment is absent. This keeps CI (release image) green while the same tests run for real against the nightly stand. To gate a new test class, add the amendment id constant to `AmendmentGuard` and call `Assert.Inconclusive` from `TestInitialize` when inactive.
