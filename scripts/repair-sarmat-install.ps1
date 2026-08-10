[CmdletBinding()]
param(
    [string]$InstallerPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\SarmatPlugins-1.5.3.msi')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $InstallerPath)) { throw "Installer not found: $InstallerPath" }

$uninstallRoots = @(
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*'
)
$products = Get-ItemProperty $uninstallRoots -ErrorAction SilentlyContinue |
    Where-Object { $_.DisplayName -in @('Sarmat Plugins for Mission Planner', 'Sarmat Altitude Assist for Mission Planner') } |
    Sort-Object PSChildName -Unique

foreach ($product in $products) {
    Write-Host "Removing $($product.DisplayName) $($product.DisplayVersion)..."
    $process = Start-Process msiexec.exe -ArgumentList @('/x', $product.PSChildName, '/qn', '/norestart') -Wait -PassThru
    if ($process.ExitCode -notin @(0, 1605, 3010)) { throw "Uninstall failed for $($product.DisplayName): $($process.ExitCode)" }
}

Write-Host 'Installing current shared Sarmat package...'
$install = Start-Process msiexec.exe -ArgumentList @('/i', ('"' + (Resolve-Path $InstallerPath).Path + '"'), '/qn', '/norestart') -Wait -PassThru
if ($install.ExitCode -notin @(0, 3010)) { throw "Install failed: $($install.ExitCode)" }
Write-Host 'Sarmat installer registration repaired successfully.'
