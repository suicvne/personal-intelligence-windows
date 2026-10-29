#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
dotnet publish src/Intel.Desktop -c Release -r win-x64 --self-contained true -p:PublishTrimmed=false -p:PublishReadyToRun=false -o artifacts/win-x64
python3 - <<'PY'
import shutil
shutil.make_archive('artifacts/Intel-win-x64', 'zip', 'artifacts/win-x64')
PY
