$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The x64 .NET Framework 4.x compiler was not found.' }
$buildDirectory = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $buildDirectory -Force | Out-Null
$sourceFile = Join-Path $PSScriptRoot 'src\SimplePopupGuard.cs'
$manifestFile = Join-Path $PSScriptRoot 'src\PopupGuard.manifest'
$outputFile = Join-Path $buildDirectory 'PopupGuard.exe'
& $compiler /nologo /target:winexe /platform:x64 /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/win32manifest:$manifestFile" "/out:$outputFile" $sourceFile
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output ('Built: ' + $outputFile)
