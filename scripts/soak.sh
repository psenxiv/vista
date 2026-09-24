#!/usr/bin/env bash
# Runs the property tests again and again, 20 times or as many as given, stopping at the first failure: scripts/soak.sh 50
# The failing run's output is kept in obj/soak.log, and the failing input in obj/counterexamples.
source "$(dirname "$0")/env.sh"
runs="${1:-20}"
[[ "$runs" =~ ^[1-9][0-9]*$ ]] || fail "Usage: scripts/soak.sh [runs]"
log="$ROOT/tests/Vista.Tests/obj/soak.log"
rm -rf "$COUNTEREXAMPLES"

for run in $(seq 1 "$runs"); do
  # The first run builds; the rest reuse it.
  build=$([ "$run" = 1 ] && echo "" || echo "--no-build")
  if ! "$ROOT/scripts/test.sh" $build --filter "Category=Property" > "$log" 2>&1; then
    grep -E "Failed Vista|CsCheck_Seed" "$log" >&2 || true
    fail "Run $run of $runs failed: the output is in $log and the input in $COUNTEREXAMPLES"
  fi
  echo "Run $run of $runs passed"
done
