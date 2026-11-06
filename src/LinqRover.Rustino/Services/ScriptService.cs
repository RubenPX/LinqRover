using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using LinqRover.Core.Models;
using LinqRover.Core.Providers;
using LinqRover.Core.Scripting;

namespace LinqRover.Services;

/// <summary>Estado de una carga: etapa actual y, si se conoce, cuántos elementos lleva (Total 0 = sin total).</summary>
public sealed record LoadSnapshot(string Stage, int Done, int Total);

/// <summary>
/// Progreso de una carga en segundo plano. Se puede observar desde varios componentes: <see cref="Changed"/> se
/// dispara desde el hilo que carga, así que los suscriptores deben volver al hilo de la interfaz.
/// </summary>
public sealed class LoadProgress
{
    private volatile LoadSnapshot _current = new("", 0, 0);

    public LoadSnapshot Current => _current;

    public event Action? Changed;

    public void Report(string stage, int done = 0, int total = 0)
    {
        _current = new LoadSnapshot(stage, done, total);
        Changed?.Invoke();
    }
}

/// <summary>
/// Lee las tablas y columnas del esquema, genera y compila sus clases (una vez por esquema) y ejecuta el script
/// contra el contexto tipado.
/// </summary>
public sealed class ScriptService(DatabaseProviders providers)
{
    // Modelos compilados. Se reutilizan mientras el esquema no cambie.
    private static readonly ConcurrentDictionary<string, CompiledModel> Models = new();

    // Autocompletado: el esquema se lee una vez y se reutiliza unos minutos (no se consulta la base en cada tecla).
    private static readonly ConcurrentDictionary<string, CompletionEntry> Completions = new();
    private static readonly TimeSpan CompletionTtl = TimeSpan.FromMinutes(5);

    private sealed record CompletionEntry(DateTime Created, Task<ScriptCompletion> Task, LoadProgress Progress);

    public async Task<QueryResult> RunAsync(
        DbConnectionProfile profile,
        string schema,
        string code,
        int maxRows,
        CancellationToken ct)
    {
        var model = await LoadModelAsync(profile, schema, ct);

        return await TypedScriptRunner.ExecuteAsync(profile.Engine, ConnectionString(profile), model, code, maxRows, ct);
    }

    /// <summary>SQL que generaría EF Core para el script, sin ejecutarlo.</summary>
    public async Task<string> GenerateSqlAsync(DbConnectionProfile profile, string schema, string code, CancellationToken ct)
    {
        var model = await LoadModelAsync(profile, schema, ct);
        return await TypedScriptRunner.GenerateSqlAsync(profile.Engine, ConnectionString(profile), model, code, ct);
    }

    private static string ConnectionString(DbConnectionProfile profile) => profile.Engine switch
    {
        DatabaseEngine.SqlServer => SqlServerDatabaseProvider.BuildConnectionString(profile),
        DatabaseEngine.Oracle => OracleDatabaseProvider.BuildConnectionString(profile),
        _ => throw new NotSupportedException($"Motor no soportado para scripts: {profile.Engine}"),
    };

    /// <summary>
    /// Prepara el autocompletado del esquema sin esperar a una tecla. Devuelve la tarea y, si todavía se está
    /// cargando, el progreso para mostrarlo (null si ya estaba listo en caché).
    /// </summary>
    public (Task Task, LoadProgress? Progress) PrepareCompletion(DbConnectionProfile profile, string schema)
    {
        var entry = GetCompletion(profile, schema);
        return (entry.Task, entry.Task.IsCompleted ? null : entry.Progress);
    }

    /// <summary>Sugerencias de C# (Roslyn) para el texto del script en la posición indicada.</summary>
    public async Task<IReadOnlyList<CompletionSuggestion>> CompleteAsync(
        DbConnectionProfile profile, string schema, string code, int offset)
    {
        var completion = await GetCompletion(profile, schema).Task;
        return await completion.GetAsync(code, offset);
    }

    private CompletionEntry GetCompletion(DbConnectionProfile profile, string schema)
    {
        var key = profile.Id + "|" + profile.Engine + "|" + schema;
        if (Completions.TryGetValue(key, out var entry))
        {
            if (DateTime.UtcNow - entry.Created < CompletionTtl)
                return entry;

            // Caducada: se libera el workspace de Roslyn que se sustituye.
            if (entry.Task.IsCompletedSuccessfully)
                entry.Task.Result.Dispose();
        }

        var progress = new LoadProgress();
        var task = BuildCompletionAsync(profile, schema, key, progress);
        var created = new CompletionEntry(DateTime.UtcNow, task, progress);
        Completions[key] = created;
        return created;
    }

    private async Task<ScriptCompletion> BuildCompletionAsync(
        DbConnectionProfile profile, string schema, string key, LoadProgress progress)
    {
        try
        {
            // Sin token de cancelación: la caché no debe guardar un fallo por una petición cancelada.
            var model = await LoadModelAsync(profile, schema, CancellationToken.None, progress);
            // Crear el workspace de Roslyn también es costoso: fuera del hilo de la interfaz.
            progress.Report("Preparando Roslyn…");
            return await Task.Run(() => new ScriptCompletion(model));
        }
        catch
        {
            Completions.TryRemove(key, out _);
            throw;
        }
    }

    /// <summary>Lee las tablas del esquema y devuelve su modelo compilado (reutilizado si la estructura no cambia).</summary>
    private async Task<CompiledModel> LoadModelAsync(
        DbConnectionProfile profile, string schema, CancellationToken ct, LoadProgress? progress = null)
    {
        var provider = providers.Get(profile.Engine);
        progress?.Report("Leyendo tablas…");
        // ConfigureAwait(false): la lectura de metadatos no debe volver al hilo de la interfaz.
        var tableInfos = await provider.GetTablesAsync(profile, schema, ct).ConfigureAwait(false);

        var tables = new List<DynamicTable>(tableInfos.Count);
        for (var i = 0; i < tableInfos.Count; i++)
        {
            var table = tableInfos[i];
            progress?.Report("Leyendo columnas", i, tableInfos.Count);
            var columns = await provider.GetColumnsAsync(profile, schema, table.Name, ct).ConfigureAwait(false);
            tables.Add(new DynamicTable(schema, table.Name,
                columns.Select(c => new DynamicColumn(c.Name, c.DataType, c.IsNullable)).ToList()));
        }

        progress?.Report("Compilando clases…");
        // La compilación con Roslyn consume CPU durante segundos: se hace fuera del hilo de la interfaz.
        return await Task.Run(() => GetOrCompile(profile, tables), CancellationToken.None).ConfigureAwait(false);
    }

    private static CompiledModel GetOrCompile(DbConnectionProfile profile, IReadOnlyList<DynamicTable> tables)
    {
        // La clave incluye el perfil y la estructura de las tablas: si cambia una columna, se vuelve a compilar.
        var structure = string.Join("|", tables.Select(t =>
            t.Key + ":" + string.Join(",", t.Columns.Select(c => c.Name + "=" + c.SqlType + (c.IsNullable ? "" : "!")))));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile.Id + "|" + profile.Engine + "|" + structure)));

        if (Models.TryGetValue(key, out var cached))
            return cached;

        if (Models.Count > 20)
            Models.Clear(); // límite simple para no acumular ensamblados

        var model = TypedScriptRunner.Compile(profile.Engine, tables);
        Models[key] = model;
        return model;
    }
}
