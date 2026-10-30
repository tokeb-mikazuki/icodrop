# Builds dist\icodrop-setup.exe: a single-file installer that carries icodrop.exe inside it.
# Uses only the C# compiler that ships with Windows; nothing to download.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw   = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$dist = Join-Path $here 'dist'

# 1) the app itself (also regenerates the icon if it is missing)
if (-not (Test-Path "$here\icodrop.ico")) { & "$here\make-icon.ps1" }
& "$here\build.ps1"

# 2) the installer, with the app and the icon embedded as resources
New-Item -ItemType Directory -Force $dist | Out-Null
$refs = @(
    'System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'Microsoft.CSharp.dll',
    (Join-Path $fw 'WPF\WindowsBase.dll'),
    (Join-Path $fw 'WPF\PresentationCore.dll'),
    (Join-Path $fw 'WPF\PresentationFramework.dll')
) | ForEach-Object { "/r:$_" }

& "$fw\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /warn:3 `
    "/out:$dist\icodrop-setup.exe" "/win32manifest:$here\app.manifest" "/win32icon:$here\icodrop.ico" `
    "/resource:$here\icodrop.exe,payload.exe" "/resource:$here\icodrop.ico,icodrop.ico" $refs `
    "$here\installer\Installer.cs"
if ($LASTEXITCODE -ne 0) { throw "installer build failed" }

$size = [math]::Round((Get-Item "$dist\icodrop-setup.exe").Length / 1KB)
Write-Host "Built $dist\icodrop-setup.exe ($size KB)"
