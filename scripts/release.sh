#!/usr/bin/env bash
# Tags the current version for a channel and pushes the tag, which runs the Release workflow.
# scripts/release.sh prod   -> prod-vX.Y.Z.N, a release for everyone
# scripts/release.sh test   -> test-vX.Y.Z.N, a build for opted-in testers
source "$(dirname "$0")/env.sh"
cd "$ROOT"
channel="${1:?usage: release.sh prod|test}"
[[ "$channel" = prod || "$channel" = test ]] || { echo "Channel must be prod or test, got $channel" >&2; exit 1; }
[ -z "$(git status --porcelain --untracked-files=no)" ] || { echo "Commit or discard changes first." >&2; exit 1; }
[ "$(git rev-parse --abbrev-ref HEAD)" = "main" ] || { echo "Release from main." >&2; exit 1; }
git fetch -q origin main --tags
[ "$(git rev-parse HEAD)" = "$(git rev-parse origin/main)" ] || { echo "main isn't in step with origin/main; push or pull first." >&2; exit 1; }
v="$(version)"
[[ "$v" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Version must be X.Y.Z.N, got $v; run make bump." >&2; exit 1; }
grep -qx "## $v" CHANGELOG.md || { echo "CHANGELOG.md has no '## $v' section." >&2; exit 1; }
tag="$channel-v$v"
git rev-parse -q --verify "refs/tags/$tag" >/dev/null && { echo "Tag $tag already exists." >&2; exit 1; }
"$ROOT/scripts/test.sh"
read -r -p "Push $tag and publish it? [y/N] " answer
[ "$answer" = "y" ] || { echo "Stopped."; exit 1; }
git tag "$tag"
git push origin "$tag"
echo "Pushed $tag. Watch it at https://github.com/psenxiv/vista/actions"
