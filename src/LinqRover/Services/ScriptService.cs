using LinqRover.Core.Models;
using LinqRover.Core.Providers;
using LinqRover.Core.Scripting;

namespace LinqRover.Services;

/// <summary>Lee las tablas y columnas del esquema y ejecuta un script de EF Core sobre la conexión.</summary>
public sealed class ScriptService(DatabaseProviders providers)
{
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

        var connectionString = profile.Engine switch
        {
            DatabaseEngine.SqlServer => SqlServerDatabaseProvider.BuildConnectionString(profile),
            DatabaseEngine.Oracle => OracleDatabaseProvider.BuildConnectionString(profile),
            _ => throw new NotSupportedException($"Motor no soportado para scripts: {profile.Engine}"),
        };

        return await ScriptRunner.ExecuteAsync(profile.Engine, connectionString, tables, code, maxRows, ct);
    }
}
