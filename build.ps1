param([string]$OutputName = 'SilverWolfPet-v2.6.6')
$ErrorActionPreference = 'Stop'
if ($OutputName -notmatch '^[a-zA-Z0-9][a-zA-Z0-9_.-]*$') { throw 'Invalid output directory name' }
$projectRoot = $PSScriptRoot
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$outputPath = Join-Path (Join-Path $projectRoot 'dist') $OutputName
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$references = @('System.dll','System.Core.dll','Microsoft.CSharp.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll') | ForEach-Object { '/reference:' + (Join-Path $frameworkRoot $_) }
& (Join-Path $frameworkRoot 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 ('/win32icon:' + (Join-Path $projectRoot 'assets\silver-wolf.ico')) ('/win32manifest:' + (Join-Path $projectRoot 'src\app.manifest')) ('/out:' + (Join-Path $outputPath 'SilverWolfPet.exe')) $references (Join-Path $projectRoot 'src\Pet.cs') (Join-Path $projectRoot 'src\CodexMonitor.cs') (Join-Path $projectRoot 'src\Companion.cs') (Join-Path $projectRoot 'src\Touch.cs') (Join-Path $projectRoot 'src\Experience.cs') (Join-Path $projectRoot 'src\Sidebar.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
Copy-Item -Recurse -Force -LiteralPath (Join-Path $projectRoot 'assets') -Destination $outputPath
Copy-Item -Force -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $outputPath
Write-Output ('Built: ' + $outputPath)
