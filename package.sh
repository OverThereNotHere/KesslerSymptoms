#!/usr/bin/env bash
# Builds the plugin fresh and zips a player-ready release into ./dist:
#   GameData/KesslerSymptoms/{Plugins/KesslerSymptoms.dll, KesslerDamage.cfg, Sounds, Textures,
#   KesslerSymptoms.version, README.md, LICENSE}
# Source, build scripts, local docs and settings.cfg are left out.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$HERE"

VERSION="$(python3 -c 'import json; v=json.load(open("KesslerSymptoms.version"))["VERSION"]; print("%d.%d.%d" % (v["MAJOR"], v["MINOR"], v["PATCH"]))')"
./build.sh

STAGE="$(mktemp -d)"
trap 'rm -rf "$STAGE"' EXIT
MOD="$STAGE/GameData/KesslerSymptoms"
mkdir -p "$MOD/Plugins"
cp Plugins/KesslerSymptoms.dll "$MOD/Plugins/"
cp -r Sounds Textures "$MOD/"
cp KesslerDamage.cfg KesslerSymptoms.version README.md LICENSE "$MOD/"

mkdir -p dist
ZIP="$HERE/dist/KesslerSymptoms-$VERSION.zip"
rm -f "$ZIP"
(cd "$STAGE" && zip -qr -X "$ZIP" GameData)
echo "Packaged -> dist/$(basename "$ZIP")"
