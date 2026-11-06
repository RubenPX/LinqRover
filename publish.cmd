@echo off
rem Publica LinqRover (host Rustino, Release, win-x64, autocontenido, archivo unico) en la carpeta ./build.
setlocal
cd /d "%~dp0"

set "OUT=%~dp0build"

dotnet publish "src\LinqRover.Rustino\LinqRover.Rustino.csproj" -c Release -p:DebugType=None -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%OUT%"
if errorlevel 1 (
    echo.
    echo Error al publicar LinqRover.
    exit /b 1
)

echo.
echo Aplicacion publicada en "%OUT%"
endlocal
