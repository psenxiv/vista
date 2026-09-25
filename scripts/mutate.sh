#!/usr/bin/env bash
# Mutation-tests Vista.Core with Stryker and prints the score and each surviving mutant.
# Given a commit, only files changed since it are mutated, and only survivors on lines changed since it are listed:
# scripts/mutate.sh eb36714
source "$(dirname "$0")/env.sh"
cd "$ROOT/tests/Vista.Tests"
dotnet tool restore > /dev/null

commit=""
mutate=()
if [ -n "${1:-}" ]; then
  commit="$(git rev-parse "$1")"
  # Stryker's own --since reads the main checkout's paths from a worktree and mutates nothing, so the files are listed here.
  files="$(cd "$ROOT" && { git diff --name-only --diff-filter=d "$commit" -- src/Vista.Core; git ls-files --others --exclude-standard -- src/Vista.Core; } | grep '\.cs$' || true)"
  if [ -z "$files" ]; then
    echo "No Core files changed since ${commit:0:7}"
    exit 0
  fi
  while read -r file; do
    mutate+=(-m "**/$file")
  done <<< "$files"
fi

rm -rf StrykerOutput
mkdir StrykerOutput
if ! dotnet stryker ${mutate[@]+"${mutate[@]}"} > StrykerOutput/run.log 2>&1; then
  cat StrykerOutput/run.log
  exit 1
fi

python3 "$ROOT/scripts/survivors.py" StrykerOutput/*/reports/mutation-report.json $commit
rm -rf StrykerOutput
