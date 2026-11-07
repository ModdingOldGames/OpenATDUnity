#!/usr/bin/env bash
# Runs the built player windowed with an auto script and waits for it to exit.
# Usage: tools/build/run-shot.sh <width> <height> "<auto script>" [extra args...]
# Script waits ("wait N") count rendered frames (about 60 per second), not seconds.
MIRROR="${OPENAT_BUILD_MIRROR:-$LOCALAPPDATA/Temp/openat-build}"
W=$1; H=$2; SCRIPT=$3; shift 3
"$MIRROR/player/OpenAT.exe" -screen-fullscreen 0 -screen-width "$W" -screen-height "$H" \
  -logFile "$(cygpath -w "$MIRROR")/player.log" -openat-auto "$SCRIPT" "$@"
grep -E "^OpenAT|Exception|^MusicPlayer|error" "$MIRROR/player.log" | head -40
