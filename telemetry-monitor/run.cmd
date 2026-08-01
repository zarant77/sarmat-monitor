@echo off
setlocal
cd /d "%~dp0"

if not exist "config.json" (
  copy /Y "config.example.json" "config.json" >nul
  echo Created config.json from config.example.json
)

dotnet run --project ".\src\Sarmat.TelemetryMonitor\Sarmat.TelemetryMonitor.csproj"
exit /b %errorlevel%
