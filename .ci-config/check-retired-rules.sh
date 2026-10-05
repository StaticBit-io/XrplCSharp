#!/usr/bin/env bash
# Checks the amendments LedgerRules reads by name against rippled's features.macro.
#
#   .ci-config/check-retired-rules.sh <features.macro> [LedgerRules.cs]
#
# rippled retires an amendment two years after mainnet enables it: the name moves to
# XRPL_RETIRE_FEATURE / XRPL_RETIRE_FIX and the pre-amendment code is deleted, so the node
# behaves as enabled whatever the ledger records. The `feature` command still lists the name
# and still reports `enabled` from the ledger, which is false on a private or standalone ledger
# that never enabled it. LedgerRules would then compute the old behaviour while the node runs
# the new one. A retired amendment has to be hardwired on in the port, as rippled did.
#
# A name features.macro no longer registers at all is reported too: the node cannot report it,
# so LedgerRules reads it as disabled.
#
# A retired name is hardwired on in the port. An unregistered one is investigated instead: the
# amendment may have been dropped before it was ever enabled (Batch, replaced by BatchV1_1) or
# renamed, and nothing says its behaviour is now unconditional.
#
# Exit codes: 0 every name is active upstream; 1 a name is retired or unregistered; 2 bad input.
set -euo pipefail

macro="${1:?usage: check-retired-rules.sh <features.macro> [LedgerRules.cs]}"
rules="${2:-$(dirname "$0")/../Xrpl/Sugar/LedgerRules.cs}"
[ -s "$macro" ] || { echo "features.macro not found or empty: $macro" >&2; exit 2; }
[ -s "$rules" ] || { echo "LedgerRules.cs not found or empty: $rules" >&2; exit 2; }

# The registered name of every entry, with its kind: XRPL_FIX and XRPL_RETIRE_FIX prepend "fix".
registry=$(sed -nE \
  -e 's/^[[:space:]]*XRPL_FEATURE[[:space:]]*\([[:space:]]*([A-Za-z0-9_]+).*/active \1/p' \
  -e 's/^[[:space:]]*XRPL_FIX[[:space:]]*\([[:space:]]*([A-Za-z0-9_]+).*/active fix\1/p' \
  -e 's/^[[:space:]]*XRPL_RETIRE_FEATURE[[:space:]]*\([[:space:]]*([A-Za-z0-9_]+).*/retired \1/p' \
  -e 's/^[[:space:]]*XRPL_RETIRE_FIX[[:space:]]*\([[:space:]]*([A-Za-z0-9_]+).*/retired fix\1/p' \
  -e 's/^[[:space:]]*XRPL_RETIRE[[:space:]]*\([[:space:]]*([A-Za-z0-9_]+).*/retired \1/p' \
  "$macro")
[ -n "$registry" ] || { echo "no amendment entries parsed from $macro" >&2; exit 2; }

# The names LedgerRules reads from the `feature` response. Whitespace, line breaks included, is
# folded first, so `GetByName( "Flow" )` and a call split over lines are read like the compact
# form. Finding none means the reading code changed shape; fail rather than report an empty list
# as clean.
names=$( (tr '\r\n\t' '   ' < "$rules" \
  | grep -oE '(GetByName|Enabled)[[:space:]]*\([[:space:]]*"[A-Za-z0-9_]+"[[:space:]]*\)' || true) \
  | sed -E 's/.*"([A-Za-z0-9_]+)".*/\1/' | sort -u)
[ -n "$names" ] || { echo "no amendment names found in $rules" >&2; exit 2; }

status=0
while IFS= read -r name; do
  kind=$(awk -v n="$name" '$2 == n { print $1; exit }' <<< "$registry")
  case "$kind" in
    active)  echo "ok         $name" ;;
    retired) echo "RETIRED    $name"; status=1 ;;
    *)       echo "UNKNOWN    $name"; status=1 ;;
  esac
done <<< "$names"

exit "$status"
