#!/usr/bin/env bash
# Serve the checklist page and open it. Ctrl+C to stop.
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
port="${1:-8765}"
( sleep 1; open "http://localhost:$port/" ) &
exec python3 -m http.server "$port" -d "$here"
