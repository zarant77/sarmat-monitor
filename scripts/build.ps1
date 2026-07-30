[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$MissionPlannerPath,
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$missionPlanner = (Resolve-Path -LiteralPath $MissionPlannerPath).Path
$missionPlannerExe = Join-Path $missionPlanner 'MissionPlanner.exe'
if (-not (Test-Path -LiteralPath $missionPlannerExe)) {
    throw "MissionPlanner.exe was not found in '$missionPlanner'."
}

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
& $dotnet restore (Join-Path $projectRoot 'SarmatPlugin.sln') --configfile (Join-Path $projectRoot 'NuGet.Config') "-p:MissionPlannerPath=$missionPlanner"
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

& $dotnet build (Join-Path $projectRoot 'SarmatPlugin.sln') --no-restore -c $Configuration "-p:MissionPlannerPath=$missionPlanner"
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

$testExe = Join-Path $projectRoot "tests\SarmatPlugin.Tests\bin\$Configuration\net472\SarmatPlugin.Tests.exe"
& $testExe
if ($LASTEXITCODE -ne 0) { throw "Unit tests failed with exit code $LASTEXITCODE." }

$dist = Join-Path $projectRoot 'dist'
if (Test-Path -LiteralPath $dist) { Remove-Item -LiteralPath $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot "src\SarmatPlugin\bin\$Configuration\net472\SarmatPlugin.dll") -Destination $dist
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $dist
Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts\install.ps1') -Destination $dist
Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts\uninstall.ps1') -Destination $dist
Set-Content -LiteralPath (Join-Path $dist 'build-info.txt') -Encoding UTF8 -Value @(
    "Built: $(Get-Date -Format o)"
    "Configuration: $Configuration"
    "Mission Planner: $($missionPlanner)"
    "Mission Planner version: $((Get-Item -LiteralPath $missionPlannerExe).VersionInfo.FileVersion)"
    "Target framework: .NET Framework 4.7.2"
)
Write-Host "Distribution created at $dist"
