[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)]
    [string]$MissionPlannerPath,
    [string]$SourcePath
)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $packagedDll = Join-Path $PSScriptRoot 'plugins\SarmatPlugin.dll'
    if (Test-Path -LiteralPath $packagedDll) {
        # GitHub Release layout: the extracted folder mirrors the Mission Planner root.
        $SourcePath = $PSScriptRoot
    } else {
        # Repository layout: scripts\install.ps1 installs the locally built dist DLL.
        $SourcePath = Join-Path $projectRoot 'dist'
    }
}
$missionPlanner = (Resolve-Path -LiteralPath $MissionPlannerPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $missionPlanner 'MissionPlanner.exe'))) {
    throw "MissionPlanner.exe was not found in '$missionPlanner'."
}
$sourceDll = Join-Path $SourcePath 'plugins\SarmatPlugin.dll'
if (-not (Test-Path -LiteralPath $sourceDll)) { throw "Build artifact not found: $sourceDll" }
$sourceDll = (Resolve-Path -LiteralPath $sourceDll).Path
$plugins = Join-Path $missionPlanner 'plugins'
$destination = Join-Path $plugins 'SarmatPlugin.dll'
if ($PSCmdlet.ShouldProcess($destination, 'Install Sarmat Plugin')) {
    New-Item -ItemType Directory -Force -Path $plugins | Out-Null
    # GitHub/browser downloads carry a Zone.Identifier (Mark-of-the-Web), which can
    # make .NET refuse to load the plugin. Remove it both before and after copying.
    Unblock-File -LiteralPath $sourceDll
    Copy-Item -LiteralPath $sourceDll -Destination $destination -Force
    Unblock-File -LiteralPath $destination
    Write-Host "Installed $destination"

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
