#!/usr/bin/env bash
# Waits until a stand has enabled its genesis amendments.
#
#   .ci-config/wait-genesis-amendments.sh <cfg> [attempts] [rpc-url]
#
# The stand's config lists the genesis up-votes in [amendments] ("<hash> <name>"), and the
# node enables them on the first ledger. The wait passes once `feature` reports any of those
# hashes enabled, which tells a started stand from one whose config was ignored or whose
# amendment-gated transactors would still return temDISABLED.
#
# Any entry rather than a fixed one: rippled retires amendments (AMM, XRPFees and fixFillOrKill
# on develop in October 2026), and `feature` reports a retired one "enabled": false on a ledger
# that never enabled it, although the node runs it. generate-amendments.sh drops retired names
# from [amendments], but a name obsoleted instead (XChainBridge) can stay listed and still never
# enable, so no single entry is a safe sentinel.
#
# Exit codes: 0 ready; 1 not ready after the last attempt; 2 bad input.
set -euo pipefail

cfg="${1:?usage: wait-genesis-amendments.sh <cfg> [attempts] [rpc-url]}"
attempts="${2:-30}"
url="${3:-http://localhost:5005/}"
[ -s "$cfg" ] || { echo "config not found or empty: $cfg" >&2; exit 2; }

hashes=$(sed -n '/^\[amendments\]/,/^\[/p' "$cfg" | sed -n -E 's/^([0-9A-Fa-f]{64})[[:space:]].*/\1/p' | tr '[:lower:]' '[:upper:]')
[ -n "$hashes" ] || { echo "no [amendments] entries in $cfg" >&2; exit 2; }
wanted=$(printf '%s\n' "$hashes" | jq -R . | jq -sc .)
total=$(printf '%s\n' "$hashes" | grep -c .)

for i in $(seq 1 "$attempts"); do
  # `|| true`: a partial or empty response while the node starts must not end the loop under -e.
  enabled=$(curl -sf --max-time 5 "$url" -d '{"method":"feature"}' 2>/dev/null \
    | jq -r --argjson wanted "$wanted" \
        '[.result.features // {} | to_entries[] | select((.key | ascii_upcase) as $k | $wanted | index($k)) | select(.value.enabled == true)] | length' \
        2>/dev/null || true)
  if [ -n "$enabled" ] && [ "$enabled" -gt 0 ]; then
    echo "Stand is ready: $enabled of $total genesis amendments from $cfg are enabled"
    exit 0
  fi
  echo "Waiting for genesis amendments from $cfg... ($i/$attempts)"
  sleep 2
done

echo "No genesis amendment from $cfg is enabled after $attempts attempts" >&2
exit 1
