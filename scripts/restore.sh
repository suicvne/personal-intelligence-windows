#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
dotnet restore Intel.sln --locked-mode
