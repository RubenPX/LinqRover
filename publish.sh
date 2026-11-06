#!/usr/bin/env bash
# Publica LinqRover (host Rustino, Release, win-x64, autocontenido, archivo único) en ./build.
set -euo pipefail

cd "$(dirname "$0")"
OUT="$(pwd)/build"

dotnet publish "src/LinqRover.Rustino/LinqRover.Rustino.csproj" \
  -c Release \
  -p:DebugType=None \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o "$OUT"

echo
echo "Aplicacion publicada en $OUT"
