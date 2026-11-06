@echo off
rem Publica LinqRover (Release, win-x64) en la carpeta ./build, junto a este script.
setlocal
cd /d "%~dp0"

set "OUT=%~dp0build"

dotnet publish "src\LinqRover\LinqRover.csproj" -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%OUT%"
if errorlevel 1 (
    echo.
    echo Error al publicar LinqRover.
    exit /b 1
)

echo.
echo Aplicacion publicada en "%OUT%"
endlocal
