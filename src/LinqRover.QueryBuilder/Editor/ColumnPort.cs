using Blazor.Diagrams.Core.Models;

namespace LinqRover.QueryBuilder.Editor;

/// <summary>Puerto de una columna. Guarda el nombre de la columna para generar la condición de unión.</summary>
public sealed class ColumnPort : PortModel
{
    public ColumnPort(NodeModel parent, PortAlignment alignment, string column)
        : base(parent, alignment)
    {
        Column = column;
    }

    public string Column { get; }
}
