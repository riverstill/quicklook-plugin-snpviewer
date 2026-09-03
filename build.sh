#!/usr/bin/env bash
# Convenience build script.
# Usage: ./build.sh [Configuration] [Platform]
#   Defaults: Release AnyCPU

set -euo pipefail

CONFIG="${1:-Release}"
PLATFORM="${2:-AnyCPU}"

if [[ ! -d QuickLook.Common/QuickLook.Common ]]; then
  echo "Submodule not initialised. Run: git submodule update --init --recursive"
  exit 1
fi

# Prefer dotnet SDK if available, else fall back to msbuild.
if command -v dotnet >/dev/null 2>&1; then
  dotnet build QuickLook.Plugin.SnpViewer.sln \
    -c "$CONFIG" \
    -p:Platform="$PLATFORM"
else
  if ! command -v msbuild >/dev/null 2>&1; then
    echo "Neither 'dotnet' nor 'msbuild' is on PATH."
    exit 1
  fi
  msbuild QuickLook.Plugin.SnpViewer.sln \
    /p:Configuration="$CONFIG" \
    /p:Platform="$PLATFORM"
fi
