# LinqRover

IDE de consultas para **Oracle** y **SQL Server** hecho con .NET 10 y MAUI Blazor Hybrid (Windows).

## Qué hace

- Conexiones guardadas (contraseña cifrada con DPAPI).
- Explorar esquemas, tablas, columnas y relaciones (diagrama).
- Visor de datos paginado (30 filas).
- Editor SQL con autocompletado.
- Script C# con EF Core sobre la conexión, con autocompletado de Roslyn.

Todo es **solo lectura**: las consultas y los scripts se ejecutan en una transacción que siempre se deshace, y `SaveChanges` está bloqueado.

## Requisitos

- Windows 10 (19041) o superior
- .NET 10 SDK
- Node.js (para compilar los estilos de Tailwind/DaisyUI)

## Puesta en marcha

```bash
npm install --prefix src/LinqRover
dotnet build LinqRover.slnx
dotnet run --project src/LinqRover/LinqRover.csproj -f net10.0-windows10.0.19041.0
```

Opcional: crea un `.env` en la raíz con valores por defecto para el formulario de nueva conexión (no se sube a git):

```
LR_ENGINE=SqlServer
LR_HOST=localhost
LR_PORT=1433
LR_DB=mi_base
LR_USER=usuario
LR_PASS=contraseña
LR_TRUST_CERT=1
```

## Estructura

- `src/LinqRover.Core` — proveedores de base de datos, modelos, generación de clases y scripts EF Core.
- `src/LinqRover` — aplicación MAUI Blazor Hybrid (componentes, servicios, estilos).
