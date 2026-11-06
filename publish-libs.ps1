# Tras publicar sin archivo único (ver publish.sh / publish.cmd):
# 1. Mueve todo lo publicado salvo el exe a la carpeta libs (DLL, deps.json, runtimeconfig.json, wwwroot...).
# 2. Parchea el apphost para que la app se busque en libs\<nombre>.dll (mismo método que AppHostPatcher de dnSpy):
#    reemplaza el nombre de la DLL embebido en el exe por la ruta relativa nueva.
param([Parameter(Mandatory = $true)][string]$Out)

$ErrorActionPreference = 'Stop'

$exeName = 'LinqRover.Rustino.exe'
$exe = Join-Path $Out $exeName
if (-not (Test-Path -LiteralPath $exe)) { throw "No se encontro $exe" }

$libs = Join-Path $Out 'libs'
New-Item -ItemType Directory -Force -Path $libs | Out-Null

# Lo que se queda junto al exe: el propio exe y las carpetas que crea la app en tiempo de ejecucion.
$keep = @($exeName, 'libs', 'profiles')
Get-ChildItem -LiteralPath $Out -Force | Where-Object {
    $keep -notcontains $_.Name -and $_.Name -notlike '*.WebView2'
} | ForEach-Object {
    Move-Item -LiteralPath $_.FullName -Destination $libs -Force
}

# Parche del apphost: "LinqRover.Rustino.dll" (UTF-8, terminado en NUL) -> "libs\LinqRover.Rustino.dll".
$oldName = 'LinqRover.Rustino.dll'
$newName = 'libs\' + $oldName
$bytes = [IO.File]::ReadAllBytes($exe)
$oldBytes = [Text.Encoding]::UTF8.GetBytes($oldName + [char]0)
$newBytes = [Text.Encoding]::UTF8.GetBytes($newName + [char]0)
if ($newBytes.Length -gt 1024) { throw 'La ruta nueva supera 1024 bytes' }

$idx = -1
for ($i = 0; $i -le $bytes.Length - $oldBytes.Length; $i++) {
    $match = $true
    for ($j = 0; $j -lt $oldBytes.Length; $j++) {
        if ($bytes[$i + $j] -ne $oldBytes[$j]) { $match = $false; break }
    }
    if ($match) { $idx = $i; break }
}
if ($idx -lt 0) { throw "No se encontro '$oldName' en $exe" }

# El apphost reserva un buffer de 1024 bytes para la ruta: lo que la ruta nueva ocupa de mas tiene que estar libre.
for ($k = $idx + $oldBytes.Length; $k -lt $idx + $newBytes.Length; $k++) {
    if ($bytes[$k] -ne 0) { throw "Sin espacio en el buffer del apphost (offset $k)" }
}

[Array]::Copy($newBytes, 0, $bytes, $idx, $newBytes.Length)
[IO.File]::WriteAllBytes($exe, $bytes)
Write-Host "Apphost parcheado: '$oldName' -> '$newName' (offset $idx)"
