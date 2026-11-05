using System.Globalization;
using LinqRover.Core.Models;

namespace LinqRover.Services;

public sealed record DiagramColumn(string Name, string DataType, bool IsPk, bool IsFk, bool IsRef);

/// <summary>Una tabla dibujada. X/Y son la esquina superior izquierda en coordenadas del SVG.</summary>
public sealed record DiagramNode(
    TableInfo Table,
    string Title,
    double X,
    double Y,
    double Width,
    double Height,
    IReadOnlyList<DiagramColumn> Columns,
    int HiddenColumns,
    bool IsCenter);

public sealed record DiagramEdge(string Path, double LabelX, double LabelY, string? Label, bool ArrowAtEnd);

public sealed record DiagramLayoutResult(
    IReadOnlyList<DiagramNode> Nodes,
    IReadOnlyList<DiagramEdge> Edges,
    double Width,
    double Height,
    int HiddenLeft,
    int HiddenRight);

/// <summary>
/// Disposición del diagrama: la tabla central en el medio, las tablas a las que apunta a la izquierda
/// (padres) y las que la referencian a la derecha (hijos). Las flechas unen la columna exacta de cada FK.
/// </summary>
public static class DiagramLayout
{
    public const double NodeWidth = 250;
    public const double HeaderHeight = 30;
    public const double RowHeight = 20;
    public const double NodeGap = 22;
    public const double ColumnGap = 160;
    public const double Margin = 24;
    public const int MaxColumns = 14;
    public const int MaxNeighborsPerSide = 8;

    public static string KeyOf(TableInfo t) => $"{t.Schema}.{t.Name}";

    /// <param name="center">Tabla seleccionada.</param>
    /// <param name="refs">Claves foráneas en las que participa (como hijo o como padre).</param>
    /// <param name="columns">Columnas por tabla, indexadas con <see cref="KeyOf"/>. Debe incluir la tabla central.</param>
    public static DiagramLayoutResult Build(
        TableInfo center,
        IReadOnlyList<ForeignKeyRef> refs,
        IReadOnlyDictionary<string, IReadOnlyList<ColumnInfo>> columns)
    {
        var centerKey = KeyOf(center);

        // Autorreferencias (una tabla que se apunta a sí misma) no se dibujan en esta versión.
        var leftAll = refs.Where(r => KeyOf(r.Child) == centerKey && KeyOf(r.Parent) != centerKey).ToList();
        var rightAll = refs.Where(r => KeyOf(r.Parent) == centerKey && KeyOf(r.Child) != centerKey).ToList();

        var leftTablesAll = leftAll.Select(r => r.Parent).DistinctBy(KeyOf).ToList();
        var rightTablesAll = rightAll.Select(r => r.Child).DistinctBy(KeyOf).ToList();
        var leftTables = leftTablesAll.Take(MaxNeighborsPerSide).ToList();
        var rightTables = rightTablesAll.Take(MaxNeighborsPerSide).ToList();
        var leftKeys = leftTables.Select(KeyOf).ToHashSet();
        var rightKeys = rightTables.Select(KeyOf).ToHashSet();

        var leftRefs = leftAll.Where(r => leftKeys.Contains(KeyOf(r.Parent))).ToList();
        var rightRefs = rightAll.Where(r => rightKeys.Contains(KeyOf(r.Child))).ToList();

        // Columnas que intervienen en cada lado de la relación, para marcarlas.
        var centerFk = leftRefs.Select(r => r.ChildColumn).ToHashSet();
        var centerRef = rightRefs.Select(r => r.ParentColumn).ToHashSet();

        var centerNode = MakeNode(center, center.Name, 0, 0,
            ColumnsOf(columns, centerKey), centerFk, centerRef, isCenter: true);

        var leftNodes = leftTables.Select(t => MakeNode(
            t, t.Name, 0, 0, ColumnsOf(columns, KeyOf(t)),
            new HashSet<string>(),
            leftRefs.Where(r => KeyOf(r.Parent) == KeyOf(t)).Select(r => r.ParentColumn).ToHashSet(),
            isCenter: false)).ToList();

        var rightNodes = rightTables.Select(t => MakeNode(
            t, t.Name, 0, 0, ColumnsOf(columns, KeyOf(t)),
            rightRefs.Where(r => KeyOf(r.Child) == KeyOf(t)).Select(r => r.ChildColumn).ToHashSet(),
            new HashSet<string>(),
            isCenter: false)).ToList();

        // Alturas de los apilados y alto total del lienzo.
        var leftSum = StackHeight(leftNodes);
        var rightSum = StackHeight(rightNodes);
        var contentHeight = Math.Max(Math.Max(leftSum, rightSum), centerNode.Height);
        var height = contentHeight + 2 * Margin;

        var leftX = Margin;
        var centerX = leftX + NodeWidth + ColumnGap;
        var rightX = centerX + NodeWidth + ColumnGap;
        var width = rightX + NodeWidth + Margin;

        centerNode = centerNode with { X = centerX, Y = Margin + (contentHeight - centerNode.Height) / 2 };
        var leftPlaced = Stack(leftNodes, leftX, height, leftSum);
        var rightPlaced = Stack(rightNodes, rightX, height, rightSum);

        var edges = new List<DiagramEdge>();
        var labeled = new HashSet<string>();

        foreach (var r in leftRefs)
        {
            var parent = leftPlaced.First(n => KeyOf(n.Table) == KeyOf(r.Parent));
            var x1 = centerNode.X;
            var y1 = RowY(centerNode, r.ChildColumn);
            var x2 = parent.X + NodeWidth;
            var y2 = RowY(parent, r.ParentColumn);
            edges.Add(BuildEdge(x1, y1, x2, y2, labeled.Add(r.ConstraintName) ? Shorten(r.ConstraintName, 22) : null, arrowAtEnd: true));
        }

        foreach (var r in rightRefs)
        {
            var child = rightPlaced.First(n => KeyOf(n.Table) == KeyOf(r.Child));
            var x1 = centerNode.X + NodeWidth;
            var y1 = RowY(centerNode, r.ParentColumn);
            var x2 = child.X;
            var y2 = RowY(child, r.ChildColumn);
            edges.Add(BuildEdge(x1, y1, x2, y2, labeled.Add(r.ConstraintName) ? Shorten(r.ConstraintName, 22) : null, arrowAtEnd: false));
        }

        var nodes = new List<DiagramNode> { centerNode };
        nodes.AddRange(leftPlaced);
        nodes.AddRange(rightPlaced);

        return new DiagramLayoutResult(
            nodes, edges, width, height,
            HiddenLeft: leftTablesAll.Count - leftTables.Count,
            HiddenRight: rightTablesAll.Count - rightTables.Count);
    }

    private static IReadOnlyList<ColumnInfo> ColumnsOf(IReadOnlyDictionary<string, IReadOnlyList<ColumnInfo>> columns, string key) =>
        columns.TryGetValue(key, out var list) ? list : [];

    /// <summary>Crea un nodo con sus columnas visibles: primero las de clave y las que intervienen en relaciones.</summary>
    private static DiagramNode MakeNode(
        TableInfo table, string title, double x, double y,
        IReadOnlyList<ColumnInfo> cols,
        HashSet<string> fkColumns,
        HashSet<string> refColumns,
        bool isCenter)
    {
        var all = cols
            .Select(c => new DiagramColumn(c.Name, c.DataType, c.IsPrimaryKey,
                fkColumns.Contains(c.Name), refColumns.Contains(c.Name)))
            .ToList();

        var important = all.Where(c => c.IsPk || c.IsFk || c.IsRef).ToList();
        var others = all.Where(c => !(c.IsPk || c.IsFk || c.IsRef)).ToList();
        var visible = important.Concat(others).Take(MaxColumns).ToList();
        var hidden = all.Count - visible.Count;

        var rows = visible.Count + (hidden > 0 ? 1 : 0);
        var height = HeaderHeight + Math.Max(1, rows) * RowHeight + 8;

        return new DiagramNode(table, Shorten(title, 26), x, y, NodeWidth, height, visible, hidden, isCenter);
    }

    private static double StackHeight(IReadOnlyList<DiagramNode> nodes) =>
        nodes.Count == 0 ? 0 : nodes.Sum(n => n.Height) + NodeGap * (nodes.Count - 1);

    /// <summary>Apila los nodos en una columna, centrada verticalmente en el lienzo.</summary>
    private static List<DiagramNode> Stack(IReadOnlyList<DiagramNode> nodes, double x, double canvasHeight, double stackHeight)
    {
        var y = (canvasHeight - stackHeight) / 2;
        var placed = new List<DiagramNode>(nodes.Count);
        foreach (var n in nodes)
        {
            placed.Add(n with { X = x, Y = y });
            y += n.Height + NodeGap;
        }
        return placed;
    }

    /// <summary>Coordenada Y del centro de la fila de una columna; si no es visible, la cabecera.</summary>
    public static double RowY(DiagramNode node, string column)
    {
        var index = -1;
        for (var i = 0; i < node.Columns.Count; i++)
        {
            if (node.Columns[i].Name == column)
            {
                index = i;
                break;
            }
        }
        return index < 0
            ? node.Y + HeaderHeight / 2
            : node.Y + HeaderHeight + index * RowHeight + RowHeight / 2;
    }

    private static DiagramEdge BuildEdge(double x1, double y1, double x2, double y2, string? label, bool arrowAtEnd)
    {
        var mx = (x1 + x2) / 2;
        var path = string.Create(CultureInfo.InvariantCulture,
            $"M{x1:0.##} {y1:0.##} C{mx:0.##} {y1:0.##}, {mx:0.##} {y2:0.##}, {x2:0.##} {y2:0.##}");
        return new DiagramEdge(path, mx, (y1 + y2) / 2, label, arrowAtEnd);
    }

    private static string Shorten(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}
