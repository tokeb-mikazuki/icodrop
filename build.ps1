# Builds WallBlur.exe with the C# compiler that ships with Windows (.NET Framework 4.8). No SDK needed.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw   = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wpf  = Join-Path $fw 'WPF'

$refs = @(
    'System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
    (Join-Path $fw 'WPF\WindowsBase.dll'),
    (Join-Path $fw 'WPF\PresentationCore.dll'),
    (Join-Path $fw 'WPF\PresentationFramework.dll')
) | ForEach-Object { "/r:$_" }

# Stop a running copy so the exe isn't locked.
Get-Process icodrop,WallBlur -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

& "$fw\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /warn:3 `
    "/out:$here\icodrop.exe" "/win32manifest:$here\app.manifest" "/win32icon:$here\icodrop.ico" "/resource:$here\icodrop.ico,icodrop.ico" $refs `
    (Get-ChildItem "$here\*.cs" | ForEach-Object { $_.FullName })
if ($LASTEXITCODE -ne 0) { throw "build failed" }
Write-Host "Built $here\icodrop.exe"
