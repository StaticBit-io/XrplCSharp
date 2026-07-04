# Mainnet Node Guide

How to run your own `rippled` node connected to the XRP Ledger **mainnet**: installation, configuration, systemd operation, monitoring, and — critically — keeping it updated so it never becomes amendment-blocked.

For a local development node see the [Standalone Node Guide](StandaloneNode-Guide.md).

## Why run your own node

- Your applications talk to `ws://localhost` — no rate limits, no third-party trust, lowest latency for `submit` and subscriptions
- Full control over history retention and API load
- Public clusters (`wss://xrplcluster.com`, `wss://s1.ripple.com`) throttle heavy users and may lag behind on busy days

Node roles (this guide covers the first one):

| Role | Purpose |
|------|---------|
| Stock node | Tracks the network, serves API, submits transactions — what an application needs |
| Full-history node | Same + complete ledger history from ledger 32570; tens of TB of NVMe |
| Validator | Participates in consensus; separate hardening/key ceremony — see [xrpl.org validator docs](https://xrpl.org/run-rippled-as-a-validator.html) |

## Hardware (stock node, 2026)

| Resource | Minimum | Recommended |
|----------|---------|-------------|
| CPU | 4 cores / 8 threads, high clock | 8+ physical cores |
| RAM | 32 GB | 64 GB |
| Disk | NVMe SSD, 10k+ sustained IOPS. **No HDD, no network storage** | NVMe RAID |
| Disk size | ~50 GB for a few days of history | 300+ GB for ~1 month (see `online_delete`) |
| Network | 100 Mbit, stable | 1 Gbit |

Disk usage is driven entirely by history retention: mainnet produces a ledger every 3–5 seconds around the clock.

## Installation (Ubuntu / Debian)

Packages are published to the official apt repository (channel `stable`):

```bash
# repository key
sudo install -m 0755 -d /usr/share/keyrings
curl -fsSL https://repos.ripple.com/repos/api/gpg/key/public | \
    sudo gpg --dearmor -o /usr/share/keyrings/ripple-key.gpg

# repository (jammy/noble — match your release)
echo "deb [signed-by=/usr/share/keyrings/ripple-key.gpg] https://repos.ripple.com/repos/rippled-deb jammy stable" | \
    sudo tee /etc/apt/sources.list.d/ripple.list

sudo apt update && sudo apt install -y rippled
```

The package installs:

| Path | Purpose |
|------|---------|
| `/opt/ripple/bin/rippled` | binary |
| `/etc/opt/ripple/rippled.cfg` | main config |
| `/etc/opt/ripple/validators.txt` | UNL (trusted validator list) source |
| `/lib/systemd/system/rippled.service` | systemd unit |
| `/var/lib/rippled/` | databases (point this at the NVMe volume) |

## Configuration

Edit `/etc/opt/ripple/rippled.cfg`. A production-sane skeleton for an application node:

```ini
[server]
port_rpc_admin_local
port_ws_admin_local
port_ws_public
port_peer

# admin — localhost ONLY, never expose
[port_rpc_admin_local]
port = 5005
ip = 127.0.0.1
admin = 127.0.0.1
protocol = http

[port_ws_admin_local]
port = 6006
ip = 127.0.0.1
admin = 127.0.0.1
protocol = ws

# public WebSocket for your applications (bind to a private interface,
# or keep 127.0.0.1 and put nginx/TLS in front)
[port_ws_public]
port = 6005
ip = 0.0.0.0
protocol = ws
# limit resource use by public clients:
# send_queue_limit = 500

# peer protocol — open this one to the internet
[port_peer]
port = 51235
ip = 0.0.0.0
protocol = peer

[node_size]
huge                     # medium for 32 GB RAM, huge for 64 GB+

[node_db]
type = NuDB              # NuDB for production nodes (append-only, SSD-friendly)
path = /var/lib/rippled/db/nudb
online_delete = 512000   # keep ~512k ledgers (~3-4 weeks); minimum 256
advisory_delete = 0

[database_path]
/var/lib/rippled/db

[ledger_history]
256                      # how many ledgers to backfill on start; <= online_delete

[debug_logfile]
/var/log/rippled/debug.log

[sntp_servers]
time.windows.com
time.apple.com
time.nist.gov
pool.ntp.org

[validators_file]
validators.txt

[ssl_verify]
1
```

Key points:

- **`network_id` is not set** for mainnet (it defaults to mainnet). Setting it wrongly is a classic way to end up on the wrong network.
- **UNL**: the stock `validators.txt` points at the dUNL publishers (`vl.ripple.com`, `unl.xrplf.org`). Do not edit it unless you know exactly why.
- **`online_delete`** is what keeps your disk finite: the node keeps a rotating window of ledgers and deletes older shards. Size the disk for the window, not the other way around.
- **Never expose admin ports.** `admin = 127.0.0.1`, and if you need remote API — publish only `port_ws_public`/`port_rpc_public` behind nginx with TLS (see the `nginx` reverse-proxy pattern with WebSocket upgrade headers).
- **Firewall**: inbound `51235/tcp` open to the world (peer protocol benefits from inbound connectivity), everything else closed or internal.

## Running under systemd

```bash
sudo systemctl enable --now rippled
sudo systemctl status rippled

# logs
journalctl -u rippled -f
```

First start on mainnet: the node fetches the latest validated ledger and backfills `ledger_history`. Expect **10–30 minutes** to reach `"server_state": "full"` (longer on modest hardware).

### Health check

```bash
/opt/ripple/bin/rippled server_info | jq '.result.info | {build_version, server_state, complete_ledgers, peers, load_factor, amendment_blocked}'
```

| Field | Healthy value |
|-------|---------------|
| `server_state` | `full` (a validator shows `proposing`) |
| `complete_ledgers` | a contiguous range, e.g. `95000000-95120000` — not `empty` |
| `peers` | 10+ |
| `load_factor` | 1 (spikes under fee escalation are normal) |
| `amendment_blocked` | **must be absent** — see below |

## Updating — and why it is not optional

XRPL evolves through **amendments**. Two weeks after an amendment gains >80% validator support it activates network-wide. A node whose binary does not implement an active amendment becomes **amendment-blocked**: it stops tracking the network, refuses to submit transactions, and `server_info` shows `"amendment_blocked": true`. The only fix is upgrading.

Practical policy:

- Subscribe to [rippled releases](https://github.com/XRPLF/rippled/releases) and the [XRPL blog](https://xrpl.org/blog/); upgrade within days of a release, not months
- The amendment voting dashboard (`feature` command, or [xrpscan.com/amendments](https://xrpscan.com/amendments)) shows what is queued — anything at >80% is your deadline timer

Upgrade procedure (apt):

```bash
sudo apt update
sudo apt install --only-upgrade rippled
sudo systemctl restart rippled
watch -n 5 "/opt/ripple/bin/rippled server_info | jq -r '.result.info.server_state'"
```

Downtime is minutes: after a restart the node re-syncs to the current ledger quickly (it does not replay history). Config files are not overwritten by upgrades; compare with the shipped example after major versions (`/etc/opt/ripple/rippled.cfg.dpkg-dist` if present).

To pin against surprise major upgrades, use `apt-mark hold rippled` and upgrade deliberately.

## Connecting XrplCSharp

```csharp
using Xrpl.Client;

// public WS port of your node
IXrplClient client = new XrplClient("ws://10.0.0.5:6005");
await client.Connect();

ServerInfo info = await client.ServerInfo();
Console.WriteLine(info.Info.CompleteLedgers);
```

Recommendations for production use with this SDK:

- Point the client at **your** node's public WS port; keep admin ports for operations only
- Check `complete_ledgers` covers the range you query — `account_tx` beyond the retention window returns partial data; for deep history use a full-history provider or [Clio](https://github.com/XRPLF/clio)
- `SubmitAndWait` relies on `LastLedgerSequence` — a healthy, synced node is what makes those guarantees real

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `"amendment_blocked": true` | Binary too old for an activated amendment | Upgrade rippled immediately |
| `server_state` stuck in `connected`/`syncing` | Underpowered disk (IOPS), bad clock, too few peers | NVMe only; verify NTP (`[sntp_servers]`, `timedatectl`); open inbound 51235 |
| `complete_ledgers: empty` after long uptime | Node keeps losing sync, history resets | Same as above — almost always disk IOPS or RAM pressure |
| Disk keeps growing | `online_delete` not set | Set `online_delete` in `[node_db]` and restart |
| `noCurrent` / `noNetwork` errors via API | Node not synced yet or lost quorum view | Wait for `full`; check peers and clock |
| High `load_factor` on your requests | Public port under-provisioned or abusive clients | Rate-limit at nginx; scale `node_size`/hardware |
| Crash loop after config edit | Syntax error or unknown section for this version | `journalctl -u rippled -n 50`; validate against the shipped example config |
