#!/usr/bin/env bash
# What must pass before a commit: formats, lints, and runs the tests.
# --check checks the formatting instead of applying it, for scripts that need the tree left alone.
source "$(dirname "$0")/env.sh"
"$ROOT/scripts/format.sh" "$@"
"$ROOT/scripts/lint.sh"
"$ROOT/scripts/test.sh" --no-build
