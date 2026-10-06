param([string]$OutputPath = (Join-Path $PSScriptRoot 'windows-app'))
$ErrorActionPreference = 'Stop'
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if ($OutputPath -eq $PSScriptRoot -or $OutputPath -eq (Split-Path $PSScriptRoot)) { throw 'Choose a dedicated output folder.' }
& dotnet publish (Join-Path $PSScriptRoot 'WriteUp/WriteUp.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $OutputPath
if ($LASTEXITCODE -ne 0) { throw 'Windows app build failed.' }
Copy-Item (Join-Path $PSScriptRoot 'WindowsPackage/AppxManifest.xml') (Join-Path $OutputPath 'AppxManifest.xml') -Force
$assets = Join-Path $OutputPath 'Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Image]::FromFile((Join-Path $PSScriptRoot 'WriteUp/Assets/dynamic-logo.png'))
try {
    foreach ($entry in @(@('StoreLogo.png',50), @('Logo150.png',150), @('Logo44.png',44))) {
        $size = [int]$entry[1]
        $bitmap = New-Object Drawing.Bitmap($size,$size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $scale = [Math]::Min($size / $source.Width, $size / $source.Height)
            $width = [int]($source.Width * $scale); $height = [int]($source.Height * $scale)
            $graphics.DrawImage($source, [int](($size-$width)/2), [int](($size-$height)/2), $width, $height)
            $bitmap.Save((Join-Path $assets $entry[0]), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $source.Dispose() }
Copy-Item (Join-Path $PSScriptRoot 'register-windows-app.ps1') $OutputPath -Force
Copy-Item (Join-Path $PSScriptRoot 'register-windows-app.cmd') $OutputPath -Force
Write-Host "Windows app layout ready: $OutputPath"
