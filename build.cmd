@echo off
REM Convenience build script (Windows).
REM Usage: build.cmd [Configuration] [Platform]
REM   Defaults: Release AnyCPU
REM
REM Requires one of:
REM   - .NET SDK 6+ on PATH
REM   - MSBuild (from Visual Studio or Build Tools for Visual Studio)

setlocal
set CONFIG=%1
if "%CONFIG%"=="" set CONFIG=Release
set PLATFORM=%2
if "%PLATFORM%"=="" set PLATFORM=AnyCPU

where dotnet >nul 2>nul
if %ERRORLEVEL%==0 (
  dotnet restore QuickLook.Plugin.SnpViewer.sln
  dotnet build QuickLook.Plugin.SnpViewer.sln -c %CONFIG% -p:Platform=%PLATFORM%
  exit /b %ERRORLEVEL%
)

where msbuild >nul 2>nul
if %ERRORLEVEL%==0 (
  msbuild QuickLook.Plugin.SnpViewer.sln /p:Configuration=%CONFIG% /p:Platform=%PLATFORM%
  exit /b %ERRORLEVEL%
)

echo Neither 'dotnet' nor 'msbuild' is on PATH.
echo Install .NET SDK 6+ or Visual Studio 2017+ / Build Tools for Visual Studio.
exit /b 1
