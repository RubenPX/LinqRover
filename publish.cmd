@echo off
rem Publica LinqRover (host Rustino, Release, win-x64, autocontenido, sin archivo unico) en la carpeta ./build.
setlocal
cd /d "%~dp0"

set "OUT=%~dp0build"
set "WEB=%~dp0src\LinqRover.Rustino"

rem Tailwind: compila los estilos antes de publicar para que wwwroot\css\app.css este al dia.
pushd "%WEB%"
if not exist node_modules call npm ci
if errorlevel 1 goto :css_error
call npm run build:css
if errorlevel 1 goto :css_error
popd

dotnet publish "src\LinqRover.Rustino\LinqRover.Rustino.csproj" -c Release -p:DebugType=None -r win-x64 --self-contained true -p:PublishSingleFile=false -o "%OUT%"
if errorlevel 1 (
    echo.
    echo Error al publicar LinqRover.
    exit /b 1
)
rem Las DLL pasan a build\libs y el exe se parchea para leer desde ahi.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-libs.ps1" -Out "%OUT%"
if errorlevel 1 (
    echo.
    echo Error al publicar LinqRover.
    exit /b 1
)

echo.
echo Aplicacion publicada en "%OUT%"
endlocal
exit /b 0

:css_error
popd
echo.
echo Error al compilar los estilos Tailwind.
exit /b 1
