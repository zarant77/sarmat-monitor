@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT=%~dp0"
set "MP_PATH=%~1"
set "PRODUCT_VERSION=%~2"

if "%MP_PATH%"=="" set "MP_PATH=C:\Program Files (x86)\Mission Planner"
if "%PRODUCT_VERSION%"=="" set "PRODUCT_VERSION=1.5.3"

if not exist "!MP_PATH!\MissionPlanner.exe" (
  echo ERROR: MissionPlanner.exe was not found in:
  echo   !MP_PATH!
  echo.
  echo Usage: build.bat [MissionPlannerPath] [ProductVersion]
  echo Example: build.bat "C:\Program Files (x86)\Mission Planner" 1.5.3
  exit /b 2
)

echo [1/2] Building Sarmat Telemetry...
call "!ROOT!telemetry-plugin\scripts\build.bat" "!MP_PATH!"
if errorlevel 1 goto :failed

echo [2/2] Building MSI installer...
dotnet build "!ROOT!installer\SarmatPlugins.Installer.wixproj" --configuration Release "-p:ProductVersion=!PRODUCT_VERSION!"
if errorlevel 1 goto :failed

echo.
echo BUILD SUCCEEDED
echo Plugin DLL staging directories:
echo   !ROOT!telemetry-plugin\dist\plugins
echo Unified installer:
echo   !ROOT!artifacts\SarmatPlugins-!PRODUCT_VERSION!.msi
exit /b 0

:failed
echo.
echo BUILD FAILED with exit code !ERRORLEVEL!.
exit /b !ERRORLEVEL!
