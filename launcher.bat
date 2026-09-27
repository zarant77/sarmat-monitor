@echo off
setlocal DisableDelayedExpansion
set "SARMAT_LAUNCHER_FILE=%~f0"
set "SARMAT_LAUNCHER_ACTION=%~1"
set "SARMAT_LAUNCHER_ARGUMENT=%~2"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$text = [IO.File]::ReadAllText($env:SARMAT_LAUNCHER_FILE); & ([scriptblock]::Create(($text -split '(?m)^# POWERSHELL\r?$', 2)[1]))"
set "LAUNCHER_EXIT=%ERRORLEVEL%"
if not "%LAUNCHER_EXIT%"=="0" if "%~1"=="" pause
exit /b %LAUNCHER_EXIT%
# POWERSHELL
# Single-file Windows launcher. The batch header runs this PowerShell section.
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding
$OutputEncoding = [Console]::OutputEncoding
$projectRoot = Split-Path -Parent $env:SARMAT_LAUNCHER_FILE
$androidRoot = Join-Path $projectRoot 'sarmat-crew'
$interactive = [string]::IsNullOrWhiteSpace($env:SARMAT_LAUNCHER_ACTION)

function Invoke-Checked {
    param([string]$Executable, [string[]]$Arguments)
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Command failed (exit $LASTEXITCODE): $Executable" }
}

function Initialize-Android {
    $jdkCandidates = @((Join-Path $androidRoot '.tools\jdk-17'),
        (Join-Path $projectRoot '.tools\jdk-17'), $env:JAVA_HOME)
    $javaCommand = Get-Command java.exe -ErrorAction SilentlyContinue
    if ($javaCommand) { $jdkCandidates += Split-Path (Split-Path $javaCommand.Source) }
    $jdkCandidates += @("$env:ProgramFiles\Android\Android Studio\jbr",
        "$env:LOCALAPPDATA\Programs\Android Studio\jbr")
    foreach ($jdkRoot in @("$env:USERPROFILE\.jdks", "$env:ProgramFiles\Eclipse Adoptium",
        "$env:ProgramFiles\Java", "$env:ProgramFiles\Microsoft")) {
        if (Test-Path -LiteralPath $jdkRoot) {
            $jdkCandidates += @(Get-ChildItem -LiteralPath $jdkRoot -Directory | Select-Object -ExpandProperty FullName)
        }
    }
    $selectedJdk = $null
    foreach ($candidate in ($jdkCandidates | Where-Object { $_ } | Select-Object -Unique)) {
        $releaseFile = Join-Path $candidate 'release'
        if ((Test-Path -LiteralPath $releaseFile) -and (Test-Path -LiteralPath (Join-Path $candidate 'bin\javac.exe'))) {
            $version = Get-Content -LiteralPath $releaseFile | Where-Object { $_ -match '^JAVA_VERSION=' }
            if ($version -match '^JAVA_VERSION="(17|21)(?:\.|"|\+)') { $selectedJdk = $candidate; break }
        }
    }
    if (-not $selectedJdk) { throw 'Install JDK 17 or 21 and set JAVA_HOME. Java 25 is not supported by this Android build.' }
    $env:JAVA_HOME = $selectedJdk

    $sdkCandidates = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT)
    $properties = Join-Path $androidRoot 'local.properties'
    if (Test-Path -LiteralPath $properties) {
        foreach ($line in (Get-Content -LiteralPath $properties)) {
            if ($line -match '^\s*sdk\.dir\s*=\s*(.+)$') {
                $sdkCandidates += $Matches[1].Trim().Replace('\:', ':').Replace('\\', '\')
            }
        }
    }
    $sdkCandidates += "$env:LOCALAPPDATA\Android\Sdk"
    $sdk = $sdkCandidates | Where-Object {
        $_ -and (Test-Path -LiteralPath (Join-Path $_ 'platforms\android-36\android.jar'))
    } | Select-Object -First 1
    if (-not $sdk) { throw 'Install Android SDK Platform 36 using Android Studio SDK Manager, then set ANDROID_HOME if needed.' }
    $env:ANDROID_HOME = $sdk
    $env:ANDROID_SDK_ROOT = $sdk
    Write-Host "Java: $selectedJdk"
    Write-Host "Android SDK: $sdk"
}

function Build-Android {
    Initialize-Android
    Push-Location $androidRoot
    try {
        Invoke-Checked -Executable (Join-Path $androidRoot 'gradlew.bat') -Arguments @("-Dorg.gradle.java.home=$env:JAVA_HOME", ':app:assembleDebug')
    } finally { Pop-Location }
    $apk = Join-Path $androidRoot 'app\build\outputs\apk\debug\app-debug.apk'
    if (-not (Test-Path -LiteralPath $apk)) { throw "APK not found: $apk" }
    Write-Host "APK ready: $apk" -ForegroundColor Green
}

function Find-MissionPlanner {
    param([string]$Requested)
    if ($Requested) {
        if (-not (Test-Path -LiteralPath (Join-Path $Requested 'MissionPlanner.exe'))) { throw "MissionPlanner.exe not found in: $Requested" }
        return (Resolve-Path -LiteralPath $Requested).Path
    }
    foreach ($candidate in @($env:MISSION_PLANNER_PATH, "${env:ProgramFiles(x86)}\Mission Planner", "$env:ProgramFiles\Mission Planner")) {
        if ($candidate -and (Test-Path -LiteralPath (Join-Path $candidate 'MissionPlanner.exe'))) { return $candidate }
    }
    if (-not $interactive) { throw 'Mission Planner not found. Pass its directory as the second argument.' }
    $selected = (Read-Host 'Path to the folder containing MissionPlanner.exe').Trim().Trim('"')
    if (-not $selected) { throw 'Mission Planner directory was not selected.' }
    return Find-MissionPlanner $selected
}

function Build-Plugin {
    param([string]$Requested)
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install .NET SDK (8 or newer), then reopen the launcher.' }
    $mp = Find-MissionPlanner $Requested
    Write-Host "Mission Planner: $mp"
    # Reuse the existing build/test/package workflow. This does not install the DLL.
    & (Join-Path $projectRoot 'telemetry-plugin\scripts\build.ps1') -MissionPlannerPath $mp -Configuration Release
    Write-Host "Plugin ready: $(Join-Path $projectRoot 'telemetry-plugin\dist\plugins\SarmatTelemetry.dll')" -ForegroundColor Green
}

function Install-Android {
    param([string]$Serial)
    Initialize-Android
    $adb = Join-Path $env:ANDROID_HOME 'platform-tools\adb.exe'
    if (-not (Test-Path -LiteralPath $adb)) { throw 'Install Android SDK Platform-Tools using SDK Manager.' }
    Invoke-Checked -Executable $adb -Arguments @('start-server')
    $deviceLines = & $adb devices -l
    if ($LASTEXITCODE -ne 0) { throw 'Could not list Android devices.' }
    $devices = @($deviceLines | ForEach-Object { if ($_ -match '^(\S+)\s+device(?:\s|$)') { $Matches[1] } })
    if (-not $Serial -and $devices.Count -eq 1) { $Serial = $devices[0] }
    if (-not $Serial -and $devices.Count -gt 1 -and $interactive) {
        $deviceLines | Write-Host
        $Serial = Read-Host 'Device serial'
    }
    if (-not $Serial -or $Serial -notin $devices) {
        $deviceLines | Write-Host
        throw 'Connect and authorize a USB-debugging device. With multiple devices: launcher.bat android-install DEVICE_SERIAL'
    }
    Build-Android
    Invoke-Checked -Executable $adb -Arguments @('-s', $Serial, 'install', '-r', (Join-Path $androidRoot 'app\build\outputs\apk\debug\app-debug.apk'))
    $output = & $adb -s $Serial shell am start -W -n 'com.sarmat.crew/.MainActivity' 2>&1
    $code = $LASTEXITCODE
    $output | Write-Host
    if ($code -ne 0 -or ($output -match 'Error:|Exception')) { throw 'APK installed, but the application could not be launched.' }
    Write-Host 'Sarmat Crew installed and running.' -ForegroundColor Green
}

function Invoke-Action {
    param([string]$Action, [string]$Argument)
    switch ($Action) {
        'android' { Build-Android }
        'plugin' { Build-Plugin $Argument }
        'all' {
            $failures = @()
            try { Build-Plugin $Argument } catch { $failures += "Plugin: $($_.Exception.Message)"; Write-Host $failures[-1] -ForegroundColor Red }
            try { Build-Android } catch { $failures += "Android: $($_.Exception.Message)"; Write-Host $failures[-1] -ForegroundColor Red }
            if ($failures.Count) { throw ($failures -join [Environment]::NewLine) }
        }
        'android-install' { Install-Android $Argument }
        'help' {
            Write-Host 'launcher.bat                            Interactive menu'
            Write-Host 'launcher.bat android                    Build debug APK (no phone needed)'
            Write-Host 'launcher.bat plugin [MissionPlannerDir]  Build Release DLL and run tests'
            Write-Host 'launcher.bat all [MissionPlannerDir]     Build both components'
            Write-Host 'launcher.bat android-install [serial]    Build, install and launch Android'
        }
        default { throw "Unknown action: $Action. Use launcher.bat help" }
    }
}

if (-not $interactive) {
    try { Invoke-Action $env:SARMAT_LAUNCHER_ACTION $env:SARMAT_LAUNCHER_ARGUMENT; exit 0 }
    catch { Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red; exit 1 }
}
while ($true) {
    Write-Host ''
    Write-Host 'SARMAT - WINDOWS LAUNCHER' -ForegroundColor Cyan
    Write-Host '  1. Build Sarmat Crew (debug APK)'
    Write-Host '  2. Build Mission Planner plugin (Release DLL + tests)'
    Write-Host '  3. Build both'
    Write-Host '  4. Build and install Sarmat Crew on a phone'
    Write-Host '  0. Exit'
    $choice = Read-Host 'Select action'
    if ($choice -eq '0' -or $null -eq $choice) { exit 0 }
    $action = switch ($choice) { '1' { 'android' }; '2' { 'plugin' }; '3' { 'all' }; '4' { 'android-install' }; default { $null } }
    if (-not $action) { Write-Host 'Select 0, 1, 2, 3 or 4.'; continue }
    try { Invoke-Action $action ''; Write-Host 'Done.' -ForegroundColor Green }
    catch { Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red }
    [void](Read-Host 'Press Enter to return to the menu')
}
