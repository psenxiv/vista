#!/usr/bin/env bash
# Runs only the movement sweep and writes its report to tests/Vista.Tests/obj/sweep-reports/sweep-<date>-<time>.md.
source "$(dirname "$0")/env.sh"
VISTA_SWEEP_REPORT=1 "$ROOT/scripts/test.sh" --filter "FullyQualifiedName~MovementSweepTests"
