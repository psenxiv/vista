#!/usr/bin/env bash
# Builds Release and packages it as CI does; prints where latest.zip landed.
source "$(dirname "$0")/env.sh"
require_dalamud
dotnet build "$PROJECT" -c Release --no-incremental

# The self-test is for developer builds only. Its types and strings all carry the word, so any trace of it,
# in names (UTF-8) or string literals (UTF-16), means Debug-only code reached Release.
bin="$ROOT/src/Vista.Plugin/bin/Release"
for dll in Vista.dll Vista.Core.dll; do
  if perl -0777 -ne 'exit(/s\0?e\0?l\0?f\0?t\0?e\0?s\0?t/i ? 0 : 1)' "$bin/$dll"; then
    rm -f "$bin/Vista/latest.zip"
    fail "$dll contains self-test code; it must stay inside #if DEBUG."
  fi
done

echo "Packaged $(version): $bin/Vista/latest.zip"
