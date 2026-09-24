#!/usr/bin/env bash
# Rewrites the camera regression scene file from the cases in the tests.
source "$(dirname "$0")/env.sh"
VISTA_WRITE_REGRESSION_SCENE=1 "$ROOT/scripts/test.sh" --filter "FullyQualifiedName~RegressionSceneTests.TheSceneFileIsCurrent"
