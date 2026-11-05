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

        // Dos claves primarias no se relacionan entre sí.
        if (Info.IsPrimaryKey && target.Info.IsPrimaryKey)
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
    /// Compatibles si pertenecen a la misma familia (texto, número, fecha). La longitud y la precisión no importan:
    /// VARCHAR2(12) se une con VARCHAR2(11). Los tipos desconocidos solo se unen con el mismo nombre de tipo.
    /// </summary>
    public static bool TypesCompatible(string a, string b) => FamilyOf(a) == FamilyOf(b);

    private static string FamilyOf(string dataType)
    {
        var baseName = dataType.Split('(')[0].Trim().ToUpperInvariant();
        return baseName switch
        {
            "NUMBER" or "NUMERIC" or "DECIMAL" or "INT" or "INTEGER" or "BIGINT" or "SMALLINT" or "TINYINT"
                or "FLOAT" or "REAL" or "DOUBLE" or "BINARY_FLOAT" or "BINARY_DOUBLE" or "MONEY" or "SMALLMONEY" => "NUM",
            "DATE" or "TIMESTAMP" or "DATETIME" or "DATETIME2" or "SMALLDATETIME" => "DATE",
            "VARCHAR" or "VARCHAR2" or "NVARCHAR" or "NVARCHAR2" or "CHAR" or "NCHAR" or "CLOB" or "NCLOB" or "TEXT" => "TEXT",
            _ => baseName,
        };
    }
}
