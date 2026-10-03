#!/usr/bin/env bash
# Builds KesslerSymptoms.dll against the local KSP install into ./Plugins.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
KSP_ROOT="${KSP_ROOT:-$(cd "$HERE/../.." && pwd)}"
MANAGED="$KSP_ROOT/KSP_Data/Managed"
GAMEDATA="$KSP_ROOT/GameData"

REFS=(
  "$MANAGED/Assembly-CSharp.dll"
  "$MANAGED/Assembly-CSharp-firstpass.dll"
  "$MANAGED/UnityEngine.dll"
  "$MANAGED/UnityEngine.CoreModule.dll"
  "$MANAGED/UnityEngine.IMGUIModule.dll"
  "$MANAGED/UnityEngine.AudioModule.dll"
  "$MANAGED/UnityEngine.PhysicsModule.dll"
  "$MANAGED/UnityEngine.UI.dll"
  "$GAMEDATA/001_ToolbarControl/Plugins/ToolbarControl.dll"
  "$GAMEDATA/000_ClickThroughBlocker/Plugins/ClickThroughBlocker.dll"
)

REF_ARGS=()
for r in "${REFS[@]}"; do
  [ -f "$r" ] || { echo "error: missing reference: $r" >&2; exit 1; }
  REF_ARGS+=("-r:$r")
done

mapfile -t SOURCES < <(find "$HERE/Source" -name '*.cs' | sort)
mkdir -p "$HERE/Plugins"

echo "Compiling ${#SOURCES[@]} files..."
mcs -target:library -out:"$HERE/Plugins/KesslerSymptoms.dll" \
  -langversion:6 -nostdlib -noconfig -optimize -debug:portable \
  -r:"$MANAGED/mscorlib.dll" -r:"$MANAGED/System.dll" -r:"$MANAGED/System.Core.dll" \
  "${REF_ARGS[@]}" -warn:4 "${SOURCES[@]}"
echo "Built -> Plugins/KesslerSymptoms.dll"
