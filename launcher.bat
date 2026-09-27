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
    Run-Gradle -Tasks @(':app:assembleDebug')
    Write-Host 'Android debug build completed:' -ForegroundColor Green
    Show-AndroidArtifacts debug
}

function Run-Gradle {
    param([string[]]$Tasks)
    Initialize-Android
    Push-Location $androidRoot
    try {
        Invoke-Checked -Executable (Join-Path $androidRoot 'gradlew.bat') -Arguments (@("-Dorg.gradle.java.home=$env:JAVA_HOME") + $Tasks)
    } finally { Pop-Location }
}

function Run-Web {
    param([string[]]$Arguments)
    $npm = Get-Command npm.cmd -ErrorAction Stop
    Push-Location $projectRoot
    try { Invoke-Checked -Executable $npm.Source -Arguments $Arguments }
    finally { Pop-Location }
}

function Show-AndroidArtifacts {
    param([string]$Variant)
    $outputRoot = Join-Path $androidRoot 'app\build\outputs'
    $files = @(if (Test-Path -LiteralPath $outputRoot) {
        Get-ChildItem -LiteralPath $outputRoot -Recurse -File | Where-Object {
            $_.Extension -in @('.apk', '.aab') -and $_.FullName -match "[\\/]$Variant[\\/]"
        } | Sort-Object FullName
    })
    if ($files.Count) { $files | ForEach-Object { Write-Host "  $($_.FullName)" } }
    else { Write-Host '  No artifacts found.' }
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
    switch -CaseSensitive -Regex ($Action) {
        '^dev$' { Run-Web -Arguments @('run', 'dev'); break }
        '^build$' { Run-Web -Arguments @('run', 'build'); break }
        '^(android-release|release)$' {
            Run-Gradle -Tasks @(':app:assembleRelease', ':app:bundleRelease')
            Write-Host 'Android release build completed:' -ForegroundColor Green
            Show-AndroidArtifacts release
            Write-Host 'Note: release APK/AAB artifacts are unsigned unless a signingConfig is configured.'
            break
        }
        '^(android-debug|debug)$' { Build-Android; break }
        '^(android-install|install-android)$' { Install-Android $Argument; break }
        '^(test|tests)$' {
            Run-Web -Arguments @('test')
            Run-Gradle -Tasks @(':app:testDebugUnitTest')
            Write-Host 'All tests completed.' -ForegroundColor Green
            break
        }
        '^(typecheck|check)$' { Run-Web -Arguments @('run', 'typecheck'); break }
        '^(db-migrate|migrate)$' { Run-Web -Arguments @('run', 'db:migrate'); break }
        '^(db-seed|seed)$' { Run-Web -Arguments @('run', 'db:seed'); break }
        '^install$' { Run-Web -Arguments @('ci'); break }
        '^(help|-h|--help)$' {
            Write-Host 'Sarmat launcher'
            Write-Host 'launcher.bat                         interactive menu'
            Write-Host 'launcher.bat dev                     development frontend + backend'
            Write-Host 'launcher.bat build                   Monitor production build'
            Write-Host 'launcher.bat android-release         release APK + AAB'
            Write-Host 'launcher.bat android-debug           debug APK'
            Write-Host 'launcher.bat android-install [serial] debug APK + install + launch'
            Write-Host 'launcher.bat test                    all Monitor and Android tests'
            Write-Host 'launcher.bat typecheck               TypeScript typecheck'
            Write-Host 'launcher.bat db-migrate              database migrations'
            Write-Host 'launcher.bat db-seed                 seed the database'
            Write-Host 'launcher.bat install                 npm ci'
            Write-Host 'launcher.bat help                    show this help'
            break
        }
        default { throw "Unknown command: $Action. Run launcher.bat help." }
    }
}

if (-not $interactive) {
    try { Invoke-Action $env:SARMAT_LAUNCHER_ACTION $env:SARMAT_LAUNCHER_ARGUMENT; exit 0 }
    catch { Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red; exit 1 }
}
while ($true) {
    Write-Host ''
    Write-Host 'SARMAT - LAUNCHER' -ForegroundColor Cyan
    Write-Host '  1. Start Monitor in development mode'
    Write-Host '  2. Build Monitor for production'
    Write-Host '  3. Build Android release (APK + AAB)'
    Write-Host '  4. Build and install Android debug'
    Write-Host '  5. Build Android debug APK'
    Write-Host '  6. Run all tests'
    Write-Host '  7. Run TypeScript checks'
    Write-Host '  8. Apply database migrations'
    Write-Host '  9. Seed the database'
    Write-Host ' 10. Install Node.js dependencies'
    Write-Host '  0. Exit'
    $choice = Read-Host 'Select an action'
    if ($choice -eq '0' -or $null -eq $choice) { exit 0 }
    $action = switch ($choice) {
        '1' { 'dev' }; '2' { 'build' }; '3' { 'android-release' }; '4' { 'android-install' }
        '5' { 'android-debug' }; '6' { 'test' }; '7' { 'typecheck' }; '8' { 'db-migrate' }
        '9' { 'db-seed' }; '10' { 'install' }; default { $null }
    }
    if (-not $action) { Write-Host "Unknown option: $choice"; continue }
    try { Invoke-Action $action '' }
    catch { Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red; exit 1 }
}
