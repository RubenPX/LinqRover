namespace LinqRover.Core.Scripting;

/// <summary>Columna de una tabla dinámica: nombre y tipo SQL tal como lo devuelve el proveedor (p. ej. "varchar(25)").</summary>
public sealed record DynamicColumn(string Name, string SqlType);

/// <summary>Traduce tipos SQL de SQL Server a tipos CLR anulables para mapear las columnas en EF Core.</summary>
public static class SqlTypeMap
{
    /// <summary>Devuelve el tipo CLR o null si el tipo no se puede mapear (la columna se omite).</summary>
    public static Type? ToClrType(string sqlType)
    {
        var baseName = sqlType.Split('(')[0].Trim().ToLowerInvariant();
        return baseName switch
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
            _ => null, // geography, hierarchyid, sql_variant, CLR...: no se mapean
        };
    }
}
