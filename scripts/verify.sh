#!/usr/bin/env bash
# What must pass before a commit: formats, lints, runs the tests, and checks Core's line coverage.
# --check checks the formatting instead of applying it, for scripts that need the tree left alone.
source "$(dirname "$0")/env.sh"
"$ROOT/scripts/format.sh" "$@"
"$ROOT/scripts/lint.sh"

# Coverage slows the property tests fiftyfold, so they run on their own; the floor is Threshold in Vista.Tests.csproj.
# The full output is kept in the log, so a failing property's seed and input are never lost.
log="$ROOT/tests/Vista.Tests/obj/verify-tests.log"
: > "$log"
"$ROOT/scripts/test.sh" --no-build --filter "Category!=Property" -p:CollectCoverage=true 2>&1 | tee -a "$log"
"$ROOT/scripts/test.sh" --no-build --filter "Category=Property" 2>&1 | tee -a "$log"
