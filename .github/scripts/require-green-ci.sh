#!/usr/bin/env bash
# Fails unless the CI workflow passed on a push of the given commit, or on a
# manual run of it. CI skips pushes that change only Markdown files (ci.yml's
# `paths-ignore: '**.md'`), and such a commit builds exactly as its parent did.
# So the check walks back past Markdown-only commits, such as the changelog roll
# that release-publish.yml tags, and asks for CI on the commit CI last built.
#
# Release and pre-release workflows only pack and publish, so this check is what
# keeps a package from shipping a commit that did not build, test, and keep the
# public API on every OS in the CI matrix.
#
# Usage: require-green-ci.sh <commit-sha>
# Needs GH_TOKEN and GITHUB_REPOSITORY, the job needs `actions: read`, and the
# checkout needs the history back to the last commit CI built.
set -euo pipefail

sha="$1"

built="$sha"
while parent=$(git rev-parse -q --verify "$built^") &&
  ! git diff --name-only "$parent" "$built" | grep -qv '\.md$'; do
  built="$parent"
done

if [ "$built" != "$sha" ]; then
  echo "${sha:0:9} changes only Markdown files since ${built:0:9}, so checking CI on ${built:0:9}."
fi

conclusion=$(gh run list --repo "$GITHUB_REPOSITORY" --workflow ci.yml --commit "$built" \
  --limit 20 --json event,status,conclusion \
  --jq '[.[] | select(.status == "completed" and (.event == "push" or .event == "workflow_dispatch"))] | .[0].conclusion // ""')

if [ "$conclusion" != "success" ]; then
  echo "::error::CI has not passed on a push or a manual run of ${built:0:9} (latest completed result: ${conclusion:-none}). Wait for CI on main to finish green, then run again."
  exit 1
fi

echo "CI passed on ${built:0:9}."
