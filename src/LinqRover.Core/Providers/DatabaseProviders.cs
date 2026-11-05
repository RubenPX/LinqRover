using LinqRover.Core.Models;

namespace LinqRover.Core.Providers;

/// <summary>Registro de proveedores por motor.</summary>
public sealed class DatabaseProviders
{
    private readonly Dictionary<DatabaseEngine, IDatabaseProvider> _providers;

    public DatabaseProviders(IEnumerable<IDatabaseProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.Engine);
    }

    public static DatabaseProviders Create() =>
        new([new OracleDatabaseProvider(), new SqlServerDatabaseProvider()]);

    public IDatabaseProvider Get(DatabaseEngine engine) =>
        _providers.TryGetValue(engine, out var provider)
            ? provider
            : throw new NotSupportedException($"Motor no soportado: {engine}");
}
