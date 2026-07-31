@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -MissionPlannerPath "C:\Program Files (x86)\Mission Planner"
set "SARMAT_EXIT_CODE=%ERRORLEVEL%"
if not "%SARMAT_EXIT_CODE%"=="0" (
    echo.
    echo Sarmat Plugin installation failed with exit code %SARMAT_EXIT_CODE%.
    pause
)
exit /b %SARMAT_EXIT_CODE%
