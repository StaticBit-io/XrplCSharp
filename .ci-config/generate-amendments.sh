#!/usr/bin/env bash
# Regenerate the [features] and [amendments] sections of rippled.cfg from the
# features.macro of a given rippled tag.
#
# The two sections do different jobs on xrpld (3.2.x and develop):
#
# - [amendments] ("<hash> <name>", hash = sha512half of the name) registers
#   genesis up-votes at --start: the amendments become enabled ON-LEDGER, so
#   the Amendments ledger object and the `feature` RPC report them, and
#   AmendmentGuard-gated tests run. Only Supported::Yes entries — the binary
#   skips unsupported ones at genesis.
#
# - [features] does NOT vote, but it feeds Rules presets (Rules::enabled()
#   checks presets first, see Rules.cpp): listed amendments are treated as
#   active during TRANSACTION PROCESSING even when not enabled on-ledger.
#   This is the only way to exercise Supported::No code paths (e.g.
#   MPTokensV2 on 3.2.0) — so [features] = all Supported::Yes entries plus
#   the EXTRA_FEATURES list below.
#
# Retired amendments (XRPL_RETIRE_FEATURE / XRPL_RETIRE_FIX) are excluded from
# both: they are permanently baked into the protocol and rippled rejects their
# names in config at startup.
#
# Usage: .ci-config/generate-amendments.sh <rippled-ref> [target-cfg]
#   <rippled-ref>  tag or commit of XRPLF/rippled to read features.macro from
#   [target-cfg]   config file to rewrite (default: rippled.cfg next to this script)
#
# Examples:
#   .ci-config/generate-amendments.sh 3.2.0
#   .ci-config/generate-amendments.sh <develop-sha> .ci-config/rippled.batchv11.cfg
#
# For rippled.batchv11.cfg the ref MUST match the pinned nightly build
# (ARG XRPLD_VERSION in Dockerfile.nightly): generating from a newer develop
# commit can emit feature names unknown to the pinned binary, and rippled
# rejects unknown names in config at startup.

# Supported::No features the SDK test suite needs ahead of rippled support.
# They go into [features] (Rules presets) only; introspection still reports
# them as disabled. Prune entries once the feature ships as Supported::Yes.
#
# This list is shared by every target config, so an entry must be a name BOTH
# binaries know (a Supported::No declaration counts): a name unknown to one of
# them lands in that stand's [features] and rippled rejects it at startup.
# Example of what does NOT belong here: LendingProtocolV1_1 — Supported::No on
# develop, absent from the 3.2.0 macro entirely.
EXTRA_FEATURES="MPTokensV2"

set -euo pipefail

TAG="${1:?usage: generate-amendments.sh <rippled-ref> [target-cfg]}"
CFG="${2:-$(cd "$(dirname "$0")" && pwd)/rippled.cfg}"
if [ ! -f "$CFG" ]; then
  echo "error: target config not found: $CFG" >&2
  exit 1
fi
MACRO_URL="https://raw.githubusercontent.com/XRPLF/rippled/$TAG/include/xrpl/protocol/detail/features.macro"

macro=$(curl -sf --max-time 30 "$MACRO_URL")

names=$(printf '%s\n' "$macro" | sed -n -E \
  -e 's/^ *XRPL_FEATURE *\( *([A-Za-z0-9_]+) *, *Supported::[Yy]es.*/\1/p' \
  -e 's/^ *XRPL_FIX *\( *([A-Za-z0-9_]+) *, *Supported::[Yy]es.*/fix\1/p' \
  | LC_ALL=C sort -u)

if [ -z "$names" ]; then
  echo "error: no Supported::yes amendments parsed from $MACRO_URL" >&2
  exit 1
fi

# [features] additionally carries EXTRA_FEATURES; guard against an extra
# graduating to Supported::Yes (it would then be listed twice).
feature_names=$(printf '%s\n%s\n' "$names" "$EXTRA_FEATURES" | grep . | LC_ALL=C sort -u)

features=""
while IFS= read -r name; do
  features+="$name"$'\n'
done <<< "$feature_names"

amendments=""
while IFS= read -r name; do
  hash=$(printf '%s' "$name" | sha512sum | cut -c1-64 | tr '[:lower:]' '[:upper:]')
  amendments+="$hash $name"$'\n'
done <<< "$names"

# Rewrite the two sections in place: keep section comments (# lines), replace
# the entry lists. If a section is missing entirely, append it at the end.
awk -v feats="$features" -v amds="$amendments" '
  function flush() {
    if (section == "[features]")   { printf "%s\n", feats; seen_f = 1 }
    if (section == "[amendments]") { printf "%s\n", amds;  seen_a = 1 }
    section = ""
  }
  /^\[/ { flush(); section = $0; print; next }
  section == "[features]" || section == "[amendments]" {
    if ($0 ~ /^#/) { print }
    next
  }
  { print }
  END {
    flush()
    if (!seen_f) printf "[features]\n%s\n", feats
    if (!seen_a) printf "[amendments]\n%s\n", amds
  }
' "$CFG" > "$CFG.tmp"
mv "$CFG.tmp" "$CFG"

count=$(printf '%s' "$names" | grep -c .)
echo "Regenerated $CFG from rippled $TAG: $count amendments."
