#!/usr/bin/env bash
# Shared setup: the repo root and the plugin project. Dalamud is required only by the
# scripts that build the plugin, so they call require_dalamud; tests and versioning do not.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/Vista.Plugin/Vista.Plugin.csproj"
version() { sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJECT"; }

# Points DALAMUD_HOME at the dev reference assemblies and refuses to go on without them.
require_dalamud() {
  export DALAMUD_HOME="${DALAMUD_HOME:-$HOME/Library/Application Support/XIV on Mac/dalamud/Hooks/dev}"
  if [ ! -f "$DALAMUD_HOME/Dalamud.dll" ]; then
    echo "Dalamud dev assemblies not found at: $DALAMUD_HOME" >&2
    exit 1
  fi
}
