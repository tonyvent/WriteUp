# Development registration gives Windows dictation the package identity it requires.
# It does not install certificates, change Windows policy, or download speech models.
$ErrorActionPreference = 'Stop'
$developerMode = Get-ItemPropertyValue 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' 'AllowDevelopmentWithoutDevLicense' -ErrorAction SilentlyContinue
if ($developerMode -ne 1) {
    throw 'For this test build, enable Developer Mode in Windows Settings > System > For developers, then run this script again. This script does not change that setting.'
}
if (Get-Process -Name WriteUp -ErrorAction SilentlyContinue) { throw 'Close WriteUp before registering or updating the Windows app.' }
if (Test-Path (Join-Path $PSScriptRoot 'AppxManifest.xml')) {
    $layout = $PSScriptRoot
} else {
    $layout = Join-Path $PSScriptRoot 'windows-app'
    & (Join-Path $PSScriptRoot 'build-windows-app.ps1') -OutputPath $layout
}
Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml') -ForceApplicationShutdown
$package = Get-AppxPackage -Name WriteUp.Desktop
if (-not $package) { throw 'Windows app registration did not succeed.' }
Write-Host 'WriteUp registered. Keep this folder in place. Launch WriteUp (Windows dictation) from Start.'
Start-Process explorer.exe ('shell:AppsFolder\' + $package.PackageFamilyName + '!WriteUp')
