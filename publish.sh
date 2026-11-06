#!/usr/bin/env bash
# Publica LinqRover (Release, win-x64) en la carpeta ./build, junto a este script.
set -euo pipefail

cd "$(dirname "$0")"
OUT="$(pwd)/build"

dotnet publish "src/LinqRover/LinqRover.csproj" \
  -c Release \
  -f net10.0-windows10.0.19041.0 \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o "$OUT"

echo
echo "Aplicacion publicada en $OUT"
