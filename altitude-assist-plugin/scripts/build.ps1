[CmdletBinding()]param([Parameter(Mandatory=$true)][string]$MissionPlannerPath,[ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop';$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$missionPlanner=(Resolve-Path -LiteralPath $MissionPlannerPath).Path
if(-not(Test-Path -LiteralPath (Join-Path $missionPlanner 'MissionPlanner.exe'))){throw "MissionPlanner.exe was not found in '$missionPlanner'."}
dotnet restore (Join-Path $root 'SarmatAltitudeAssist.sln') --configfile (Join-Path $root 'NuGet.Config') "-p:MissionPlannerPath=$missionPlanner";if($LASTEXITCODE-ne 0){throw 'restore failed'}
dotnet build (Join-Path $root 'SarmatAltitudeAssist.sln') --no-restore -c $Configuration "-p:MissionPlannerPath=$missionPlanner";if($LASTEXITCODE-ne 0){throw 'build failed'}
& (Join-Path $root "tests\SarmatAltitudeAssist.Tests\bin\$Configuration\net472\SarmatAltitudeAssist.Tests.exe");if($LASTEXITCODE-ne 0){throw 'tests failed'}
$dist=Join-Path $root 'dist';if(Test-Path $dist){Remove-Item -LiteralPath $dist -Recurse -Force};New-Item -ItemType Directory -Path (Join-Path $dist 'plugins')|Out-Null
Copy-Item (Join-Path $root "src\SarmatAltitudeAssist\bin\$Configuration\net472\SarmatAltitudeAssist.dll") (Join-Path $dist 'plugins')
Copy-Item (Join-Path $root "src\SarmatAltitudeAssist.Core\bin\$Configuration\net472\SarmatAltitudeAssist.Core.dll") (Join-Path $dist 'plugins')
Write-Host "Altitude Assist distribution: $dist"
