#!/usr/bin/env bash
# Mutation-tests Vista.Core with Stryker and prints the score and each surviving mutant.
# Given a commit, only files changed since it are mutated, and only survivors on lines changed since it are listed:
# scripts/mutate.sh eb36714
source "$(dirname "$0")/env.sh"
cd "$ROOT/tests/Vista.Tests"
dotnet tool restore > /dev/null

since=""
commit=""
if [ -n "${1:-}" ]; then
  commit="$(git rev-parse "$1")"
  since="--since:$commit"
fi

rm -rf StrykerOutput
mkdir StrykerOutput
if ! dotnet stryker $since > StrykerOutput/run.log 2>&1; then
  cat StrykerOutput/run.log
  exit 1
fi

python3 "$ROOT/scripts/survivors.py" StrykerOutput/*/reports/mutation-report.json $commit
rm -rf StrykerOutput
