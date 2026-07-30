[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)]
    [string]$MissionPlannerPath,
    [string]$SourcePath
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = Join-Path $projectRoot 'dist'
}
$missionPlanner = (Resolve-Path -LiteralPath $MissionPlannerPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $missionPlanner 'MissionPlanner.exe'))) {
    throw "MissionPlanner.exe was not found in '$missionPlanner'."
}
$sourceDll = Join-Path $SourcePath 'SarmatPlugin.dll'
if (-not (Test-Path -LiteralPath $sourceDll)) { throw "Build artifact not found: $sourceDll" }
$plugins = Join-Path $missionPlanner 'plugins'
$destination = Join-Path $plugins 'SarmatPlugin.dll'
if ($PSCmdlet.ShouldProcess($destination, 'Install Sarmat Plugin')) {
    New-Item -ItemType Directory -Force -Path $plugins | Out-Null
    if (Test-Path -LiteralPath $destination) {
        $backup = "$destination.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Copy-Item -LiteralPath $destination -Destination $backup
        Write-Host "Previous plugin backed up to $backup"
    }
    Copy-Item -LiteralPath $sourceDll -Destination $destination -Force
    Unblock-File -LiteralPath $destination
    Write-Host "Installed $destination"
}
