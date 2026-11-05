using System.Data.Common;
using System.Globalization;
using LinqRover.Core.Models;

namespace LinqRover.Core.Providers;

internal static class ReaderHelpers
{
    /// <summary>Lee como máximo <paramref name="maxRows"/> filas y las convierte a texto.</summary>
    public static async Task<(List<string> Columns, List<string?[]> Rows)> ReadPageAsync(
        DbDataReader reader, int maxRows, CancellationToken ct)
    {
        var columns = new List<string>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
            columns.Add(reader.GetName(i));

        var rows = new List<string?[]>();
        while (rows.Count < maxRows && await reader.ReadAsync(ct))
        {
            var row = new string?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
                row[i] = FormatValue(reader.GetValue(i), reader.IsDBNull(i));
            rows.Add(row);
        }

        return (columns, rows);
    }

    /// <summary>Convierte el resultado de una consulta libre. Si no hay columnas, es una sentencia sin resultados (INSERT, etc.).</summary>
    public static async Task<QueryResult> ReadResultAsync(
        DbDataReader reader, int maxRows, int recordsAffected, System.Diagnostics.Stopwatch clock, CancellationToken ct)
    {
        if (reader.FieldCount == 0)
            return new QueryResult([], [], false, recordsAffected >= 0 ? recordsAffected : null, clock.Elapsed);

        // Se piden maxRows + 1 filas para saber si hay más de las permitidas.
        var (columns, rows) = await ReadPageAsync(reader, maxRows + 1, ct);
        var truncated = rows.Count > maxRows;
        if (truncated)
            rows.RemoveAt(rows.Count - 1);

        return new QueryResult(columns, rows, truncated, null, clock.Elapsed);
    }

    /// <summary>Lee la primera columna de cada fila como texto.</summary>
    public static async Task<List<string>> ReadStringsAsync(DbDataReader reader, CancellationToken ct)
    {
        var values = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            if (!reader.IsDBNull(0))
                values.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? "");
        }
        return values;
    }

    public static string Str(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? "" : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? "";

    public static int? Int(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static string? FormatValue(object value, bool isNull) => isNull
        ? null
        : value switch
        {
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            byte[] bytes => $"<binary {bytes.Length} bytes>",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
}
