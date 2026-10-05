#!/usr/bin/env bash
# Prints the `dotnet test --filter` of one integration shard.
#
#   .ci-config/integration-shard-filter.sh <shard>
#
# Shards named in integration-shards.txt run the classes listed for them; the shard after the
# last one listed runs every other TestI class. A class is matched as ".<Class>." within its
# fully qualified test name, so TestIBookCrossing does not also take TestIBookCrossingAmm.
set -euo pipefail

shard="${1:?usage: integration-shard-filter.sh <shard>}"
list="$(dirname "$0")/integration-shards.txt"

last=$(grep -v '^#' "$list" | awk 'NF == 2 { print $1 }' | sort -n | tail -1)
if [ "$shard" -le "$last" ]; then
  classes=$(grep -v '^#' "$list" | awk -v s="$shard" 'NF == 2 && $1 == s { printf "%sFullyQualifiedName~.%s.", sep, $2; sep = "|" }')
  [ -n "$classes" ] || { echo "shard $shard lists no class" >&2; exit 1; }
  echo "TestI&($classes)"
elif [ "$shard" -eq $((last + 1)) ]; then
  echo "TestI$(grep -v '^#' "$list" | awk 'NF == 2 { printf "&FullyQualifiedName!~.%s.", $2 }')"
else
  echo "shard $shard is beyond the last one, $((last + 1))" >&2
  exit 1
fi
