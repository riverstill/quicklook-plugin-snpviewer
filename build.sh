#!/usr/bin/env bash
# Convenience build script.
# Usage: ./build.sh [Configuration] [Platform]
#   Defaults: Release AnyCPU
#
# Requires one of:
#   - dotnet SDK 6.0+ on PATH
#   - MSBuild on PATH (e.g. from Visual Studio / Build Tools for VS)

set -euo pipefail

CONFIG="${1:-Release}"
PLATFORM="${2:-AnyCPU}"

if command -v dotnet >/dev/null 2>&1; then
  dotnet restore QuickLook.Plugin.SnpViewer.sln
  dotnet build QuickLook.Plugin.SnpViewer.sln \
    -c "$CONFIG" \
    -p:Platform="$PLATFORM"
else
  if ! command -v msbuild >/dev/null 2>&1; then
    echo "Neither 'dotnet' nor 'msbuild' is on PATH."
    echo "Install .NET SDK 6+ or Visual Studio 2017+ / Build Tools for Visual Studio."
    exit 1
  fi
  msbuild QuickLook.Plugin.SnpViewer.sln \
    /p:Configuration="$CONFIG" \
    /p:Platform="$PLATFORM"
fi
