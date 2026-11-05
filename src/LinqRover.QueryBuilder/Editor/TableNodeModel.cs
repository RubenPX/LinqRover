using Blazor.Diagrams.Core.Geometry;
using Blazor.Diagrams.Core.Models;
using LinqRover.Core.Models;

namespace LinqRover.QueryBuilder.Editor;

/// <summary>Nodo de una tabla: un puerto a cada lado de cada columna.</summary>
public sealed class TableNodeModel : NodeModel
{
    private readonly Dictionary<string, ColumnPort> _left = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ColumnPort> _right = new(StringComparer.OrdinalIgnoreCase);

    public TableNodeModel(Point position, string schema, string table, IReadOnlyList<ColumnInfo> columns)
        : base(position)
    {
        Schema = schema;
        Table = table;
        Columns = columns;
        Title = table;

        foreach (var column in columns)
        {
            var left = new ColumnPort(this, PortAlignment.Left, column);
            var right = new ColumnPort(this, PortAlignment.Right, column);
            AddPort(left);
            AddPort(right);
            _left[column.Name] = left;
            _right[column.Name] = right;
        }
    }

    public string Schema { get; }
    public string Table { get; }
    public IReadOnlyList<ColumnInfo> Columns { get; }

    public ColumnPort? LeftPort(string column) => _left.GetValueOrDefault(column);
    public ColumnPort? RightPort(string column) => _right.GetValueOrDefault(column);
}
