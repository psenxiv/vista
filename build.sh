#!/usr/bin/env bash
set -euo pipefail
export DALAMUD_HOME="$HOME/Library/Application Support/XIV on Mac/dalamud/Hooks/dev"
if [ ! -f "$DALAMUD_HOME/Dalamud.dll" ]; then
  echo "Dalamud dev assemblies not found at: $DALAMUD_HOME" >&2
  exit 1
fi
dotnet build src/CinematicCam.Plugin/CinematicCam.Plugin.csproj -c Debug "$@"
