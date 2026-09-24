#!/usr/bin/env bash
# Mutation-tests Vista.Core with Stryker and prints the score and each surviving mutant.
# Given a commit, only code changed since it is mutated: scripts/mutate.sh eb36714
source "$(dirname "$0")/env.sh"
cd "$ROOT/tests/Vista.Tests"
dotnet tool restore > /dev/null

since=""
if [ -n "${1:-}" ]; then
  since="--since:$(git rev-parse "$1")"
fi

rm -rf StrykerOutput
mkdir StrykerOutput
if ! dotnet stryker $since > StrykerOutput/run.log 2>&1; then
  cat StrykerOutput/run.log
  exit 1
fi

python3 "$ROOT/scripts/survivors.py" StrykerOutput/*/reports/mutation-report.json
rm -rf StrykerOutput
