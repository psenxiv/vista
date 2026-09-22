#!/usr/bin/env bash
# Tags the current version and pushes the tag, which runs the Release workflow.
# scripts/release.sh          -> vX.Y.Z, published and written to repo.json
# scripts/release.sh rc 1     -> vX.Y.Z-rc.1, a prerelease only
source "$(dirname "$0")/env.sh"
cd "$ROOT"
[ -z "$(git status --porcelain --untracked-files=no)" ] || { echo "Commit or discard changes first." >&2; exit 1; }
[ "$(git rev-parse --abbrev-ref HEAD)" = "main" ] || { echo "Release from main." >&2; exit 1; }
git fetch -q origin main
[ "$(git rev-parse HEAD)" = "$(git rev-parse origin/main)" ] || { echo "main isn't in step with origin/main; push or pull first." >&2; exit 1; }
tag="v$(version)"
if [ "${1:-}" = "rc" ]; then tag="$tag-rc.${2:?usage: release.sh rc N}"; fi
git rev-parse -q --verify "refs/tags/$tag" >/dev/null && { echo "Tag $tag already exists." >&2; exit 1; }
"$ROOT/scripts/test.sh"
read -r -p "Push $tag and publish it? [y/N] " answer
[ "$answer" = "y" ] || { echo "Stopped."; exit 1; }
git tag "$tag"
git push origin "$tag"
echo "Pushed $tag. Watch it at https://github.com/psenxiv/vista/actions"
