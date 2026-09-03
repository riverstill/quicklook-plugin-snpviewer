#!/usr/bin/env pwsh
# Package the build output into a .qlplugin (a zip).
# Usage: ./package.ps1 -Configuration Release -Platform AnyCPU

param(
    [string]$Configuration = "Release",
    [string]$Platform = "AnyCPU"
)

$ErrorActionPreference = "Stop"

$binPath = "Build/$Configuration/QuickLook.Plugin/QuickLook.Plugin.SnpViewer"
if (-not (Test-Path $binPath)) {
    Write-Error "Build output not found at $binPath. Run build first."
}

$qlplugin = "QuickLook.Plugin.SnpViewer.qlplugin"
if (Test-Path $qlplugin) {
    Remove-Item -Recurse -Force $qlplugin
}

Compress-Archive -Path "$binPath/*" -DestinationPath $qlplugin -Force
Write-Host "Packaged: $qlplugin"
