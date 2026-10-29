#!/usr/bin/env bash
# Source from scripts; keep SDK and NuGet caches local to this checkout.
INTEL_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_CLI_HOME="$INTEL_ROOT/artifacts/dotnet-home"
export NUGET_PACKAGES="$INTEL_ROOT/artifacts/nuget"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
cd "$INTEL_ROOT"
