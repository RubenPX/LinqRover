#!/usr/bin/env bash
# Publica LinqRover (host Rustino, Release, win-x64, autocontenido) en ./build.
# Sin archivo único: el exe va junto a sus DLL, porque Roslyn necesita rutas reales a los ensamblados.
set -euo pipefail

cd "$(dirname "$0")"
OUT="$(pwd)/build"
WEB="src/LinqRover.Rustino"

# Tailwind: compila los estilos antes de publicar para que wwwroot/css/app.css esté al día.
if [ ! -d "$WEB/node_modules" ]; then
  (cd "$WEB" && npm ci)
fi
(cd "$WEB" && npm run build:css)

dotnet publish "src/LinqRover.Rustino/LinqRover.Rustino.csproj" \
  -c Release \
  -p:DebugType=None \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -o "$OUT"

# Las DLL y el resto pasan a ./build/libs y el exe se parchea para leer desde ahi.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$(cygpath -w "$(pwd)/publish-libs.ps1")" -Out "$(cygpath -w "$OUT")"

echo
echo "Aplicacion publicada en $OUT (ejecutar LinqRover.Rustino.exe)"
