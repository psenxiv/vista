#!/usr/bin/env bash
# Builds the plugin and the tests with every analyzer warning as an error; the rules live in .editorconfig.
source "$(dirname "$0")/env.sh"
"$ROOT/scripts/build.sh" -warnaserror
dotnet build "$ROOT/tests/Vista.Tests/Vista.Tests.csproj" -warnaserror
