#!/usr/bin/env bash
# Checks that every rippled RPC method is either implemented by the SDK or listed as excluded.
#
#   .ci-config/check-rpc-methods.sh [ref]
#
# rippled names every method it accepts in src/xrpld/rpc/MethodNames.h, shared by the dispatch
# table and the command-line parser. The SDK names a method through `Command = "..."` in its
# request models under Xrpl/. A method in the first list and not the second is either a gap or
# a deliberate exclusion; exclusions live in .ci-config/rpc-methods-ignore.txt with a reason.
#
# The protocol-watch diff only reports changes after its baseline, so a method added before the
# baseline (get_aggregate_price) was never reported. This check compares the full lists on every
# run instead.
#
# ref defaults to RIPPLED_REF, then develop. METHOD_NAMES_FILE names a local copy of
# MethodNames.h and skips the download.
#
# Output lines: ok / GAP (listed as a known gap) / ignored / MISSING / STALE / sdk-only.
# Exit codes: 0 every method is covered; 1 a method is missing or an ignore entry is stale;
# 2 bad input.
set -euo pipefail

ref="${1:-${RIPPLED_REF:-develop}}"
root="$(cd "$(dirname "$0")/.." && pwd)"
ignore_file="${RPC_METHODS_IGNORE:-$root/.ci-config/rpc-methods-ignore.txt}"
sdk_dir="${SDK_DIR:-$root/Xrpl}"
min_upstream=60

[ -s "$ignore_file" ] || { echo "ignore list not found or empty: $ignore_file" >&2; exit 2; }
[ -d "$sdk_dir" ] || { echo "SDK sources not found: $sdk_dir" >&2; exit 2; }

if [ -n "${METHOD_NAMES_FILE:-}" ]; then
  header=$(cat "$METHOD_NAMES_FILE")
else
  url="https://raw.githubusercontent.com/XRPLF/rippled/$ref/src/xrpld/rpc/MethodNames.h"
  header=$(curl -fsSL --retry 3 "$url") || { echo "could not fetch $url" >&2; exit 2; }
fi

# `inline constexpr std::string_view kAccountInfo{"account_info"};`. Fewer names than a release
# has ever had means the file moved or changed shape; fail rather than compare a partial list.
upstream=$( (tr -d '\r' <<< "$header" \
  | grep -oE 'string_view[[:space:]]+k[A-Za-z0-9_]+[[:space:]]*\{[[:space:]]*"[a-z0-9_]+"' || true) \
  | sed -E 's/.*"([a-z0-9_]+)"$/\1/' | sort -u)
count=$(grep -c . <<< "$upstream" || true)
if [ "$count" -lt "$min_upstream" ]; then
  echo "parsed $count method names from MethodNames.h at $ref, expected at least $min_upstream" >&2
  exit 2
fi

# `Command = "account_info"`, not `SubCommand = "create"`: path_find subcommands are not methods.
sdk=$( (grep -rhoE --include='*.cs' '(^|[^A-Za-z0-9_])Command[[:space:]]*=[[:space:]]*"[a-z0-9_]+"' "$sdk_dir" || true) \
  | sed -E 's/.*"([a-z0-9_]+)"$/\1/' | sort -u)
[ -n "$sdk" ] || { echo "no Command = \"...\" values found under $sdk_dir" >&2; exit 2; }

# `name  # category: reason`. An entry without a reason is rejected: the list is only worth
# keeping while each line says why.
ignored=""
while IFS= read -r line; do
  line="${line%$'\r'}"
  [[ "$line" =~ ^[[:space:]]*(#|$) ]] && continue
  if ! [[ "$line" =~ ^[[:space:]]*([a-z0-9_]+)[[:space:]]+#[[:space:]]*(.*[^[:space:]])[[:space:]]*$ ]]; then
    echo "malformed ignore entry (expected 'name  # category: reason'): $line" >&2
    exit 2
  fi
  name="${BASH_REMATCH[1]}"
  reason="${BASH_REMATCH[2]}"
  ignored+="$name"$'\t'"$reason"$'\n'
done < "$ignore_file"
[ -n "$ignored" ] || { echo "no entries parsed from $ignore_file" >&2; exit 2; }

dupes=$(cut -f1 <<< "$ignored" | grep . | sort | uniq -d)
[ -z "$dupes" ] || { echo "duplicate ignore entries: $dupes" >&2; exit 2; }

echo "rippled $ref: $count methods; SDK: $(grep -c . <<< "$sdk") methods"
status=0

while IFS= read -r name; do
  reason=$(awk -F'\t' -v n="$name" '$1 == n { print $2; exit }' <<< "$ignored")
  if grep -qxF "$name" <<< "$sdk"; then
    echo "ok         $name"
  elif [ -z "$reason" ]; then
    echo "MISSING    $name"; status=1
  elif [[ "$reason" == gap* ]]; then
    echo "GAP        $name  # $reason"
  else
    echo "ignored    $name  # $reason"
  fi
done <<< "$upstream"

while IFS=$'\t' read -r name reason; do
  [ -n "$name" ] || continue
  if ! grep -qxF "$name" <<< "$upstream"; then
    echo "STALE      $name  # no longer in MethodNames.h"; status=1
  elif grep -qxF "$name" <<< "$sdk"; then
    echo "STALE      $name  # implemented now, drop the entry"; status=1
  fi
done <<< "$ignored"

# Names the SDK sends that rippled does not register: Clio-only methods (nft_info, nft_history)
# are expected. Reported, never a failure.
while IFS= read -r name; do
  grep -qxF "$name" <<< "$upstream" || echo "sdk-only   $name"
done <<< "$sdk"

exit "$status"
