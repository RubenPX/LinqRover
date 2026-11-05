using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using LinqRover.Core.Models;
using LinqRover.Core.Providers;
using LinqRover.Core.Scripting;

namespace LinqRover.Services;

/// <summary>
/// Lee las tablas y columnas del esquema, genera y compila sus clases (una vez por esquema) y ejecuta el script
/// contra el contexto tipado.
/// </summary>
public sealed class ScriptService(DatabaseProviders providers)
{
    // Modelos compilados. Se reutilizan mientras el esquema no cambie.
    private static readonly ConcurrentDictionary<string, CompiledModel> Models = new();

    // Autocompletado: el esquema se lee una vez y se reutiliza unos minutos (no se consulta la base en cada tecla).
    private static readonly ConcurrentDictionary<string, (DateTime Created, Task<ScriptCompletion> Task)> Completions = new();
    private static readonly TimeSpan CompletionTtl = TimeSpan.FromMinutes(5);

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

    /// <summary>Prepara el autocompletado del esquema (lectura de metadatos y compilación) sin esperar a una tecla.</summary>
    public Task PrepareCompletionAsync(DbConnectionProfile profile, string schema) =>
        GetCompletionAsync(profile, schema);

    /// <summary>Sugerencias de C# (Roslyn) para el texto del script en la posición indicada.</summary>
    public async Task<IReadOnlyList<CompletionSuggestion>> CompleteAsync(
        DbConnectionProfile profile, string schema, string code, int offset)
    {
        var completion = await GetCompletionAsync(profile, schema);
        return await completion.GetAsync(code, offset);
    }

    private Task<ScriptCompletion> GetCompletionAsync(DbConnectionProfile profile, string schema)
    {
        var key = profile.Id + "|" + profile.Engine + "|" + schema;
        if (Completions.TryGetValue(key, out var entry) && DateTime.UtcNow - entry.Created < CompletionTtl)
            return entry.Task;

        var task = BuildCompletionAsync(profile, schema, key);
        Completions[key] = (DateTime.UtcNow, task);
        return task;
    }

    private async Task<ScriptCompletion> BuildCompletionAsync(DbConnectionProfile profile, string schema, string key)
    {
        try
        {
            // Sin token de cancelación: la caché no debe guardar un fallo por una petición cancelada.
            var model = await LoadModelAsync(profile, schema, CancellationToken.None);
            // Crear el workspace de Roslyn también es costoso: fuera del hilo de la interfaz.
            return await Task.Run(() => new ScriptCompletion(model));
        }
        catch
        {
            Completions.TryRemove(key, out _);
            throw;
        }
    }

    /// <summary>Lee las tablas del esquema y devuelve su modelo compilado (reutilizado si la estructura no cambia).</summary>
    private async Task<CompiledModel> LoadModelAsync(DbConnectionProfile profile, string schema, CancellationToken ct)
    {
        var provider = providers.Get(profile.Engine);
        var tables = new List<DynamicTable>();
        // ConfigureAwait(false): la lectura de metadatos no debe volver al hilo de la interfaz.
        foreach (var table in await provider.GetTablesAsync(profile, schema, ct).ConfigureAwait(false))
        {
            var columns = await provider.GetColumnsAsync(profile, schema, table.Name, ct).ConfigureAwait(false);
            tables.Add(new DynamicTable(schema, table.Name,
                columns.Select(c => new DynamicColumn(c.Name, c.DataType, c.IsNullable)).ToList()));
        }

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
