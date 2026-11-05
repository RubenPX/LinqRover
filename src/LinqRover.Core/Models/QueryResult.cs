namespace LinqRover.Core.Models;

/// <summary>Resultado de una consulta libre. Si <see cref="Truncated"/> es true, hubo más filas que el límite.</summary>
public sealed record QueryResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<string?[]> Rows,
    bool Truncated,
    int? RowsAffected,
    TimeSpan Elapsed);
