#!/usr/bin/env bash
# Mutation-tests Vista.Core with Stryker; the settings are in tests/Vista.Tests/stryker-config.json.
# Given a commit, only code changed since it is mutated: scripts/mutate.sh eb36714
source "$(dirname "$0")/env.sh"
cd "$ROOT/tests/Vista.Tests"
dotnet tool restore > /dev/null
if [ -n "${1:-}" ]; then
  dotnet stryker "--since:$(git rev-parse "$1")"
else
  dotnet stryker
fi
