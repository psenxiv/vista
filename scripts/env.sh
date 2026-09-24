#!/usr/bin/env bash
# Shared setup for every script: strict mode, the repo root, the plugin project and a few helpers.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/Vista.Plugin/Vista.Plugin.csproj"
# Where a failing property writes its input (Fixtures.CounterexampleFolder), emptied before each property run.
COUNTEREXAMPLES="$ROOT/tests/Vista.Tests/obj/counterexamples"

# Prints a message to stderr and stops.
fail() {
  echo "$*" >&2
  exit 1
}

# The plugin's version, from its project file.
version() {
  sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJECT"
}

# True when the argument is a version in X.Y.Z.N form.
is_version() {
  [[ "$1" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]
}

# Points DALAMUD_HOME at the dev reference assemblies, which only plugin builds need, and stops without them.
require_dalamud() {
  export DALAMUD_HOME="${DALAMUD_HOME:-$HOME/Library/Application Support/XIV on Mac/dalamud/Hooks/dev}"
  if [ ! -f "$DALAMUD_HOME/Dalamud.dll" ]; then
    fail "Dalamud dev assemblies not found at: $DALAMUD_HOME"
  fi
}
