[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)]
    [string]$MissionPlannerPath
)
$ErrorActionPreference = 'Stop'
$missionPlanner = (Resolve-Path -LiteralPath $MissionPlannerPath).Path
$destination = Join-Path $missionPlanner 'plugins\SarmatPlugin.dll'
if (-not (Test-Path -LiteralPath $destination)) {
    Write-Host "SarmatPlugin.dll is not installed."
    return
}
if ($PSCmdlet.ShouldProcess($destination, 'Uninstall Sarmat Plugin')) {
    Remove-Item -LiteralPath $destination -Force
    Write-Host "Removed $destination. User settings and logs were preserved."
}
