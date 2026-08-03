[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory = $true)]
    [string]$MissionPlannerPath
)
$ErrorActionPreference = 'Stop'
$missionPlanner = (Resolve-Path -LiteralPath $MissionPlannerPath).Path
$destinations = @(
    (Join-Path $missionPlanner 'plugins\SarmatTelemetry.dll'),
    (Join-Path $missionPlanner 'plugins\SarmatPlugin.dll')
)
$installed = @($destinations | Where-Object { Test-Path -LiteralPath $_ })
if ($installed.Count -eq 0) {
    Write-Host "SarmatTelemetry.dll is not installed."
    return
}
foreach ($destination in $installed) {
    if ($PSCmdlet.ShouldProcess($destination, 'Uninstall Sarmat Telemetry Plugin')) {
        Remove-Item -LiteralPath $destination -Force
        Write-Host "Removed $destination. User settings and logs were preserved."
    }
}
