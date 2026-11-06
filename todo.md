# TODO — LinqRover

Estado al cierre de la sesión de hoy.

## Estado del repositorio

- **Rama activa:** `feature/visual-query-builder` (sin fusionar en `main`, sin subir al remoto).
- **Último commit:** `7fb3ca2` — Validar solo el tipo base en las uniones del editor visual.
- **Rama `main`:** `00a68a8` (incluye el rediseño de pestañas y la barra lateral). Sincronizada con `origin/main`.
- **Cambios sin commitear:** formulario de consulta visual (`src/LinqRover.Core/Query/`, `QueryDesignPanel.razor`, `CollapsibleSection.razor`, cambios en el editor visual y en `Workspace.razor`/`TablesPanel.razor`), estilo de `#blazor-error-ui` en `Styles/app.css`, y este `todo.md`.
- **Archivos generados que cambian al compilar:** `src/LinqRover/wwwroot/css/app.css` (lo regenera Tailwind; no es un cambio real).

## Estado funcional

### Hecho y probado por el usuario
- Conexiones SQL Server y Oracle, metadatos, visor paginado de 30 filas.
- Editor SQL con Ctrl+Enter (`addAction`, un atajo por editor).
- Pestañas de consulta desde el botón «+»: SQL, EF Script y Editor visual.
- Barra lateral de tablas colapsable, con el nombre de la conexión encima del selector de esquema. Al pasar el ratón por una tabla, tooltip a la derecha con sus columnas y tipos.
- Modal de tabla (Datos, Esquema, Diagrama) al elegir una tabla.
- EF Script: autocompletado con Roslyn (probado por el usuario), botón «Ver SQL» en modal, «Generando...» mientras genera.
- Oracle 11g: `Take`, `Skip` y su combinación con ROWNUM; `Any`/`All`/`Contains` (literales booleanos como 1/0); columnas NOT NULL generadas como no anulables.
- Editor visual (biblioteca `LinqRover.QueryBuilder`): añadir tablas arrastrándolas desde la lista (eventos de puntero, `wwwroot/js/drag-drop.js`), relacionar columnas (enlaces), nodo seleccionado resaltado.
- Validación de uniones: tipo base igual (`NUMBER(2)` con `NUMBER(10)` se une; `NUMBER` con `VARCHAR2` no), sin repetir la misma unión. PK con PK permitido.
- Formulario de consulta visual a la derecha del diagrama (panel a todo el alto, secciones colapsables, Select y Where expandidos por defecto):
  - Select con orden de columnas (↑ / ↓), GroupBy, Agregaciones (COUNT, SUM, AVG, MIN, MAX con alias), Where con AND/OR reordenable (↑ / ↓), Skip / Take / Limit.
  - Generar SQL y Generar EF Script abren un modal; Ejecutar consulta muestra el resultado (1000 filas máximo, paginado de 30) debajo del diagrama.
  - Generadores en `LinqRover.Core/Query` (`QueryDesign`, `QueryDesignSql`, `QueryDesignLinq`).
  - Decisiones: tabla base = primera tabla añadida; `Take ?? Limit`; Ejecutar usa siempre SQL; agregaciones sin GroupBy devuelven una fila y no admiten columnas sueltas ni Skip/Take.

### Pendiente de confirmar
- Ejecución del formulario contra Oracle 11g (generado y revisado a mano, no ejecutado) y contra SQL Server.
- EF Script generado por el formulario: compilarlo y comparar sus filas con el SQL.
- Probar en Oracle 19c o superior (el generador ROWNUM se usa en todas las versiones; solo se ha probado en 11g).
- `ORA-03115` con `Any()` sin correlación dentro de un `Where` en 11g (EF pasa un booleano como parámetro que el driver no admite). Sin resolver.
- `FirstOrDefault` sin resultados muestra 0 filas sin indicar que el valor es null (mejora de UX, no hecha).

## Tareas pendientes de siempre (no bloquean lo anterior)

1. **Vulnerabilidad NuGet** `System.Security.Cryptography.Pkcs` 9.0.18 (gravedad alta, dependencia transitiva en Core y en la app). Actualizar la dependencia.
2. **Contraseña de `sololectura`:** el usuario la cambiará. Actualizar `LR_PASS` en el `.env` local (no está en git).
3. **Pruebas en Oracle 19c o superior** del generador ROWNUM.
4. **`ORA-03115`** con `Any()` sin correlación en un `Where` (11g). Opciones: convertir el booleano a número en el generador, o documentarlo como limitación.
5. **`FirstOrDefault` sin resultados:** mostrar «sin resultados» en vez de 0 filas.
6. **Limpieza:** la rama `feature/visual-query-builder` debe fusionarse en `main` (`--no-ff`) y subirse, cuando el usuario lo pida. Después borrar la rama local y la remota.
7. **Monitor de workspaces de Roslyn** (ya hecho en `main`): comprobar que no hay fugas con uso prolongado.

## Entorno de pruebas local

- **Contenedor Docker:** `ora11g` (imagen `oracleinanutshell/oracle-xe-11g`), parado al final de la sesión de ayer. Arrancar con `docker start ora11g`. La base tarda en estar lista; la línea «DATABASE IS READY TO USE» **no aparece** en los logs de esta imagen. Comprobar con `sqlplus` dentro del contenedor (`system/oracle`, servicio `XE`).
- **Conexión de prueba en la app:** Oracle, `localhost:1521`, servicio `XE`, usuario `DOCENTIA` / `docentia_pwd`.
- **Usuarios de prueba:** `DOCENTIA` (dueño de las tablas) y `LECTOR` (solo `SELECT`, contraseña `lector_pwd`).
- **Tablas de prueba en DOCENTIA:**
  - `DIMENSIONES` (PK `ID_DIMENSION`), `PROFESORES` (PK `ID_PROFESOR`, `ID_DIMENSION`).
  - `CURSOS` (PK `ID_CURSO`, 12 filas), `MATRICULAS` (PK `ID_MATRICULA`, FK `ID_CURSO`, `ID_PROFESOR`, 400 filas, 40 notas nulas).
- **Regla:** no tocar producción ni los servidores de la empresa sin confirmación. El `.env` apunta a `10.215.37.21` (SQL Server de pruebas de la empresa): no usarlo para pruebas automáticas.
- **Carpetas temporales de pruebas:** `%TEMP%\lr-probe` (proyecto de consola que referencia `LinqRover.Core` y `LinqRover.QueryBuilder`). Se puede borrar cuando ya no haga falta.

## Notas de trabajo

- Compilar con `dotnet build LinqRover.slnx`. La app tiene que **pararse antes de compilar** (`taskkill //F //IM LinqRover.exe`), si no falla por DLLs bloqueadas.
- Lanzar la app: `dotnet run --project src/LinqRover/LinqRover.csproj -f net10.0-windows10.0.19041.0`.
- No se hacen commits sin que el usuario lo pida. Las ramas se crean desde `main`; los cambios se fusionan con `--no-ff`.
- Escribir al usuario en español y sin preámbulos.
