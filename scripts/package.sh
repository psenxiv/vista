#!/usr/bin/env bash
# Builds Release and packages it as CI does; prints where latest.zip landed.
source "$(dirname "$0")/env.sh"
dotnet build "$PROJECT" -c Release --no-incremental
echo "Packaged $(version): $ROOT/src/Vista.Plugin/bin/Release/Vista/latest.zip"
