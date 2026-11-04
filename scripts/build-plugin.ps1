# Compila el plugin y crea dist\ClaudeCode_<versión>.lplug4 (Windows).
# - Si Logi Options+ está instalado, compila contra su PluginApi.dll y enlaza el plugin al servicio (recarga en caliente).
# - Si no, usa el PluginApi.dll oficial del paquete NuGet LogiPluginTool y solo empaqueta.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

dotnet tool restore | Out-Null
node scripts/generate.mjs

$hostApi = if ($env:PluginApiDir) { $env:PluginApiDir } else { 'C:\Program Files\Logi\LogiPluginService\' }
$toolVersion = (Get-Content .config/dotnet-tools.json | ConvertFrom-Json).tools.logiplugintool.version
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$nugetApi = Join-Path $nugetRoot "logiplugintool\$toolVersion\tools\net10.0\any\"

if (Test-Path (Join-Path $hostApi 'PluginApi.dll')) { $api = $hostApi; $link = 'true' } else { $api = $nugetApi; $link = 'false' }
Write-Host "PluginApi: $api (enlazar al servicio: $link)"

dotnet build logi-plugin/src/ClaudeCodePlugin.csproj -c Release "-p:PluginApiDir=$api" "-p:LinkPlugin=$link"
if ($LASTEXITCODE -ne 0) { throw 'La compilación falló' }

$version = ((Get-Content logi-plugin/src/package/metadata/LoupedeckPackage.yaml | Select-String '^version:').Line -replace '^version:\s*', '').Trim()
New-Item -ItemType Directory -Force dist | Out-Null
dotnet tool run logiplugintool -- pack logi-plugin/bin/Release "dist/ClaudeCode_$version.lplug4"
dotnet tool run logiplugintool -- verify "dist/ClaudeCode_$version.lplug4"
Copy-Item profiles/*.lp5 dist/
Write-Host "Listo: dist\ClaudeCode_$version.lplug4"
