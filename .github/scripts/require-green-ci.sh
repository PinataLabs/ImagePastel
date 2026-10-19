#!/usr/bin/env bash
# Fails unless the CI workflow passed on a push of the given commit, or on a
# manual run of it. CI skips pushes that change only Markdown files, so such a
# commit needs a manual run before it can be released.
#
# Release and pre-release workflows only pack and publish, so this check is what
# keeps a package from shipping a commit that did not build, test, and keep the
# public API on all three platforms.
#
# Usage: require-green-ci.sh <commit-sha>
# Needs GH_TOKEN and GITHUB_REPOSITORY, and the job needs `actions: read`.
set -euo pipefail

sha="$1"

conclusion=$(gh run list --repo "$GITHUB_REPOSITORY" --workflow ci.yml --commit "$sha" \
  --limit 20 --json event,status,conclusion \
  --jq '[.[] | select(.status == "completed" and (.event == "push" or .event == "workflow_dispatch"))] | .[0].conclusion // ""')

if [ "$conclusion" != "success" ]; then
  echo "::error::CI has not passed on a push or a manual run of ${sha:0:9} (latest completed result: ${conclusion:-none}). Wait for CI on main to finish green, then run again. If the commit changed only Markdown files, CI skipped it: run the CI workflow on main by hand first."
  exit 1
fi

echo "CI passed on ${sha:0:9}."
