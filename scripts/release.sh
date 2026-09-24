#!/usr/bin/env bash
# Tags the current version for a channel and pushes the tag, which runs the Release workflow.
# scripts/release.sh prod   -> prod-vX.Y.Z.N, a release for everyone
# scripts/release.sh test   -> test-vX.Y.Z.N, a build for opted-in testers
source "$(dirname "$0")/env.sh"
cd "$ROOT"

channel="${1:?usage: release.sh prod|test}"
if [ "$channel" != prod ] && [ "$channel" != test ]; then
  fail "Channel must be prod or test, got $channel"
fi

# The tree must be clean, on main, and in step with origin.
if [ -n "$(git status --porcelain --untracked-files=no)" ]; then
  fail "Commit or discard changes first."
fi
if [ "$(git rev-parse --abbrev-ref HEAD)" != main ]; then
  fail "Release from main."
fi
git fetch -q origin main --tags
if [ "$(git rev-parse HEAD)" != "$(git rev-parse origin/main)" ]; then
  fail "main isn't in step with origin/main; push or pull first."
fi

# The version must be set, have a changelog section, and not be tagged already.
v="$(version)"
if ! is_version "$v"; then
  fail "Version must be X.Y.Z.N, got $v; run make bump."
fi
if ! grep -qx "## $v" CHANGELOG.md; then
  fail "CHANGELOG.md has no '## $v' section; make bump names the pending one."
fi
tag="$channel-v$v"
if git rev-parse -q --verify "refs/tags/$tag" > /dev/null; then
  fail "Tag $tag already exists."
fi

"$ROOT/scripts/verify.sh" --check

read -r -p "Push $tag and publish it? [y/N] " answer
if [ "$answer" != y ]; then
  fail "Stopped."
fi
git tag "$tag"
git push origin "$tag"
echo "Pushed $tag. Watch it at https://github.com/psenxiv/vista/actions"
