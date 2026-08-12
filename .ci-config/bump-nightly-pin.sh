#!/usr/bin/env bash
# Move the nightly stand to the newest xrpld build published in the nightly apt
# channel: rewrite ARG XRPLD_VERSION in Dockerfile.nightly and regenerate
# rippled.batchv11.cfg from the matching develop commit.
#
# Why this needs to happen regularly, not only when a new amendment is wanted:
# definitions-watch raises its stand from docker-compose.batchv11.yml, i.e. from
# THIS pin. While the pin is stale the weekly monitor diffs definitions.json
# against a build older than the one CI runs and reports "in sync" about the
# past — which is how rippled 3.3.0 renaming sfMutableFlags to sfImmutableFlags
# and moving SponsorshipSet onto delta fields reached the CI bump PR unnoticed.
#
# The generated config MUST come from the commit the pinned binary was built
# from: a newer ref can emit feature names the binary does not know, and rippled
# rejects unknown names in config at startup. The commit is taken from the
# version string itself, so the two cannot drift apart.
#
# Usage:
#   .ci-config/bump-nightly-pin.sh            # bump to the newest build
#   .ci-config/bump-nightly-pin.sh --check    # report only, change nothing
#
# Outputs (stdout, and $GITHUB_OUTPUT when set):
#   old_version, new_version, new_ref, age_days (age of the CURRENT pin)
#
# Exit codes: 0 done or already current (see `bumped`), 1 error.

set -euo pipefail

PACKAGES_URL="https://repos.ripple.com/repos/rippled-deb/dists/jammy/nightly/binary-amd64/Packages"
DIR="$(cd "$(dirname "$0")" && pwd)"
DOCKERFILE="$DIR/Dockerfile.nightly"
CFG="$DIR/rippled.batchv11.cfg"

check_only=false
if [ "${1:-}" = "--check" ]; then
  check_only=true
elif [ $# -gt 0 ]; then
  echo "usage: $(basename "$0") [--check]" >&2
  exit 1
fi

for f in "$DOCKERFILE" "$CFG"; do
  [ -f "$f" ] || { echo "error: not found: $f" >&2; exit 1; }
done

old_version=$(sed -n -E 's/^ARG XRPLD_VERSION=(.+)$/\1/p' "$DOCKERFILE")
if [ -z "$old_version" ]; then
  echo "error: could not read ARG XRPLD_VERSION from $DOCKERFILE" >&2
  exit 1
fi

packages=$(curl -sf --max-time 60 "$PACKAGES_URL") || {
  echo "error: could not fetch $PACKAGES_URL" >&2
  exit 1
}

# Version strings look like 3.4.0~b0+202608111815.26cc683e-1: upstream version,
# a build timestamp and the develop commit it was built from. The timestamp
# format shrank from 14 digits (YYYYMMDDHHMMSS) to 12 (YYYYMMDDHHMM) mid-2026,
# which is why plain version sorting ranks old builds above new ones and why the
# pin exists at all. Truncating both to YYYYMMDDHHMM makes them comparable.
newest=$(printf '%s\n' "$packages" \
  | awk '/^Package: xrpld$/ { p = 1; next } /^Version: / { if (p) print $2; p = 0 }' \
  | while IFS= read -r v; do
      ts=$(printf '%s' "$v" | sed -n -E 's/.*\+([0-9]{12,14})\..*/\1/p')
      [ -n "$ts" ] && printf '%s %s\n' "${ts:0:12}" "$v"
    done \
  | LC_ALL=C sort -k1,1n | tail -1)

if [ -z "$newest" ]; then
  echo "error: no parseable xrpld versions in the nightly Packages index" >&2
  exit 1
fi

new_version=${newest#* }
new_ts=${newest%% *}
new_ref=$(printf '%s' "$new_version" | sed -n -E 's/.*\+[0-9]{12,14}\.([0-9a-f]+).*/\1/p')
if [ -z "$new_ref" ]; then
  echo "error: could not extract the develop commit from '$new_version'" >&2
  exit 1
fi

old_ts=$(printf '%s' "$old_version" | sed -n -E 's/.*\+([0-9]{12,14})\..*/\1/p')
old_ts=${old_ts:0:12}
age_days=""
if [ -n "$old_ts" ]; then
  old_epoch=$(date -u -d "${old_ts:0:8} ${old_ts:8:2}:${old_ts:10:2}" +%s 2>/dev/null || echo "")
  if [ -n "$old_epoch" ]; then
    age_days=$(( ( $(date -u +%s) - old_epoch ) / 86400 ))
  fi
fi

bumped=false
if [ "$old_version" = "$new_version" ]; then
  echo "Nightly pin is already the newest build ($old_version)."
elif [ "$check_only" = true ]; then
  echo "Nightly pin $old_version is behind $new_version (build ${new_ts}); run without --check to bump."
else
  # Match the whole value so a partially-rewritten pin cannot survive.
  sed -i -E "s#^ARG XRPLD_VERSION=.+\$#ARG XRPLD_VERSION=${new_version}#" "$DOCKERFILE"
  written=$(sed -n -E 's/^ARG XRPLD_VERSION=(.+)$/\1/p' "$DOCKERFILE")
  if [ "$written" != "$new_version" ]; then
    echo "error: rewriting ARG XRPLD_VERSION failed (file now holds '$written')" >&2
    exit 1
  fi
  bash "$DIR/generate-amendments.sh" "$new_ref" "$CFG"
  bumped=true
  echo "Nightly pin bumped: $old_version -> $new_version (ref $new_ref)."
fi

[ -n "$age_days" ] && echo "Current pin age before this run: ${age_days}d."

if [ -n "${GITHUB_OUTPUT:-}" ]; then
  {
    echo "old_version=$old_version"
    echo "new_version=$new_version"
    echo "new_ref=$new_ref"
    echo "age_days=${age_days:-unknown}"
    echo "bumped=$bumped"
  } >> "$GITHUB_OUTPUT"
fi
