#!/usr/bin/env bash
# What must pass before a commit: formats, builds the plugin with no warnings, and runs the tests.
# --check checks the formatting instead of applying it, for scripts that need the tree left alone.
source "$(dirname "$0")/env.sh"
"$ROOT/scripts/format.sh" "$@"
"$ROOT/scripts/build.sh" -warnaserror
"$ROOT/scripts/test.sh"
