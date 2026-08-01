@echo off
setlocal
cd /d "%~dp0"
call corepack pnpm install --prod --frozen-lockfile
if errorlevel 1 exit /b %errorlevel%
echo Telemetry Aggregator dependencies installed successfully.
