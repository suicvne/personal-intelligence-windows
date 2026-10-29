#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
INTEL_WINE_BIN="${INTEL_WINE_BIN:-$HOME/Library/Application Support/com.isaacmarovitz.Whisky/Libraries/Wine/bin}"
export WINEPREFIX="$INTEL_ROOT/.whisky/intel-prototype"
export WINEDEBUG="${WINEDEBUG:--all}"
if [[ ! -x "$INTEL_WINE_BIN/wine64" ]]; then echo 'Whisky wine64 is missing. Set INTEL_WINE_BIN to its bin directory.' >&2; exit 1; fi
if [[ ! -f "$WINEPREFIX/system.reg" ]]; then
  mkdir -p "$WINEPREFIX"
  "$INTEL_WINE_BIN/wine64" wineboot -u
  "$INTEL_WINE_BIN/wine64" reg add 'HKCU\Software\Wine' /v Version /t REG_SZ /d win10 /f
fi
exec "$INTEL_WINE_BIN/wine64" "$INTEL_ROOT/artifacts/win-x64/Intel.exe" "$@"
