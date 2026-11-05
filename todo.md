# TODO — LinqRover

Estado al cierre de la sesión de hoy.

## Estado del repositorio

- **Rama activa:** `feature/visual-query-builder` (sin fusionar en `main`, sin subir al remoto).
- **Último commit:** `7fb3ca2` — Validar solo el tipo base en las uniones del editor visual.
- **Rama `main`:** `00a68a8` (incluye el rediseño de pestañas y la barra lateral). Sincronizada con `origin/main`.
- **Cambios sin commitear:** ninguno de código. Hay un `todo.md` nuevo (este archivo).
- **Archivo no creado:** `src/LinqRover.Core/Query/QueryDesign.cs` (el intento de hoy falló por un heredoc de bash; el contenido está descrito en la sección 1).
- **Archivos generados que cambian al compilar:** `src/LinqRover/wwwroot/css/app.css` (lo regenera Tailwind; no es un cambio real).

## Estado funcional

### Hecho y probado por el usuario
- Conexiones SQL Server y Oracle, metadatos, visor paginado de 30 filas.
- Editor SQL con Ctrl+Enter (`addAction`, un atajo por editor).
- Pestañas de consulta desde el botón «+»: SQL, EF Script y Editor visual.
- Barra lateral de tablas colapsable, con el nombre de la conexión encima del selector de esquema.
- Modal de tabla (Datos, Esquema, Diagrama) al elegir una tabla.
- EF Script: autocompletado con Roslyn (probado por el usuario), botón «Ver SQL» en modal, «Generando...» mientras genera.
- Oracle 11g: `Take`, `Skip` y su combinación con ROWNUM; `Any`/`All`/`Contains` (literales booleanos como 1/0); columnas NOT NULL generadas como no anulables.
- Editor visual (biblioteca `LinqRover.QueryBuilder`): añadir tablas, arrastrar tablas desde la lista (eventos de puntero, `wwwroot/js/drag-drop.js`), relacionar columnas (enlaces), eliminar selección, nodo seleccionado resaltado.
- Validación de uniones: tipo base igual (`NUMBER(2)` con `NUMBER(10)` se une; `NUMBER` con `VARCHAR2` no), sin repetir la misma unión. PK con PK permitido.

### Pendiente de confirmar
- Probar en Oracle 19c o superior (el generador ROWNUM se usa en todas las versiones; solo se ha probado en 11g).
- `ORA-03115` con `Any()` sin correlación dentro de un `Where` en 11g (EF pasa un booleano como parámetro que el driver no admite). Sin resolver.
- `FirstOrDefault` sin resultados muestra 0 filas sin indicar que el valor es null (mejora de UX, no hecha).

## Próxima tarea: formulario de consulta visual

### Decisiones tomadas
1. Formulario a la **derecha** del editor de diagramas (ancho fijo ~320 px, scroll propio).
2. Campos: **Select** (columnas), **Where** (condiciones con AND/OR), **GroupBy**, **Agregaciones** (COUNT, SUM, AVG, MIN, MAX con alias), **Skip**, **Take**, **Limit**.
3. Tres botones al final: **Generar SQL**, **Generar EF Script**, **Ejecutar consulta**.
4. Tipo de join: **INNER JOIN** para todos los enlaces del diagrama.
5. Resultado de **Ejecutar**: se muestra **debajo del diagrama**.

### Decisiones a confirmar (asumidas hoy, revisar mañana)
- **Tabla base:** la primera tabla añadida al diagrama (`Sources[0]`).
- **Limit vs Take:** se asume que `Limit` es sinónimo de `Take` cuando `Take` está vacío (`Take ?? Limit`). Confirmar si `Limit` debe ser otra cosa.
- **Salida de Generar SQL / Generar EF Script:** se muestra en un `<pre>` debajo de los botones del formulario (no se abre una pestaña nueva).
- **Ejecutar:** ejecuta siempre el SQL generado, aunque el usuario haya pedido EF Script.
- **Agregaciones sin GroupBy:** se devuelve una sola fila (`COUNT`, `SUM`... sobre toda la consulta). Con columnas seleccionadas y sin GroupBy se rechaza.

### 1. Núcleo (LinqRover.Core/Query) — pendiente de crear
Archivos a crear:

- **`QueryDesign.cs`**
  - Enums: `AggregateFunction { Count, Sum, Avg, Min, Max }`, `FilterOperator { Equals, NotEquals, Greater, GreaterOrEqual, Less, LessOrEqual, Like, IsNull, IsNotNull }`.
  - Records: `QuerySource(Id, Schema, Table, Columns)`, `ColumnRef(SourceId, Column)`, `QueryJoin(Left, Right)`, `WhereItem(Column, Operator, Value, Or)`, `AggregateItem(Function, Column?, Alias)`, `QueryDesign(Sources, Joins, Select, Where, GroupBy, Aggregates, Skip, Take, Limit)`, `OutputItem(Alias, Column?, Aggregate?)`, `JoinStep(SourceId, Conditions)`.
  - `QueryDesignPlan`:
    - `Validate(design)`: columnas existentes, alias de agregación válido (`^[A-Za-z_][A-Za-z0-9_]*$`) y sin repetir, reglas de agregaciones y de números no negativos.
    - `PlanJoins(design)`: ordena los joins desde la base; si una tabla no se alcanza, error «no está unida a la tabla base».
    - `Outputs(design)`: columnas de salida. Sin Select ni agregaciones salen todas las columnas. Alias duplicados se prefijan con el alias de la tabla (`t1_ID`).
    - `AliasMap(design)`: `t0`, `t1`, ... por posición en `Sources`.
  - `QueryLiterals`: `Sql(engine, value, column)` y `CSharp(value, column)`. Numéricos (validados; en C# con sufijo `m`), fechas (ISO; `TO_DATE` en Oracle; `new DateTime(...)` en C#), texto con comillas escapadas.

- **`QueryDesignSql.cs`** — `Build(engine, design)`:
  - `SELECT` con todas las salidas aliasadas (`expr AS "alias"`).
  - `FROM base alias` + `INNER JOIN tabla alias ON ...` en el orden de `PlanJoins`.
  - `WHERE` encadenado por la izquierda con paréntesis: `((c1) OR c2) AND c3`.
  - `GROUP BY`.
  - Paginación:
    - SQL Server, sin Skip: `SELECT TOP (n)`. Con Skip: `ORDER BY (SELECT NULL) OFFSET s ROWS [FETCH NEXT n ROWS ONLY]`.
    - Oracle, sin Skip: `SELECT * FROM (...) WHERE ROWNUM <= n`. Con Skip: `SELECT aliases FROM (SELECT a__.*, ROWNUM AS "RN__" FROM (...) a__ [WHERE ROWNUM <= s+n]) WHERE "RN__" > s`.
  - Quoting: `[x]` en SQL Server, `"x"` en Oracle.

- **`QueryDesignLinq.cs`** — `Build(design)` (script EF):
  - Base: `var query = db.<ContextMemberName(tabla)>`.
  - Joins: `.Join(db.<tabla>, r => new { K0 = ... }, t => new { K0 = t.X }, (r, t) => ...)`. El primer join usa `r` como entidad y crea `new { t0 = r, t1 = t }`; los siguientes usan `new { r.t0, r.t1, tN = t }`.
  - Accesos: sin joins `r.COL`; con joins `r.tN.COL`. Nombres de miembros con `ClassGenerator.Identifier`.
  - `Where` encadenado con paréntesis; `EF.Functions.Like` para LIKE; `== null` / `!= null` para IS NULL / IS NOT NULL.
  - Sin agregaciones: `.Select(r => new { Alias = ... })`, luego `.Skip(s)` y `.Take(n)`, y `return query;`.
  - Con GroupBy: `.GroupBy(r => new { G0 = ... })` y `.Select(g => new { Alias = g.Key.Gi, Agg = g.Count() / g.Sum(r => ...) })`.
  - Agregaciones sin GroupBy: `var query = ...;` y `return new { Alias = query.Count(), ... };`.

- **Pruebas:** comprobar los tres generadores contra el 11g local (`DOCENTIA`, tablas `CURSOS`, `MATRICULAS`, `DIMENSIONES`, `PROFESORES`; para las uniones, `DOCENTES` con `DIMENSIONES` o `CURSOS` con `MATRICULAS`). Ver la sección de entorno local.

### 2. Biblioteca `LinqRover.QueryBuilder` — pendiente
- **`Editor/QueryDesignPanel.razor`** (formulario):
  - Parámetros: `Sources`, `Joins` (desde el diagrama), `Engine`, `OnExecute` (EventCallback<string> con el SQL).
  - Secciones: Select (casillas agrupadas por tabla), Where (filas: columna, operador, valor, AND/OR), GroupBy (casillas), Agregaciones (filas: función, columna, alias), Skip / Take / Limit (números).
  - Botones: Generar SQL, Generar EF Script, Ejecutar consulta. Salida en `<pre>` debajo de los botones (SQL o EF según el último botón).
  - Construye un `QueryDesign` desde el estado del formulario y llama a los generadores de Core.
- **`Editor/QueryDiagramEditor.razor`** (modificar):
  - Layout: a la izquierda el diagrama con la barra de herramientas, a la derecha `QueryDesignPanel`.
  - Calcula `Sources` (nodos: `Id`, esquema, tabla, columnas) y `Joins` (enlaces entre dos `ColumnPort`) a partir del diagrama.
  - Nuevos parámetros: `OnExecute` (EventCallback<string>) y `ResultContent` (RenderFragment) que se muestra debajo del diagrama.
  - `TableNodeModel.Id` puede servir como `Id` de la fuente (comprobar que es estable).

### 3. App (`LinqRover`) — pendiente
- **`Components/Pages/Workspace.razor`:**
  - `QueryDocument` pasa a tener propiedades mutables para el resultado: `Result`, `ResultKey`, `Status`, `Error`.
  - `RunVisualAsync(doc, sql)`: ejecuta con `Providers.Get(engine).ExecuteReadOnlyAsync` (límite `MaxRows`), guarda el resultado en el documento.
  - `LoadDocPage(doc, pageIndex, pageSize)`: paginación del resultado (como `LoadResultPage`).
  - En la pestaña visual: pasar `OnExecute` y `ResultContent` (`DataViewer` con `@key="doc.ResultKey"`).

### Criterios de aceptación
- Con dos tablas unidas por un enlace, «Generar SQL» produce `INNER JOIN ... ON` correcto y se ejecuta en el 11g.
- «Generar EF Script» produce un script que compila y devuelve las mismas filas que el SQL.
- Where con AND/OR, GroupBy y COUNT/SUM funcionan en SQL y en EF.
- Skip y Take funcionan en Oracle 11g y SQL Server.
- «Ejecutar» muestra los resultados debajo del diagrama, con paginación de 30 filas.
- Un valor con comilla simple no rompe la consulta (escapado).
- Una columna inexistente o una tabla no unida da un error legible, no una excepción sin contexto.

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
