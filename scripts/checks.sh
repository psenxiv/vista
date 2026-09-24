#!/usr/bin/env bash
# Serves the in-game checklist page at http://localhost:3000, or on the port given: scripts/checks.sh 8080
source "$(dirname "$0")/env.sh"
port="${1:-3000}"
[[ "$port" =~ ^[1-9][0-9]*$ ]] || fail "Usage: scripts/checks.sh [port]"
command -v serve > /dev/null || fail "serve not found: npm install -g serve"
exec serve --listen "tcp://127.0.0.1:$port" --no-clipboard "$ROOT/tests/in-game"
