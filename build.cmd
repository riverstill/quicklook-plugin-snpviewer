@echo off
REM Convenience build script (Windows).
REM Usage: build.cmd [Configuration] [Platform]
REM   Defaults: Release AnyCPU

setlocal
set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=Release
set PLATFORM=%2
if "%PLATFORM%"=="" set PLATFORM=AnyCPU

if not exist QuickLook.Common\QuickLook.Common (
  echo Submodule not initialised. Run: git submodule update --init --recursive
  exit /b 1
)

where dotnet >nul 2>nul
if %ERRORLEVEL%==0 (
  dotnet build QuickLook.Plugin.SnpViewer.sln -c %CONFIG% -p:Platform=%PLATFORM%
  exit /b %ERRORLEVEL%
)

where msbuild >nul 2>nul
if %ERRORLEVEL%==0 (
  msbuild QuickLook.Plugin.SnpViewer.sln /p:Configuration=%CONFIG% /p:Platform=%PLATFORM%
  exit /b %ERRORLEVEL%
)

echo Neither 'dotnet' nor 'msbuild' is on PATH.
exit /b 1
