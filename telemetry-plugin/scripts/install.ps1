[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$MissionPlannerPath,
    [string]$SourcePath,
    [switch]$MissionPlannerPathConfirmed
)
$ErrorActionPreference = 'Stop'

function Test-MissionPlannerDirectory {
    param([string]$Path)
    return -not [string]::IsNullOrWhiteSpace($Path) -and
        (Test-Path -LiteralPath (Join-Path $Path 'MissionPlanner.exe'))
}

function Find-MissionPlannerSuggestion {
    $candidates = New-Object System.Collections.Generic.List[string]
    $candidates.Add($PSScriptRoot)

    foreach ($programFiles in @(
        [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86),
        [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
    )) {
        if (-not [string]::IsNullOrWhiteSpace($programFiles)) {
            $candidates.Add((Join-Path $programFiles 'Mission Planner'))
        }
    }

    foreach ($registryPath in @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )) {
        Get-ItemProperty -Path $registryPath -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName -like '*Mission Planner*' } |
            ForEach-Object {
                if (-not [string]::IsNullOrWhiteSpace($_.InstallLocation)) {
                    $candidates.Add($_.InstallLocation.Trim('"'))
                }
                if (-not [string]::IsNullOrWhiteSpace($_.DisplayIcon)) {
                    $displayIcon = $_.DisplayIcon.Trim('"') -replace ',\d+$', ''
                    $candidates.Add((Split-Path -Parent $displayIcon))
                }
            }
    }

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-MissionPlannerDirectory $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

function Select-MissionPlannerDirectory {
    param([string]$SuggestedPath)
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = 'Select which Mission Planner installation should receive the Sarmat plugin (folder containing MissionPlanner.exe)'
    $dialog.ShowNewFolderButton = $false
    if (Test-MissionPlannerDirectory $SuggestedPath) {
        $dialog.SelectedPath = (Resolve-Path -LiteralPath $SuggestedPath).Path
    }
    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        throw 'Mission Planner installation was not selected.'
    }
    if (-not (Test-MissionPlannerDirectory $dialog.SelectedPath)) {
        throw "MissionPlanner.exe was not found in '$($dialog.SelectedPath)'."
    }
    return (Resolve-Path -LiteralPath $dialog.SelectedPath).Path
}

if (-not $MissionPlannerPathConfirmed) {
    $suggestedPath = if (Test-MissionPlannerDirectory $MissionPlannerPath) {
        $MissionPlannerPath
    } else {
        Find-MissionPlannerSuggestion
    }
    $MissionPlannerPath = Select-MissionPlannerDirectory $suggestedPath
} elseif (-not (Test-MissionPlannerDirectory $MissionPlannerPath)) {
    throw "MissionPlanner.exe was not found in the confirmed folder '$MissionPlannerPath'."
} else {
    $MissionPlannerPath = (Resolve-Path -LiteralPath $MissionPlannerPath).Path
}

$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal($currentIdentity)
$isAdministrator = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdministrator -and -not $WhatIfPreference) {
    $elevatedArguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"{0}"' -f $PSCommandPath),
        '-MissionPlannerPath', ('"{0}"' -f $MissionPlannerPath),
        '-MissionPlannerPathConfirmed'
    )
    if (-not [string]::IsNullOrWhiteSpace($SourcePath)) {
        $elevatedArguments += @('-SourcePath', ('"{0}"' -f $SourcePath))
    }

    $elevated = Start-Process -FilePath 'powershell.exe' -Verb RunAs -Wait -PassThru -ArgumentList $elevatedArguments
    exit $elevated.ExitCode
}

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $packagedDll = Join-Path $PSScriptRoot 'plugins\SarmatTelemetry.dll'
    $packagedAltitude = Join-Path $PSScriptRoot 'plugins\SarmatAltitude.dll'
    if ((Test-Path -LiteralPath $packagedDll) -or (Test-Path -LiteralPath $packagedAltitude)) {
        # GitHub Release layout: the extracted folder mirrors the Mission Planner root.
        $SourcePath = $PSScriptRoot
    } else {
        # Repository layout: scripts\install.ps1 installs the locally built dist DLL.
        $SourcePath = Join-Path $projectRoot 'dist'
    }
}
$missionPlanner = $MissionPlannerPath
$pluginNames = @('SarmatTelemetry.dll', 'SarmatAltitude.dll') | Where-Object {
    Test-Path -LiteralPath (Join-Path $SourcePath "plugins\$_")
}
if (-not $pluginNames) { throw "Plugin build artifacts not found in $SourcePath\plugins" }
$plugins = Join-Path $missionPlanner 'plugins'
$legacyDestination = Join-Path $plugins 'SarmatPlugin.dll'
foreach ($pluginName in $pluginNames) {
  $sourceDll = (Resolve-Path -LiteralPath (Join-Path $SourcePath "plugins\$pluginName")).Path
  $destination = Join-Path $plugins $pluginName
  if ($PSCmdlet.ShouldProcess($destination, "Install $pluginName")) {
    New-Item -ItemType Directory -Force -Path $plugins | Out-Null
    if ($pluginName -eq 'SarmatTelemetry.dll' -and (Test-Path -LiteralPath $legacyDestination)) {
        Remove-Item -LiteralPath $legacyDestination -Force
        Write-Host "Removed legacy $legacyDestination"
    }
    # GitHub/browser downloads carry a Zone.Identifier (Mark-of-the-Web), which can
    # make .NET refuse to load the plugin. Remove it both before and after copying.
    Unblock-File -LiteralPath $sourceDll
    Copy-Item -LiteralPath $sourceDll -Destination $destination -Force
    Unblock-File -LiteralPath $destination
    Write-Host "Installed $destination"
  }
}
if ($PSCmdlet.ShouldProcess($missionPlanner, 'Install Sarmat branding assets')) {
    foreach ($assetName in @('icon.png', 'logo.txt', 'logo2.png', 'splashbg.png')) {
        $sourceAsset = Join-Path $SourcePath $assetName
        if (-not (Test-Path -LiteralPath $sourceAsset)) {
            throw "Mission Planner branding asset was not found: $sourceAsset"
        }

        $assetDestination = Join-Path $missionPlanner $assetName
        Unblock-File -LiteralPath $sourceAsset
        Copy-Item -LiteralPath $sourceAsset -Destination $assetDestination -Force
        Unblock-File -LiteralPath $assetDestination
        Write-Host "Installed $assetDestination"
    }
}
