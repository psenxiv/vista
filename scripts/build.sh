#!/usr/bin/env bash
# Builds the plugin in Debug for loading as a dev plugin. Extra arguments go to dotnet build.
source "$(dirname "$0")/env.sh"
require_dalamud
dotnet build "$PROJECT" -c Debug "$@"
