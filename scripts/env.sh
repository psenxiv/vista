#!/usr/bin/env bash
# Shared setup: the repo root, the plugin project, and Dalamud's reference assemblies.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/Vista.Plugin/Vista.Plugin.csproj"
export DALAMUD_HOME="${DALAMUD_HOME:-$HOME/Library/Application Support/XIV on Mac/dalamud/Hooks/dev}"
if [ ! -f "$DALAMUD_HOME/Dalamud.dll" ]; then
  echo "Dalamud dev assemblies not found at: $DALAMUD_HOME" >&2
  exit 1
fi
version() { sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$PROJECT"; }
