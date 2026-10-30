# Builds the preview tool against the current icodrop.exe and renders docs\preview-*.jpg.
# Needs: docs\wallpaper.jpg (tools\make-wallpaper.ps1) and the apps listed in tools\icons.txt.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$fw   = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$bin  = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null

if (-not (Test-Path "$root\icodrop.exe")) { & "$root\build.ps1" }
if (-not (Test-Path "$root\docs\wallpaper.jpg")) { & "$PSScriptRoot\make-wallpaper.ps1" }

Copy-Item "$root\icodrop.exe" "$bin\icodrop.exe" -Force
$refs = @('System.dll', 'System.Core.dll', 'System.Xaml.dll',
    (Join-Path $fw 'WPF\WindowsBase.dll'), (Join-Path $fw 'WPF\PresentationCore.dll'), (Join-Path $fw 'WPF\PresentationFramework.dll'),
    "$bin\icodrop.exe") | ForEach-Object { "/r:$_" }

& "$fw\csc.exe" /nologo /target:exe /platform:x64 /optimize+ "/out:$bin\icodrop-preview.exe" $refs "$PSScriptRoot\Preview.cs"
if ($LASTEXITCODE -ne 0) { throw 'preview tool build failed' }

& "$bin\icodrop-preview.exe" "$root\docs\wallpaper.jpg" "$PSScriptRoot\icons.txt" "$root\docs"
if ($LASTEXITCODE -ne 0) { throw 'rendering failed' }
