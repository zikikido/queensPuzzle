#!/usr/bin/env bash
# Run once after a fresh clone.
# Fetches the Assets/kidogamesCode submodule with ONLY the Common folder
# checked out (sparse checkout + blob filter, so other folders are never downloaded).
set -euo pipefail
# Script lives in SetupProjectScript/ — work from the repo root
cd "$(dirname "$0")/.."

SUB_PATH="Assets/kidogamesCode"
SUB_URL="git@github.com:zikikido/kidogamesCode.git"

# Commit the superproject pins the submodule to
COMMIT=$(git ls-tree HEAD "$SUB_PATH" | awk '{print $3}')

if [ -e "$SUB_PATH/.git" ]; then
    echo "Submodule already initialized — syncing to pinned commit $COMMIT"
    git -C "$SUB_PATH" fetch origin "$COMMIT" || true
    git -C "$SUB_PATH" checkout "$COMMIT"
    exit 0
fi

git submodule init
git clone --filter=blob:none --no-checkout "$SUB_URL" "$SUB_PATH"
git -C "$SUB_PATH" sparse-checkout set --no-cone '/Common/' '/Common.meta' '/.gitignore'
git -C "$SUB_PATH" fetch origin "$COMMIT" || true
git -C "$SUB_PATH" checkout "$COMMIT"
git submodule absorbgitdirs "$SUB_PATH"

echo "Done: $SUB_PATH at $COMMIT (Common folder only)"
