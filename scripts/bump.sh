#!/usr/bin/env bash
# Sets the plugin version and commits it: scripts/bump.sh 0.6.0.1
source "$(dirname "$0")/env.sh"
new="${1:?usage: bump.sh X.Y.Z.N}"
[[ "$new" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Version must be X.Y.Z.N, got $new" >&2; exit 1; }
sed -i '' "s:<Version>.*</Version>:<Version>$new</Version>:" "$PROJECT"
git -C "$ROOT" add "$PROJECT"
git -C "$ROOT" commit -m "chore(release) bump to $new"
echo "Version is now $new"
