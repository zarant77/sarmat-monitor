@echo off
setlocal
if "%~1"=="" (
  echo Usage: build.bat "C:\Program Files (x86)\Mission Planner"
  exit /b 2
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -MissionPlannerPath "%~1" -Configuration Release
exit /b %ERRORLEVEL%
