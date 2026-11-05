using LinqRover.Core.Models;
using LinqRover.Core.Sql;
using Oracle.ManagedDataAccess.Client;

namespace LinqRover.Core.Providers;

/// <summary>
/// Oracle mediante ODP.NET managed (no necesita cliente Oracle instalado).
/// Metadatos desde ALL_* y paginación con ROWNUM (compatible con 11g y superiores).
/// El campo Database del perfil es el nombre de servicio (EZConnect: host:puerto/servicio).
/// </summary>
public sealed class OracleDatabaseProvider : IDatabaseProvider
{
    public DatabaseEngine Engine => DatabaseEngine.Oracle;

    public string GetDefaultSchema(DbConnectionProfile profile) => profile.User.ToUpperInvariant();

    // En Oracle la "base" es el servicio, que hace falta para conectar: no hay lista previa.
    public bool CanListDatabases => false;

    public Task<IReadOnlyList<string>> GetDatabasesAsync(DbConnectionProfile profile, CancellationToken ct = default) =>
        throw new NotSupportedException("En Oracle la base de datos es el servicio, que se indica antes de conectar.");

    public async Task TestConnectionAsync(DbConnectionProfile profile, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(profile, ct);
        await using var cmd = new OracleCommand("SELECT 1 FROM DUAL", conn);
        await cmd.ExecuteScalarAsync(ct);
    }

    public async Task<IReadOnlyList<string>> GetSchemasAsync(DbConnectionProfile profile, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(profile, ct);
        await using var cmd = new OracleCommand("SELECT DISTINCT OWNER FROM ALL_TABLES ORDER BY OWNER", conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await ReaderHelpers.ReadStringsAsync(reader, ct);
    }

    public async Task<IReadOnlyList<TableInfo>> GetTablesAsync(DbConnectionProfile profile, string schema, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(profile, ct);
        await using var cmd = new OracleCommand(
            "SELECT TABLE_NAME FROM ALL_TABLES WHERE OWNER = :owner ORDER BY TABLE_NAME", conn) { BindByName = true };
        cmd.Parameters.Add("owner", schema);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var names = await ReaderHelpers.ReadStringsAsync(reader, ct);
        return names.Select(n => new TableInfo(schema, n)).ToList();
    }

    public async Task<IReadOnlyList<ColumnInfo>> GetColumnsAsync(DbConnectionProfile profile, string schema, string table, CancellationToken ct = default)
    {
        const string sql = """
            SELECT c.COLUMN_NAME,
                   c.DATA_TYPE,
                   c.DATA_LENGTH,
                   c.DATA_PRECISION,
                   c.DATA_SCALE,
                   c.NULLABLE,
                   CASE WHEN EXISTS (
                            SELECT 1
                            FROM ALL_CONSTRAINTS k
                            JOIN ALL_CONS_COLUMNS kc
                              ON kc.OWNER = k.OWNER AND kc.CONSTRAINT_NAME = k.CONSTRAINT_NAME
                            WHERE k.OWNER = c.OWNER
                              AND k.TABLE_NAME = c.TABLE_NAME
                              AND k.CONSTRAINT_TYPE = 'P'
                              AND kc.COLUMN_NAME = c.COLUMN_NAME)
                        THEN 1 ELSE 0 END AS IS_PK
            FROM ALL_TAB_COLUMNS c
            WHERE c.OWNER = :owner AND c.TABLE_NAME = :table
            ORDER BY c.COLUMN_ID
            """;

        await using var conn = await OpenAsync(profile, ct);
        await using var cmd = new OracleCommand(sql, conn) { BindByName = true };
        cmd.Parameters.Add("owner", schema);
        cmd.Parameters.Add("table", table);

        var result = new List<ColumnInfo>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = ReaderHelpers.Str(reader, 0);
            var dataType = ReaderHelpers.Str(reader, 1);
            var length = ReaderHelpers.Int(reader, 2);
            var precision = ReaderHelpers.Int(reader, 3);
            var scale = ReaderHelpers.Int(reader, 4);
            var nullable = ReaderHelpers.Str(reader, 5) == "Y";
            var isPk = ReaderHelpers.Int(reader, 6) == 1;

            result.Add(new ColumnInfo(name, FormatType(dataType, length, precision, scale), isPk, nullable));
        }
        return result;
    }

    public async Task<PageResult> GetPageAsync(DbConnectionProfile profile, string schema, string table, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        var qualified = $"{Quote(schema)}.{Quote(table)}";

        await using var conn = await OpenAsync(profile, ct);

        long total;
        await using (var countCmd = new OracleCommand($"SELECT COUNT(*) FROM {qualified}", conn))
        {
            total = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct));
        }

        // ROWNUM anidado en lugar de OFFSET/FETCH: OFFSET/FETCH no existe en Oracle 11g (ORA-00933).
        // ROWID da un orden estable para que la paginación no repita ni salte filas.
        // La última columna del resultado es RN (la añade la consulta), así que se descarta.
        var lower = pageIndex * pageSize;
        var upper = lower + pageSize;
        var pageSql = $"""
            SELECT * FROM (
                SELECT t.*, ROWNUM AS RN FROM (SELECT * FROM {qualified} ORDER BY ROWID) t
                WHERE ROWNUM <= :upper
            ) WHERE RN > :lower
            """;

        await using var cmd = new OracleCommand(pageSql, conn) { BindByName = true };
        cmd.Parameters.Add("upper", upper);
        cmd.Parameters.Add("lower", lower);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var (columns, rows) = await ReaderHelpers.ReadPageAsync(reader, pageSize, ct);

        columns.RemoveAt(columns.Count - 1);
        var trimmed = rows.Select(r => r[..^1]).ToList();

        return new PageResult(columns, trimmed, total, pageIndex, pageSize);
    }

    public async Task<QueryResult> ExecuteReadOnlyAsync(DbConnectionProfile profile, string sql, int maxRows, CancellationToken ct = default)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await using var conn = await OpenAsync(profile, ct);
        await using var tx = (OracleTransaction)await conn.BeginTransactionAsync(ct);

        // Transacción de solo lectura: cualquier DML falla (ORA-01456).
        await using (var ro = new OracleCommand("SET TRANSACTION READ ONLY", conn) { Transaction = tx })
            await ro.ExecuteNonQueryAsync(ct);

        QueryResult result;
        await using (var cmd = new OracleCommand(SqlSafety.Normalize(sql), conn) { Transaction = tx, CommandTimeout = 60 })
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            result = await ReaderHelpers.ReadResultAsync(reader, maxRows, reader.RecordsAffected, clock, ct);
        }

        await tx.RollbackAsync(ct);
        return result;
    }

    public async Task<IReadOnlyList<ForeignKeyRef>> GetForeignKeysAsync(DbConnectionProfile profile, string schema, string table, CancellationToken ct = default)
    {
        // Las mismas tablas/parámetros se usan en ambos lados del OR; los nombres distintos evitan problemas con BindByName.
        const string sql = """
            SELECT c.CONSTRAINT_NAME,
                   c.OWNER AS CHILD_OWNER, c.TABLE_NAME AS CHILD_TABLE, cc.COLUMN_NAME AS CHILD_COLUMN,
                   rc.OWNER AS PARENT_OWNER, rc.TABLE_NAME AS PARENT_TABLE, rcc.COLUMN_NAME AS PARENT_COLUMN
            FROM ALL_CONSTRAINTS c
            JOIN ALL_CONS_COLUMNS cc
              ON cc.OWNER = c.OWNER AND cc.CONSTRAINT_NAME = c.CONSTRAINT_NAME
            JOIN ALL_CONSTRAINTS rc
              ON rc.OWNER = c.R_OWNER AND rc.CONSTRAINT_NAME = c.R_CONSTRAINT_NAME
            JOIN ALL_CONS_COLUMNS rcc
              ON rcc.OWNER = rc.OWNER AND rcc.CONSTRAINT_NAME = rc.CONSTRAINT_NAME AND rcc.POSITION = cc.POSITION
            WHERE c.CONSTRAINT_TYPE = 'R'
              AND ((c.OWNER = :owner1 AND c.TABLE_NAME = :table1)
                OR (rc.OWNER = :owner2 AND rc.TABLE_NAME = :table2))
            ORDER BY c.CONSTRAINT_NAME, cc.POSITION
            """;

        await using var conn = await OpenAsync(profile, ct);
        await using var cmd = new OracleCommand(sql, conn) { BindByName = true };
        cmd.Parameters.Add("owner1", schema);
        cmd.Parameters.Add("table1", table);
        cmd.Parameters.Add("owner2", schema);
        cmd.Parameters.Add("table2", table);

        var result = new List<ForeignKeyRef>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new ForeignKeyRef(
                ReaderHelpers.Str(reader, 0),
                new TableInfo(ReaderHelpers.Str(reader, 1), ReaderHelpers.Str(reader, 2)),
                ReaderHelpers.Str(reader, 3),
                new TableInfo(ReaderHelpers.Str(reader, 4), ReaderHelpers.Str(reader, 5)),
                ReaderHelpers.Str(reader, 6)));
        }
        return result;
    }

    private static async Task<OracleConnection> OpenAsync(DbConnectionProfile profile, CancellationToken ct)
    {
        var builder = new OracleConnectionStringBuilder
        {
            DataSource = $"{profile.Host}:{profile.Port}/{profile.Database}",
            UserID = profile.User,
            Password = profile.Password,
        };

        var conn = new OracleConnection(builder.ConnectionString);
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

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string FormatType(string dataType, int? length, int? precision, int? scale) => dataType switch
    {
        "NUMBER" when precision is not null && scale is > 0 => $"NUMBER({precision},{scale})",
        "NUMBER" when precision is not null => $"NUMBER({precision})",
        "VARCHAR2" or "NVARCHAR2" or "CHAR" or "NCHAR" or "RAW" when length is not null => $"{dataType}({length})",
        _ => dataType,
    };
}
