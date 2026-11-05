using LinqRover.Core.Models;

namespace LinqRover.Core.Providers;

/// <summary>Contrato común para cada motor. Cada implementación usa el diccionario/catálogo de su propio motor.</summary>
public interface IDatabaseProvider
{
    DatabaseEngine Engine { get; }

    /// <summary>Esquema que se selecciona por defecto al conectar.</summary>
    string GetDefaultSchema(DbConnectionProfile profile);

    /// <summary>Indica si el motor permite listar bases de datos antes de elegir una (SQL Server sí; Oracle no).</summary>
    bool CanListDatabases { get; }

    Task TestConnectionAsync(DbConnectionProfile profile, CancellationToken ct = default);

    /// <summary>Lista las bases de datos del servidor. Ignora el campo Database del perfil.</summary>
    Task<IReadOnlyList<string>> GetDatabasesAsync(DbConnectionProfile profile, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetSchemasAsync(DbConnectionProfile profile, CancellationToken ct = default);

    Task<IReadOnlyList<TableInfo>> GetTablesAsync(DbConnectionProfile profile, string schema, CancellationToken ct = default);

    Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(DbConnectionProfile profile, string schema, string table, CancellationToken ct = default);

    /// <summary>
    /// Ejecuta SQL libre dentro de una transacción que siempre se deshace: ningún cambio llega a guardarse.
    /// Devuelve como máximo <paramref name="maxRows"/> filas.
    /// </summary>
    Task<QueryResult> ExecuteReadOnlyAsync(DbConnectionProfile profile, string sql, int maxRows, CancellationToken ct = default);

    /// <summary>Devuelve una página de filas, con <paramref name="pageSize"/> filas como máximo.</summary>
    Task<PageResult> GetPageAsync(DbConnectionProfile profile, string schema, string table, int pageIndex, int pageSize, CancellationToken ct = default);
}
