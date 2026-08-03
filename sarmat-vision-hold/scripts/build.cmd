@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -MissionPlannerPath "%~1" -Configuration Release
