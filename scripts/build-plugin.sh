#!/usr/bin/env bash
# Compila el plugin y crea dist/ClaudeCode_<versión>.lplug4 (macOS / Linux).
# - Si Logi Options+ está instalado, compila contra su PluginApi.dll y enlaza el plugin al servicio (recarga en caliente).
# - Si no, usa el PluginApi.dll oficial del paquete NuGet LogiPluginTool y solo empaqueta.
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet tool restore >/dev/null
node scripts/generate.mjs

HOST_API="/Applications/Utilities/LogiPluginService.app/Contents/MonoBundle/"
TOOL_VERSION=$(node -e 'console.log(require("./.config/dotnet-tools.json").tools.logiplugintool.version)')
NUGET_API="${NUGET_PACKAGES:-$HOME/.nuget/packages}/logiplugintool/${TOOL_VERSION}/tools/net10.0/any/"

if [[ -f "${PluginApiDir:-$HOST_API}PluginApi.dll" ]]; then
  API="${PluginApiDir:-$HOST_API}"; LINK=true
else
  API="$NUGET_API"; LINK=false
fi
echo "PluginApi: $API (enlazar al servicio: $LINK)"

dotnet build logi-plugin/src/ClaudeCodePlugin.csproj -c Release -p:PluginApiDir="$API" -p:LinkPlugin="$LINK"

VERSION=$(sed -n 's/^version: *//p' logi-plugin/src/package/metadata/LoupedeckPackage.yaml)
mkdir -p dist
dotnet tool run logiplugintool -- pack logi-plugin/bin/Release "dist/ClaudeCode_${VERSION}.lplug4"
dotnet tool run logiplugintool -- verify "dist/ClaudeCode_${VERSION}.lplug4"
cp profiles/*.lp5 dist/
echo "Listo: dist/ClaudeCode_${VERSION}.lplug4"
