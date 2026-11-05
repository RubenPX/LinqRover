namespace LinqRover.Core.Models;

public sealed record TableInfo(string Schema, string Name);

public sealed record ColumnInfo(string Name, string DataType, bool IsPrimaryKey, bool IsNullable);

/// <summary>Una página de resultados. Los valores se convierten a texto para mostrarlos; null = NULL.</summary>
public sealed record PageResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<string?[]> Rows,
    long TotalRows,
    int PageIndex,
    int PageSize);
