param([string]$Serial, [switch]$Help)

$ErrorActionPreference = 'Stop'
if ($Help -or $Serial -in @('--help', '-h')) {
    Write-Host 'Usage: .\build-and-install.bat [DEVICE_SERIAL]'
    Write-Host 'Builds, installs and launches Sarmat Crew on one connected Android device.'
    exit 0
}

try {
    # Use process-local settings only; do not change the user's environment.
    $sdkCandidates = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT,
        "$env:LOCALAPPDATA\Android\Sdk")
    $sdk = $sdkCandidates | Where-Object {
        $_ -and (Test-Path -LiteralPath (Join-Path $_ 'platform-tools\adb.exe'))
    } | Select-Object -First 1
    $adbCommand = Get-Command adb.exe -ErrorAction SilentlyContinue
    if ($sdk) {
        $adb = Join-Path $sdk 'platform-tools\adb.exe'
        $env:ANDROID_HOME = $sdk
        $env:ANDROID_SDK_ROOT = $sdk
    } elseif ($adbCommand) {
        $adb = $adbCommand.Source
    } else {
        throw 'Android SDK Platform-Tools not found. Install them in Android Studio > SDK Manager, or set ANDROID_HOME.'
    }

    # Prefer supported LTS JDKs; recent Android Studio bundles may contain Java 25.
    $javaCommand = Get-Command java.exe -ErrorAction SilentlyContinue
    $jdkCandidates = @((Join-Path $PSScriptRoot '.tools\jdk-17'), $env:JAVA_HOME)
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
        if ((Test-Path -LiteralPath $releaseFile) -and
            (Test-Path -LiteralPath (Join-Path $candidate 'bin\javac.exe'))) {
            $versionLine = Get-Content -LiteralPath $releaseFile | Where-Object { $_ -match '^JAVA_VERSION=' }
            if ($versionLine -match '^JAVA_VERSION="(17|21)(?:\.|"|\+)') {
                $selectedJdk = $candidate
                break
            }
        }
    }
    if (-not $selectedJdk) {
        throw 'Compatible JDK not found. Install JDK 17 or 21 and set JAVA_HOME to its folder. Java 25 bundled with newer Android Studio is incompatible with this project build.'
    }
    $env:JAVA_HOME = $selectedJdk
    Write-Host "Using Java: $selectedJdk"

    Write-Host '[1/4] Checking Android device...'
    & $adb start-server
    if ($LASTEXITCODE -ne 0) { throw 'Could not start adb.' }
    $deviceLines = & $adb devices -l
    if ($LASTEXITCODE -ne 0) { throw 'Could not list Android devices.' }
    $devices = @($deviceLines | ForEach-Object {
        if ($_ -match '^(\S+)\s+device(?:\s|$)') { $Matches[1] }
    })
    if ($Serial) {
        if ($Serial -notin $devices) {
            $deviceLines | Write-Host
            throw "Device '$Serial' is unavailable. Enable USB debugging and accept the authorization prompt on the phone."
        }
    } else {
        if ($devices.Count -ne 1) {
            $deviceLines | Write-Host
            if ($devices.Count -eq 0) { throw 'Connect your phone with a data USB cable, enable USB debugging and accept the authorization prompt.' }
            throw 'Multiple devices found. Run build-and-install.bat DEVICE_SERIAL to select one.'
        }
        $Serial = $devices[0]
    }

    Write-Host '[2/4] Building debug APK...'
    Push-Location $PSScriptRoot
    try {
        & '.\gradlew.bat' "-Dorg.gradle.java.home=$selectedJdk" ':app:assembleDebug'
        if ($LASTEXITCODE -ne 0) { throw 'Build failed. See the Gradle error above; install Android SDK 36 in SDK Manager if required.' }
    } finally { Pop-Location }

    $apk = Join-Path $PSScriptRoot 'app\build\outputs\apk\debug\app-debug.apk'
    if (-not (Test-Path -LiteralPath $apk)) { throw "APK not found: $apk" }
    Write-Host "[3/4] Installing on $Serial..."
    & $adb -s $Serial install -r $apk
    if ($LASTEXITCODE -ne 0) { throw 'Installation failed. See the adb error above. Existing app data has not been automatically deleted.' }

    Write-Host '[4/4] Launching Sarmat Crew...'
    $launchOutput = & $adb -s $Serial shell am start -W -n 'com.sarmat.crew/.MainActivity' 2>&1
    $launchExit = $LASTEXITCODE
    $launchOutput | Write-Host
    if ($launchExit -ne 0 -or ($launchOutput -match 'Error:|Exception')) { throw 'App installed, but could not be launched.' }
    Write-Host 'Done! Sarmat Crew is installed and running.' -ForegroundColor Green
} catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
