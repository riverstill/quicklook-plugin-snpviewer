#!/usr/bin/env pwsh
# Package the plugin into a .qlplugin (a zip) via `dotnet publish`.
# Publish (not `dotnet build` output) is used because it reliably stages
# all NuGet dependency assemblies (OxyPlot, ...) next to the plugin DLL,
# regardless of the custom OutputPath configured in the csproj.
#
# Usage: ./package.ps1 -Configuration Release -Platform AnyCPU
#
# Produces: QuickLook.Plugin.SnpViewer.qlplugin in the repo root, containing:
#   - QuickLook.Plugin.SnpViewer.dll
#   - OxyPlot*.dll (charting engine, shipped with the plugin)
#   - Translations.config
# Deliberately EXCLUDED: QuickLook.Common.dll (provided by the host
# QuickLook process), *.pdb, *.xml docs and build caches.

param(
    [string]$Configuration = "Release",
    [string]$Platform = "AnyCPU"
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "QuickLook.Plugin.SnpViewer/QuickLook.Plugin.SnpViewer.csproj"
$publishDir = Join-Path $PSScriptRoot "publish/SnpViewer"
$stageDir = Join-Path $PSScriptRoot "publish/stage"
$qlplugin = Join-Path $PSScriptRoot "QuickLook.Plugin.SnpViewer.qlplugin"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "'dotnet' is not on PATH. Install .NET SDK 6+ to package the plugin."
}

Write-Host "Publishing $project ($Configuration|$Platform) -> $publishDir ..."
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
dotnet publish $project -c $Configuration -p:Platform=$Platform -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$oxy = Get-ChildItem -Path $publishDir -Filter "OxyPlot*.dll" -ErrorAction SilentlyContinue
if (-not $oxy) { throw "OxyPlot assemblies missing from publish output at $publishDir - aborting packaging." }
Write-Host "Found charting assemblies:"
$oxy | ForEach-Object { Write-Host "  $($_.Name) ($($_.Length) bytes)" }

if (Test-Path $stageDir) { Remove-Item -Recurse -Force $stageDir }
New-Item -ItemType Directory -Path $stageDir | Out-Null

# Plugin DLL + third-party dependency DLLs, but NOT the host-provided QuickLook.Common.dll
Get-ChildItem -Path $publishDir -Filter "*.dll" |
    Where-Object { $_.Name -ne "QuickLook.Common.dll" } |
    Copy-Item -Destination $stageDir -Force

# Translations.config (i18n strings shown in the QuickLook UI)
$translations = Join-Path $publishDir "Translations.config"
if (-not (Test-Path $translations)) { throw "Translations.config missing from publish output at $publishDir" }
Copy-Item -Path $translations -Destination $stageDir -Force

Write-Host "Staged files:"
Get-ChildItem $stageDir | Select-Object Name, Length | Format-Table -AutoSize | Out-String | Write-Host

if (Test-Path $qlplugin) { Remove-Item -Force $qlplugin }
Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $qlplugin -Force
Write-Host "Packaged: $qlplugin"
Get-Item $qlplugin | Select-Object Name, Length | Format-Table -AutoSize | Out-String | Write-Host
