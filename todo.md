# TODO — LinqRover

Estado actualizado a 6 de noviembre de 2026.

## Estado del repositorio

- **Rama activa:** `main`, sincronizada con `origin/main` (último commit `5274eaf`).
- **Ramas:** `feature/visual-query-builder`, `ide-vscode-theme` y `rustino-ui` ya están fusionadas y borradas. Solo queda `main`.
- **Cambios sin commitear:** ninguno. El `todo.md` se escribe ahora en este commit.
- **Archivos generados:** `src/LinqRover.Rustino/wwwroot/css/app.css` cambia al compilar (lo regenera Tailwind; no es un cambio real). `build/` y `bin/`/`obj/` están ignorados.

## Arquitectura actual

- **La app es `LinqRover.Rustino`**: host Blazor con `EdoardoTona.Rustino.Blazor` (0.6.0-preview.2), sobre WebView2. Es el único punto de entrada.
- **El proyecto MAUI está eliminado.** Su historial está en git. La UI, los servicios, los estilos y `wwwroot` viven en `src/LinqRover.Rustino`.
- **Solución (`LinqRover.slnx`):** `LinqRover.Core`, `LinqRover.QueryBuilder` y `LinqRover.Rustino`.
- **Perfiles de conexión:** `(carpeta del ejecutable)/profiles/(usuario de Windows).json`, cifrado entero con DPAPI. Un archivo antiguo en texto plano se lee y se reescribe cifrado.
- **Al cerrar la ventana**, el proceso termina (`Environment.Exit(0)`).
- **Publicación:** `publish.sh` y `publish.cmd` generan un ejecutable único autocontenido en `./build` (Release, sin `.pdb`, con `wwwroot` junto a él).

## Estado funcional

### Hecho y probado
- Conexiones SQL Server y Oracle, metadatos, visor paginado de 30 filas.
- Editor SQL con Ctrl+Enter, pestañas SQL, EF Script y Editor visual desde el botón «+».
- EF Script: autocompletado con Roslyn y «Ver SQL» en modal.
- Barra lateral de tablas colapsable, con tooltip de columnas al pasar el ratón.
- Modal de tabla (Datos, Esquema, Diagrama).
- Editor visual: arrastrar tablas, enlazar columnas, validación de tipos, formulario de consulta a la derecha con `Select`, `Where`, `GroupBy`, `DistinctBy`, agregaciones, `Skip`/`Take`/`Limit`.
- Quitar una tabla del diagrama actualiza el formulario y descarta sus columnas.
- Divisor vertical arrastrable entre editor/diagrama y resultados (SQL, EF y visual).
- Pie de resultados con tiempo de ejecución; los acentos se cargan bien en Oracle.
- Tema oscuro tipo VS Code, con barra de actividades, barra de estado y barra de título nativa.
- README reescrito, con banner, icono (`app.ico` embebido en el ejecutable) y una captura del editor visual.

### Pendiente de confirmar
- Ejecutar el formulario visual contra Oracle 11g y contra SQL Server. El SQL se ha revisado a mano.
- Comprobar en pantalla la lista de conexiones y el espacio de trabajo tras el cifrado del perfil (la lista se vio bien; falta revisar el resto del flujo).
- EF Script generado por el formulario: compilarlo y comparar sus filas con el SQL.
- Probar el generador ROWNUM en Oracle 19c o superior (solo probado en 11g).
- `ORA-03115` con `Any()` sin correlación dentro de un `Where` en 11g (EF pasa un booleano como parámetro que el driver no admite). Sin resolver.
- `FirstOrDefault` sin resultados muestra 0 filas sin indicar que el valor es null.

## Tareas pendientes de siempre

1. **Pruebas en Oracle 19c o superior** del generador ROWNUM.
2. **`ORA-03115`** con `Any()` sin correlación en un `Where` (11g). Opciones: convertir el booleano a número en el generador, o documentarlo como limitación.
3. **Monitor de workspaces de Roslyn:** comprobar que no hay fugas con uso prolongado.
4. **Subir cambios:** `origin/main` ya está al día; cualquier cambio nuevo se sube solo cuando el usuario lo pida.

## Entorno de pruebas local (Dile a un agente que lo haga)

- **Base de datos de ejemplo «Tienda»** (Oracle 11g XE): `docker/docker-compose.yml` y `docker/tienda-oracle.sql`.
  - Contenedor: `linqrover-oracle-11g`. Arrancar con `docker compose -f docker/docker-compose.yml up -d`. La instancia tarda unos minutos en abrirse.
  - Cargar datos: el comando está en la cabecera del `docker-compose.yml` y necesita `NLS_LANG=AMERICAN_AMERICA.AL32UTF8`; sin eso, los acentos se corrompen.
  - Conexión de la app: host `localhost`, puerto `1521`, servicio `XE`, usuario `TIENDA`, contraseña `TIENDA`.
  - Tablas: `CLIENTES` (8), `PRODUCTOS` (12), `PEDIDOS` (15), `PEDIDO_LINEAS` (27).
- **Contenedor antiguo `ora11g`** (`oracleinanutshell/oracle-xe-11g`): parado. No se usa para las pruebas actuales. Usa el mismo puerto 1521, así que no puede estar en marcha a la vez que `linqrover-oracle-11g`.
- **Regla:** no tocar producción ni los servidores de la empresa sin confirmación. El `.env` apunta a `10.215.37.21` (SQL Server de pruebas de la empresa): no usarlo para pruebas automáticas.

## Notas de trabajo

- **Parar la app antes de compilar** (`taskkill //F //IM LinqRover.Rustino.exe`). Si no, falla por DLLs bloqueadas.
- **Compilar:** `dotnet build LinqRover.slnx`.
- **Ejecutar:** `dotnet run --project src/LinqRover.Rustino/LinqRover.Rustino.csproj`.
- **Publicar:** `./publish.sh` o `publish.cmd`.
- **Dependencias de Node** (Tailwind y Monaco): `npm install --prefix src/LinqRover.Rustino` (una vez en cada clon).
- **Lanzar un ejecutable desde Git Bash:** con `&` en segundo plano, a veces no aparece en `tasklist`. Para comprobarlo, mejor `timeout` en primer plano (código 124 = sigue en marcha).
- **No se hacen commits ni push sin que el usuario lo pida.** Las ramas se crean desde `main`; los cambios se fusionan con `--no-ff`.
- Escribir al usuario en español y sin preámbulos.
