$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $projectRoot 'artifacts'
$appRoot = Join-Path $artifactRoot 'app'
$packageRoot = Join-Path $artifactRoot 'package'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

New-Item -ItemType Directory -Force -Path $appRoot, $packageRoot | Out-Null
dotnet restore (Join-Path $projectRoot 'NetworkMonitor.csproj') --source $packageRoot
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed' }
dotnet publish (Join-Path $projectRoot 'NetworkMonitor.csproj') -c Release --no-restore -o $appRoot -p:PublishSingleFile=false -p:DebugType=None
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

$uninstallerPath = Join-Path $packageRoot 'Uninstall.exe'
& $compiler /nologo /target:winexe "/out:$uninstallerPath" /reference:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'Uninstall.cs')
if ($LASTEXITCODE -ne 0) { throw 'Uninstaller compilation failed' }

$payload = Join-Path $packageRoot 'payload.zip'
$payloadFiles = @('NetworkMonitor.exe','NetworkMonitor.dll','NetworkMonitor.deps.json','NetworkMonitor.runtimeconfig.json')
foreach ($file in $payloadFiles) { Copy-Item -LiteralPath (Join-Path $appRoot $file) -Destination (Join-Path $packageRoot $file) -Force }
if (Test-Path -LiteralPath $payload) { Remove-Item -LiteralPath $payload -Force }
$inputs = @($payloadFiles | ForEach-Object { Join-Path $packageRoot $_ }) + $uninstallerPath
Compress-Archive -LiteralPath $inputs -DestinationPath $payload -CompressionLevel Optimal

$setupPath = Join-Path $artifactRoot 'NetworkMonitor-Setup-1.0.0.exe'
& $compiler /nologo /target:winexe "/out:$setupPath" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.IO.Compression.dll /reference:Microsoft.CSharp.dll "/resource:$payload,payload.zip" (Join-Path $PSScriptRoot 'Setup.cs')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
Get-Item -LiteralPath $setupPath | Select-Object FullName,Length
