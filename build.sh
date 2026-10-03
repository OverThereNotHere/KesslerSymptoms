#!/usr/bin/env bash
# Builds KesslerSymptoms.dll against the local KSP install into ./Plugins.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
KSP_ROOT="${KSP_ROOT:-$(cd "$HERE/../.." && pwd)}"
# Managed assemblies live in a different folder per platform: Linux, Windows, macOS.
MANAGED=""
for d in "$KSP_ROOT/KSP_Data/Managed" "$KSP_ROOT/KSP_x64_Data/Managed" "$KSP_ROOT/KSP.app/Contents/Resources/Data/Managed"; do
  [ -d "$d" ] && { MANAGED="$d"; break; }
done
[ -n "$MANAGED" ] || { echo "error: no KSP Managed folder under $KSP_ROOT (set KSP_ROOT)" >&2; exit 1; }
GAMEDATA="$KSP_ROOT/GameData"

REFS=(
  "$MANAGED/Assembly-CSharp.dll"
  "$MANAGED/Assembly-CSharp-firstpass.dll"
  "$MANAGED/UnityEngine.dll"
  "$MANAGED/UnityEngine.CoreModule.dll"
  "$MANAGED/UnityEngine.IMGUIModule.dll"
  "$MANAGED/UnityEngine.TextRenderingModule.dll"
  "$MANAGED/UnityEngine.AudioModule.dll"
  "$MANAGED/UnityEngine.PhysicsModule.dll"
  "$MANAGED/UnityEngine.ParticleSystemModule.dll"
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
