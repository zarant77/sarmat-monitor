[CmdletBinding()]param([Parameter(Mandatory=$true)][string]$MissionPlannerPath,[ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop';$root=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
dotnet restore (Join-Path $root 'SarmatVisionHold.sln') --configfile (Join-Path $root 'NuGet.Config') "-p:MissionPlannerPath=$MissionPlannerPath";if($LASTEXITCODE-ne 0){throw 'restore failed'}
dotnet build (Join-Path $root 'SarmatVisionHold.sln') --no-restore -c $Configuration "-p:MissionPlannerPath=$MissionPlannerPath";if($LASTEXITCODE-ne 0){throw 'build failed'}
& (Join-Path $root "tests\SarmatVisionHold.Tests\bin\$Configuration\net472\SarmatVisionHold.Tests.exe");if($LASTEXITCODE-ne 0){throw 'tests failed'}
& (Join-Path $root "tests\SarmatVisionHold.OfflineTests\bin\$Configuration\net472\SarmatVisionHold.OfflineTests.exe");if($LASTEXITCODE-ne 0){throw 'offline optical-flow tests failed'}
$dist=Join-Path $root 'dist';if(Test-Path $dist){Remove-Item -LiteralPath $dist -Recurse -Force};New-Item -ItemType Directory -Path (Join-Path $dist 'plugins')|Out-Null
Copy-Item (Join-Path $root "src\SarmatVisionHold\bin\$Configuration\net472\SarmatVisionHold.dll") (Join-Path $dist 'plugins')
Copy-Item (Join-Path $root "src\SarmatVisionHold.Core\bin\$Configuration\net472\SarmatVisionHold.Core.dll") (Join-Path $dist 'plugins')
Copy-Item (Join-Path $root "src\SarmatVisionHold.Vision\bin\$Configuration\net472\SarmatVisionHold.Vision.dll") (Join-Path $dist 'plugins')
Get-ChildItem (Join-Path $root "src\SarmatVisionHold\bin\$Configuration\net472") -Filter 'OpenCvSharp*.dll' -Recurse|Copy-Item -Destination (Join-Path $dist 'plugins') -Force
Write-Host "Distribution: $dist"
