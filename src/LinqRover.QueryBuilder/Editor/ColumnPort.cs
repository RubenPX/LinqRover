using Blazor.Diagrams.Core.Models;
using Blazor.Diagrams.Core.Models.Base;
using LinqRover.Core.Models;

namespace LinqRover.QueryBuilder.Editor;

/// <summary>
/// Puerto de una columna. Decide si un enlace puede llegar a ella: tipos compatibles, sin PK con PK,
/// y sin repetir una unión que ya existe.
/// </summary>
public sealed class ColumnPort : PortModel
{
    public ColumnPort(NodeModel parent, PortAlignment alignment, ColumnInfo info)
        : base(parent, alignment)
    {
        Info = info;
    }

    public ColumnInfo Info { get; }
    public string Column => Info.Name;

    public override bool CanAttachTo(ILinkable other)
    {
        if (!base.CanAttachTo(other))
            return false;

        if (other is not ColumnPort target)
            return false;

        if (!TypesCompatible(Info.DataType, target.Info.DataType))
            return false;

        return !AlreadyLinkedTo(target);
    }

    private bool AlreadyLinkedTo(ColumnPort target) =>
        Links.Any(l =>
            (l.Source?.Model == this && l.Target?.Model == target) ||
            (l.Source?.Model == target && l.Target?.Model == this));

    /// <summary>
    /// Tipo exacto: el tipo completo, con su longitud o precisión, tiene que coincidir.
    /// Ejemplo: NUMBER(2) no se une con NUMBER(10), y VARCHAR2(12) no se une con VARCHAR2(11).
    /// </summary>
    public static bool TypesCompatible(string a, string b) => Normalize(a) == Normalize(b);

    private static string Normalize(string dataType) =>
        new string(dataType.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
}
