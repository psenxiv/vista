#!/usr/bin/env bash
# Formats the C# and project files with CSharpier. --check lists unformatted files and fails instead of changing them.
source "$(dirname "$0")/env.sh"
cd "$ROOT"
dotnet tool restore > /dev/null
if [ "${1:-}" = "--check" ]; then
  dotnet csharpier check .
else
  dotnet csharpier format .
fi
