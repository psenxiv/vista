#!/usr/bin/env bash
# Runs the Core tests. Extra arguments go to dotnet test.
source "$(dirname "$0")/env.sh"
dotnet test "$ROOT/tests/Vista.Tests/Vista.Tests.csproj" "$@"
