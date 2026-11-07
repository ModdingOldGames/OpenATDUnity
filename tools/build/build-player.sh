#!/usr/bin/env bash
# Builds a Windows player from a mirror of the project so it works while the editor has the
# project open. Usage: tools/build/build-player.sh [outdir]
set -e
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
MIRROR="${OPENAT_BUILD_MIRROR:-$LOCALAPPDATA/Temp/openat-build}"
OUT="${1:-$MIRROR/player}"
UNITY="${UNITY_EXE:-/c/Program Files/Unity/Hub/Editor/6000.4.3f1/Editor/Unity.exe}"
mkdir -p "$MIRROR"
for d in Assets Packages ProjectSettings; do
  rm -rf "$MIRROR/$d"; cp -r "$ROOT/$d" "$MIRROR/$d"
done
build() {
  "$UNITY" -batchmode -quit -projectPath "$(cygpath -w "$MIRROR")" \
    -buildWindows64Player "$(cygpath -w "$OUT")/OpenAT.exe" -logFile "$(cygpath -w "$MIRROR")/build.log" || true
}
build
# The mirror's package cache sometimes ends up half-registered (package types such as
# UnityEngine.InputSystem missing); clearing it and building again fixes that.
if grep -qE "namespace name '(InputSystem|UI)' does not exist" "$MIRROR/build.log"; then
  echo "build-player: package cache broken, clearing and rebuilding"
  rm -rf "$MIRROR/Library/PackageCache" "$MIRROR/Library/PackageManager" "$MIRROR/Library/ScriptAssemblies" "$MIRROR/Library/Bee"
  build
fi
grep -E "error CS|Build succeeded|Build Finished|Exiting batchmode" "$MIRROR/build.log" | tail -20
ls "$OUT"/OpenAT.exe
