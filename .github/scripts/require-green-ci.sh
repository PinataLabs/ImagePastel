#!/usr/bin/env bash
# Fails unless the CI workflow passed on a push of the given commit.
#
# Release and pre-release workflows only pack and publish, so this check is what
# keeps a package from shipping a commit that did not build, test, and keep the
# public API on all three platforms.
#
# Usage: require-green-ci.sh <commit-sha>
# Needs GH_TOKEN and GITHUB_REPOSITORY, and the job needs `actions: read`.
set -euo pipefail

sha="$1"

conclusion=$(gh run list --repo "$GITHUB_REPOSITORY" --workflow ci.yml --commit "$sha" --event push \
  --limit 20 --json status,conclusion \
  --jq '[.[] | select(.status == "completed")] | .[0].conclusion // ""')

if [ "$conclusion" != "success" ]; then
  echo "::error::CI has not passed on a push of ${sha:0:9} (latest completed result: ${conclusion:-none}). Wait for CI on main to finish green, then run again."
  exit 1
fi

echo "CI passed on ${sha:0:9}."
