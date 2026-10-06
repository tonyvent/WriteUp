# Development registration gives Windows offline speech the package identity it requires.
# It does not install certificates, change Windows policy, or download speech models.
$ErrorActionPreference = 'Stop'
$developerMode = 0
try {
    $developerSettings = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction Stop
    $developerProperty = $developerSettings.PSObject.Properties['AllowDevelopmentWithoutDevLicense']
    if ($null -ne $developerProperty) { $developerMode = $developerProperty.Value }
} catch {
    # A missing key/value is normal when Developer Mode has never been enabled.
    $developerMode = 0
}
if ($developerMode -ne 1) {
    Write-Host 'Developer Mode is not enabled. Opening Windows settings...' -ForegroundColor Yellow
    try { Start-Process 'ms-settings:developers' } catch { }
    throw 'Turn on Developer Mode in Windows Settings, then rerun register-windows-app.cmd. If your organization controls this setting, ask IT about installing the Windows app. This script does not change Windows policy.'
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
Write-Host 'WriteUp registered. Keep this folder in place. Launch WriteUp (Offline speech) from Start.'
Start-Process explorer.exe ('shell:AppsFolder\' + $package.PackageFamilyName + '!WriteUp')
