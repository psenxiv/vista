#!/usr/bin/env bash
# Sets the plugin version, names the pending changelog section after it, and commits both: scripts/bump.sh 0.6.0.1
source "$(dirname "$0")/env.sh"
new="${1:?usage: bump.sh X.Y.Z.N}"
[[ "$new" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Version must be X.Y.Z.N, got $new" >&2; exit 1; }
sed -i '' "s:<Version>.*</Version>:<Version>$new</Version>:" "$PROJECT"
sed -i '' "s/^## X\.Y\.Z\.N\$/## $new/" "$ROOT/CHANGELOG.md"
git -C "$ROOT" add "$PROJECT" "$ROOT/CHANGELOG.md"
git -C "$ROOT" commit -m "chore(release) bump to $new"
echo "Version is now $new"
