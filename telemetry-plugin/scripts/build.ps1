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
$distPlugins = Join-Path $dist 'plugins'
New-Item -ItemType Directory -Path $distPlugins | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot "src\SarmatPlugin\bin\$Configuration\net472\SarmatTelemetry.dll") -Destination $distPlugins
foreach ($assetName in @('icon.png', 'logo.txt', 'logo2.png', 'splashbg.png')) {
    $assetPath = Join-Path (Join-Path $projectRoot 'mission-planner') $assetName
    if (-not (Test-Path -LiteralPath $assetPath)) {
        throw "Mission Planner branding asset was not found: $assetPath"
    }
    Copy-Item -LiteralPath $assetPath -Destination $dist
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts\install.ps1') -Destination $dist
Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts\install.cmd') -Destination $dist
Write-Host "Distribution created at $dist"
