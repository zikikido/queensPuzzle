#!/bin/sh
# Downloads the Firebase UPM tarballs referenced by Packages/manifest.json.
# Run from anywhere; files land next to this script. Bump VERSION together with the manifest.
VERSION=13.13.0
cd "$(dirname "$0")" || exit 1
for p in app remote-config analytics crashlytics; do
    f="com.google.firebase.$p-$VERSION.tgz"
    echo "downloading $f"
    curl -sfL -o "$f" "https://dl.google.com/games/registry/unity/com.google.firebase.$p/$f" || { echo "FAILED $f"; exit 1; }
done
echo "done"
