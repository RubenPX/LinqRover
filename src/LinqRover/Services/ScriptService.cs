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

    public async Task<QueryResult> RunAsync(
        DbConnectionProfile profile,
        string schema,
        string code,
        int maxRows,
        CancellationToken ct)
    {
        var provider = providers.Get(profile.Engine);
        var tables = new List<DynamicTable>();
        foreach (var table in await provider.GetTablesAsync(profile, schema, ct))
        {
            var columns = await provider.GetColumnsAsync(profile, schema, table.Name, ct);
            tables.Add(new DynamicTable(schema, table.Name,
                columns.Select(c => new DynamicColumn(c.Name, c.DataType)).ToList()));
        }

        var model = GetOrCompile(profile, tables);

        var connectionString = profile.Engine switch
        {
            DatabaseEngine.SqlServer => SqlServerDatabaseProvider.BuildConnectionString(profile),
            DatabaseEngine.Oracle => OracleDatabaseProvider.BuildConnectionString(profile),
            _ => throw new NotSupportedException($"Motor no soportado para scripts: {profile.Engine}"),
        };

        return await TypedScriptRunner.ExecuteAsync(profile.Engine, connectionString, model, code, maxRows, ct);
    }

    private static CompiledModel GetOrCompile(DbConnectionProfile profile, IReadOnlyList<DynamicTable> tables)
    {
        // La clave incluye el perfil y la estructura de las tablas: si cambia una columna, se vuelve a compilar.
        var structure = string.Join("|", tables.Select(t =>
            t.Key + ":" + string.Join(",", t.Columns.Select(c => c.Name + "=" + c.SqlType))));
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
