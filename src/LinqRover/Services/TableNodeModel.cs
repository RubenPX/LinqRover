using Point = Blazor.Diagrams.Core.Geometry.Point;
using Blazor.Diagrams.Core.Models;
using LinqRover.Core.Models;

namespace LinqRover.Services;

/// <summary>
/// Nodo de tabla para Z.Blazor.Diagrams. Tiene un puerto a cada lado de cada columna visible:
/// el izquierdo recibe las FK de la tabla y el derecho las referencias a sus columnas.
/// </summary>
public sealed class TableNodeModel : NodeModel
{
    private readonly Dictionary<string, PortModel> _left = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PortModel> _right = new(StringComparer.Ordinal);

    public TableNodeModel(Point position, TableInfo table, IReadOnlyList<DiagramColumn> columns, int hiddenColumns, bool isCenter)
        : base(position)
    {
        Table = table;
        Columns = columns;
        HiddenColumns = hiddenColumns;
        IsCenter = isCenter;
        Title = table.Name;

        foreach (var column in columns)
        {
            _left[column.Name] = AddPort(new PortModel(this, PortAlignment.Left));
            _right[column.Name] = AddPort(new PortModel(this, PortAlignment.Right));
        }
    }

    public TableInfo Table { get; }
    public IReadOnlyList<DiagramColumn> Columns { get; }
    public int HiddenColumns { get; }
    public bool IsCenter { get; }

    public PortModel? LeftPort(string column) => _left.GetValueOrDefault(column);
    public PortModel? RightPort(string column) => _right.GetValueOrDefault(column);
}
