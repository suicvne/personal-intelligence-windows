#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
dotnet run --project src/Intel.Desktop -- "$@"
