using LinqRover.Core.Models;

namespace LinqRover.Core.Scripting;

/// <summary>Columna de una tabla dinámica: nombre y tipo tal como lo devuelve el proveedor (p. ej. "varchar(25)", "NUMBER(6)").</summary>
public sealed record DynamicColumn(string Name, string SqlType);

/// <summary>Traduce los tipos de cada motor a tipos CLR anulables para mapear las columnas en EF Core.</summary>
public static class SqlTypeMap
{
    /// <summary>Devuelve el tipo CLR o null si el tipo no se puede mapear (la columna se omite).</summary>
    public static Type? ToClrType(string sqlType, DatabaseEngine engine) => engine switch
    {
        DatabaseEngine.SqlServer => SqlServerType(sqlType),
        DatabaseEngine.Oracle => OracleType(sqlType),
        _ => null,
    };

    private static Type? SqlServerType(string sqlType) => BaseName(sqlType) switch
    {
        "int" => typeof(int?),
        "bigint" => typeof(long?),
        "smallint" => typeof(short?),
        "tinyint" => typeof(byte?),
        "bit" => typeof(bool?),
        "decimal" or "numeric" or "money" or "smallmoney" => typeof(decimal?),
        "float" => typeof(double?),
        "real" => typeof(float?),
        "datetime" or "datetime2" or "smalldatetime" or "date" => typeof(DateTime?),
        "datetimeoffset" => typeof(DateTimeOffset?),
        "time" => typeof(TimeSpan?),
        "uniqueidentifier" => typeof(Guid?),
        "binary" or "varbinary" or "image" or "timestamp" => typeof(byte[]),
        "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" or "xml" => typeof(string),
        _ => null, // geography, hierarchyid, sql_variant...: no se mapean
    };

    private static Type? OracleType(string sqlType)
    {
        var upper = sqlType.Trim().ToUpperInvariant();

        if (upper.Contains("WITH TIME ZONE"))
            return typeof(DateTimeOffset?);

        return BaseName(sqlType) switch
        {
            "number" => OracleNumberType(upper),
            "date" or "timestamp" => typeof(DateTime?),
            "float" or "binary_double" => typeof(double?),
            "binary_float" => typeof(float?),
            "varchar2" or "nvarchar2" or "char" or "nchar" or "clob" or "nclob" or "long" => typeof(string),
            "raw" or "blob" => typeof(byte[]),
            _ => null, // ROWID, BFILE, tipos de objeto...: no se mapean
        };
    }

    /// <summary>NUMBER sin escala y con pocos dígitos es entero; el resto, decimal.</summary>
    private static Type OracleNumberType(string upper)
    {
        var open = upper.IndexOf('(');
        if (open < 0)
            return typeof(decimal?);

        var close = upper.IndexOf(')', open);
        var parts = upper[(open + 1)..close].Split(',');
        var precision = int.Parse(parts[0].Trim());
        var scale = parts.Length > 1 ? int.Parse(parts[1].Trim()) : 0;

        if (scale > 0) return typeof(decimal?);
        if (precision <= 9) return typeof(int?);
        if (precision <= 18) return typeof(long?);
        return typeof(decimal?);
    }

    /// <summary>Nombre base del tipo en minúsculas, sin parámetros: "varchar(25)" → "varchar".</summary>
    private static string BaseName(string sqlType) =>
        sqlType.Split('(')[0].Trim().ToLowerInvariant();
}
