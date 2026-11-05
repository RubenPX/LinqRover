using LinqRover.Core.Models;
using LinqRover.Core.Sql;
using Microsoft.Data.SqlClient;

namespace LinqRover.Core.Providers;

/// <summary>
/// SQL Server mediante Microsoft.Data.SqlClient. Metadatos desde el catálogo sys.*.
/// Autenticación con usuario y contraseña de SQL Server (sin autenticación de Windows).
/// Paginación con OFFSET/FETCH (SQL Server 2012+), ordenada por clave primaria o por el primer índice único.
/// </summary>
public sealed class SqlServerDatabaseProvider : IDatabaseProvider
{
    private const string DefaultSchema = "dbo";
    private static readonly string[] SystemDatabases = ["master", "model", "msdb", "tempdb"];

    public DatabaseEngine Engine => DatabaseEngine.SqlServer;

    public string GetDefaultSchema(DbConnectionProfile profile) => DefaultSchema;

    public bool CanListDatabases => true;

    public async Task<IReadOnlyList<string>> GetDatabasesAsync(DbConnectionProfile profile, CancellationToken ct = default)
    {
        // Sin base inicial: se conecta al servidor y se listan sus bases.
        await using var conn = await OpenAsync(profile, ct, useDatabase: false);
        await using var cmd = new SqlCommand(
            "SELECT name FROM sys.databases WHERE state_desc = N'ONLINE' ORDER BY name", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var all = await ReaderHelpers.ReadStringsAsync(reader, ct);
        return all.Where(n => !SystemDatabases.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    public async Task TestConnectionAsync(DbConnectionProfile profile, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(profile, ct);
        await using var cmd = new SqlCommand("SELECT 1", conn);
        await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetSchemasAsync(DbConnectionProfile profile, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(profile, ct);
        await using var cmd = new SqlCommand(
            """
            SELECT DISTINCT s.name
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            ORDER BY s.name
            """, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await ReaderHelpers.ReadStringsAsync(reader, ct);
    }

    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(DbConnectionProfile profile, string schema, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(profile, ct);
        await using var cmd = new SqlCommand(
            """
            SELECT t.name
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            WHERE s.name = @schema
            ORDER BY t.name
            """, conn);
        cmd.Parameters.AddWithValue("@schema", schema);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var names = await ReaderHelpers.ReadStringsAsync(reader, ct);
        return names.Select(n => new TableInfo(schema, n)).ToList();
    }

    public async Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(DbConnectionProfile profile, string schema, string table, CancellationToken ct = default)
    {
        const string sql = """
            SELECT c.name,
                   TYPE_NAME(c.user_type_id) AS type_name,
                   c.max_length,
                   c.precision,
                   c.scale,
                   c.is_nullable,
                   CASE WHEN EXISTS (
                            SELECT 1
                            FROM sys.indexes i
                            JOIN sys.index_columns ic
                              ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                            WHERE i.object_id = c.object_id
                              AND i.is_primary_key = 1
                              AND ic.column_id = c.column_id)
                        THEN 1 ELSE 0 END AS is_pk
            FROM sys.columns c
            WHERE c.object_id = OBJECT_ID(QUOTENAME(@schema) + N'.' + QUOTENAME(@table))
            ORDER BY c.column_id
            """;

        await using var conn = await OpenAsync(profile, ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);

        var result = new List<ColumnInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = ReaderHelpers.Str(reader, 0);
            var typeName = ReaderHelpers.Str(reader, 1);
            var maxLength = ReaderHelpers.Int(reader, 2);
            var precision = ReaderHelpers.Int(reader, 3);
            var scale = ReaderHelpers.Int(reader, 4);
            var nullable = ReaderHelpers.Int(reader, 5) == 1;
            var isPk = ReaderHelpers.Int(reader, 6) == 1;

            result.Add(new ColumnInfo(name, FormatType(typeName, maxLength, precision, scale), isPk, nullable));
        }
        return result;
    }

    public async Task<PageResult> GetPageAsync(DbConnectionProfile profile, string schema, string table, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        var qualified = $"{Quote(schema)}.{Quote(table)}";

        await using var conn = await OpenAsync(profile, ct);

        var keys = await GetOrderKeysAsync(conn, schema, table, ct);
        // Sin clave ni índice único el orden no está garantizado: se avisa en la documentación del proyecto.
        var orderBy = keys.Count > 0 ? string.Join(", ", keys.Select(Quote)) : "(SELECT NULL)";

        long total;
        await using (var countCmd = new SqlCommand($"SELECT COUNT_BIG(*) FROM {qualified}", conn))
        {
            total = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct));
        }

        var pageSql = $"SELECT * FROM {qualified} ORDER BY {orderBy} OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY";
        await using var cmd = new SqlCommand(pageSql, conn);
        cmd.Parameters.AddWithValue("@skip", pageIndex * pageSize);
        cmd.Parameters.AddWithValue("@take", pageSize);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var (columns, rows) = await ReaderHelpers.ReadPageAsync(reader, pageSize, ct);

        return new PageResult(columns, rows, total, pageIndex, pageSize);
    }

    /// <summary>Columnas de la clave primaria o, si no hay, del primer índice único.</summary>
    private static async Task<List<string>> GetOrderKeysAsync(SqlConnection conn, string schema, string table, CancellationToken ct)
    {
        const string sql = """
            WITH best AS (
                SELECT TOP (1) i.object_id, i.index_id
                FROM sys.indexes i
                WHERE i.object_id = OBJECT_ID(QUOTENAME(@schema) + N'.' + QUOTENAME(@table))
                  AND i.is_unique = 1
                  AND i.is_disabled = 0
                ORDER BY i.is_primary_key DESC, i.index_id
            )
            SELECT c.name
            FROM best b
            JOIN sys.index_columns ic
              ON ic.object_id = b.object_id AND ic.index_id = b.index_id AND ic.is_included_column = 0
            JOIN sys.columns c
              ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            ORDER BY ic.key_ordinal
            """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@schema", schema);
        cmd.Parameters.AddWithValue("@table", table);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await ReaderHelpers.ReadStringsAsync(reader, ct);
    }

    public async Task<QueryResult> ExecuteReadOnlyAsync(DbConnectionProfile profile, string sql, int maxRows, CancellationToken ct = default)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await using var conn = await OpenAsync(profile, ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);

        QueryResult result;
        await using (var cmd = new SqlCommand(SqlSafety.Normalize(sql), conn, tx) { CommandTimeout = 60 })
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            result = await ReaderHelpers.ReadResultAsync(reader, maxRows, reader.RecordsAffected, clock, ct);
        }

        // Se deshace aunque la consulta haya escrito algo.
        await tx.RollbackAsync(ct);
        return result;
    }

    private static async Task<SqlConnection> OpenAsync(DbConnectionProfile profile, CancellationToken ct, bool useDatabase = true)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"{profile.Host},{profile.Port}",
            UserID = profile.User,
            Password = profile.Password,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = profile.TrustServerCertificate,
            ConnectTimeout = 15,
            ApplicationName = "LinqRover",
        };
        if (useDatabase && !string.IsNullOrWhiteSpace(profile.Database))
            builder.InitialCatalog = profile.Database;

        var conn = new SqlConnection(builder.ConnectionString);
        try
        {
            await conn.OpenAsync(ct);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    private static string Quote(string identifier) => "[" + identifier.Replace("]", "]]") + "]";

    private static string FormatType(string typeName, int? maxLength, int? precision, int? scale) => typeName switch
    {
        "nvarchar" or "nchar" when maxLength == -1 => $"{typeName}(max)",
        "nvarchar" or "nchar" when maxLength is not null => $"{typeName}({maxLength / 2})",
        "varchar" or "char" or "varbinary" or "binary" when maxLength == -1 => $"{typeName}(max)",
        "varchar" or "char" or "varbinary" or "binary" when maxLength is not null => $"{typeName}({maxLength})",
        "decimal" or "numeric" when precision is not null => $"{typeName}({precision},{scale})",
        _ => typeName,
    };
}
