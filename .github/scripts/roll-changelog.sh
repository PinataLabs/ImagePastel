#!/usr/bin/env bash
#
# Rolls the "Unreleased" section of CHANGELOG.md into a dated version heading and
# leaves a fresh, empty "Unreleased" section at the top.
#
#   ## Unreleased          ->   ## Unreleased
#
#   ### 🐛 Bug Fixes             ## v2.2.0 - 2026-11-25
#   - ...
#                                ### 🐛 Bug Fixes
#                                - ...
#
# Usage: roll-changelog.sh <version> [date] [changelog-path]
#
#   version         Version being released, with or without a leading "v".
#   date            Release date as YYYY-MM-DD. Defaults to today (UTC).
#   changelog-path  Defaults to CHANGELOG.md in the repository root.
#
# Exits non-zero if the changelog has no "## Unreleased" heading, already has a
# heading for the version, or has an empty Unreleased section. Releasing with
# nothing written down is almost always a mistake.

set -euo pipefail

version=${1:?usage: roll-changelog.sh <version> [date] [changelog-path]}
date=${2:-$(date -u +%Y-%m-%d)}
changelog=${3:-"$(dirname "$0")/../../CHANGELOG.md"}

version=${version#v}
title='## Unreleased'
heading="## v${version} - ${date}"

if [[ ! -f $changelog ]]; then
  echo "::error::Changelog not found: $changelog" >&2
  exit 2
fi

# Headings are compared as fixed strings, ignoring trailing whitespace, because a
# checkout with CRLF endings leaves a trailing \r on every line.
if ! awk -v want="$title" '
  { line = $0; sub(/[[:space:]]+$/, "", line) }
  line == want { found = 1; exit }
  END { exit !found }
' "$changelog"; then
  echo "::error::No '$title' heading found in $changelog" >&2
  exit 3
fi

if grep -qF -- "## v${version} " "$changelog"; then
  echo "::error::$changelog already contains a heading for v${version}" >&2
  exit 4
fi

unreleased_body=$(awk -v title="$title" '
  { line = $0; sub(/[[:space:]]+$/, "", line) }
  line == title { inside = 1; next }
  inside && /^## / { exit }
  inside { print }
' "$changelog")

if [[ -z ${unreleased_body//[[:space:]]/} ]]; then
  echo "::error::The Unreleased section of $changelog is empty, so there is nothing to release" >&2
  exit 5
fi

tmp=$(mktemp)
trap 'rm -f "$tmp"' EXIT

awk -v title="$title" -v heading="$heading" '
  { print }
  { line = $0; sub(/[[:space:]]+$/, "", line) }
  !done && line == title {
    print ""
    print heading
    done = 1
  }
' "$changelog" >"$tmp"
mv "$tmp" "$changelog"

# Rebuild the "Contents" list from the "## " headings so it cannot drift as releases
# are rolled. Anchors follow GitHub's rule: lower-case, drop every character that is
# not a letter, digit, space or hyphen, then turn spaces into hyphens.
contents=$(
  grep -E '^## ' "$changelog" | while IFS= read -r line; do
    text=${line#'## '}
    text=${text%$'\r'}
    [[ $text == Contents ]] && continue
    anchor=$(printf '%s' "$text" | tr '[:upper:]' '[:lower:]' | tr -cd 'a-z0-9 -' | tr ' ' '-')
    label=${text% - [0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]}
    printf -- '- [%s](#%s)\n' "$label" "$anchor"
  done
)

awk -v contents="$contents" '
  !seen && /^## Contents[[:space:]]*$/ {
    print; print ""; print contents; print ""
    seen = 1; skip = 1
    next
  }
  skip && /^## / { skip = 0 }
  skip { next }
  { print }
' "$changelog" >"$tmp"
mv "$tmp" "$changelog"
trap - EXIT

echo "Rolled '${title}' into '${heading}' in ${changelog}"
