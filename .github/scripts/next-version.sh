#!/usr/bin/env bash
# Works out the next version tag for HEAD from the conventional-commit subjects since the last vX.Y.Z tag:
#   - "type!:" or a "BREAKING CHANGE" footer -> major bump
#   - "feat:" / "feat(scope):"               -> minor bump
#   - anything else (fix, perf, docs, ...)   -> patch bump
# With no tag yet, the first version is the <Version> in the app's .csproj.
#
# Prints version=, tag= and skip= lines, and also appends them to $GITHUB_OUTPUT when run in GitHub Actions.
# skip=true means HEAD already carries a version tag (e.g. a re-run), so nothing should be tagged.
set -euo pipefail

csproj="src/AudioDeviceDebugger/AudioDeviceDebugger.csproj"
last=$(git tag --list 'v[0-9]*.[0-9]*.[0-9]*' --sort=-v:refname | head -n 1)
skip=false

if [ -z "$last" ]; then
  version=$(sed -n 's:.*<Version>\([0-9][0-9.]*\)</Version>.*:\1:p' "$csproj" | head -n 1)
  if [ -z "$version" ]; then
    echo "No <Version> found in $csproj" >&2
    exit 1
  fi
else
  if [ "$(git rev-list -n 1 "$last")" = "$(git rev-parse HEAD)" ]; then
    version=${last#v}
    skip=true
  else
    IFS=. read -r major minor patch <<< "${last#v}"
    subjects=$(git log --format=%s "$last..HEAD")
    bodies=$(git log --format=%b "$last..HEAD")
    if grep -qE '^[a-zA-Z]+(\([^)]*\))?!:' <<< "$subjects" || grep -q 'BREAKING CHANGE' <<< "$bodies"; then
      major=$((major + 1)); minor=0; patch=0
    elif grep -qE '^feat(\([^)]*\))?:' <<< "$subjects"; then
      minor=$((minor + 1)); patch=0
    else
      patch=$((patch + 1))
    fi
    version="$major.$minor.$patch"
  fi
fi

result="version=$version
tag=v$version
skip=$skip"
echo "$result"
if [ -n "${GITHUB_OUTPUT:-}" ]; then
  echo "$result" >> "$GITHUB_OUTPUT"
fi
