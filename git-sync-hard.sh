#!/bin/sh
set -e

echo "🔍 Checking current git branch..."

BRANCH=$(git rev-parse --abbrev-ref HEAD)

if [ "$BRANCH" = "HEAD" ]; then
  echo "❌ Detached HEAD state. Abort."
  exit 1
fi

echo "✅ Current branch: $BRANCH"
echo

echo "📡 Fetching from origin..."
git fetch origin
echo

REMOTE_BRANCH="origin/$BRANCH"

echo "📌 Remote branch: $REMOTE_BRANCH"
echo

echo "⚠️  WARNING: This will HARD RESET your local branch to:"
echo "   $REMOTE_BRANCH"
echo "   All local changes and commits will be LOST."
echo

printf "Type 'yes' to continue: "
read CONFIRM

if [ "$CONFIRM" != "yes" ]; then
  echo "❌ Aborted by user."
  exit 0
fi

echo
echo "🔄 Resetting local branch..."

git reset --hard "$REMOTE_BRANCH"

echo
echo "✅ Done."
echo
echo "📄 Current status:"
git status
echo
echo "🕒 Last commits:"
git --no-pager log --oneline --graph -n 5
