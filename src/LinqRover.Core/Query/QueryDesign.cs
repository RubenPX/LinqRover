using System.Globalization;
using System.Text.RegularExpressions;
using LinqRover.Core.Models;
using LinqRover.Core.Scripting;

namespace LinqRover.Core.Query;

public enum AggregateFunction { Count, Sum, Avg, Min, Max }

public enum FilterOperator { Equals, NotEquals, Greater, GreaterOrEqual, Less, LessOrEqual, Like, IsNull, IsNotNull }

/// <summary>Diseño del editor visual no válido. El mensaje se muestra tal cual al usuario.</summary>
public sealed class QueryDesignException(string message) : Exception(message);

/// <summary>Tabla del diagrama. <see cref="Id"/> identifica el nodo y es estable mientras exista.</summary>
public sealed record QuerySource(string Id, string Schema, string Table, IReadOnlyList<ColumnInfo> Columns);

public sealed record ColumnRef(string SourceId, string Column);

/// <summary>Enlace entre dos columnas: <c>INNER JOIN ... ON Left = Right</c>.</summary>
public sealed record QueryJoin(ColumnRef Left, ColumnRef Right);

/// <summary>Condición WHERE. <see cref="Or"/> es el conector con la condición anterior (false = AND).</summary>
public sealed record WhereItem(ColumnRef Column, FilterOperator Operator, string? Value, bool Or = false);

/// <summary>Agregación. Column null solo vale para COUNT(*).</summary>
public sealed record AggregateItem(AggregateFunction Function, ColumnRef? Column, string Alias);

public sealed record QueryDesign(
    IReadOnlyList<QuerySource> Sources,
    IReadOnlyList<QueryJoin> Joins,
    IReadOnlyList<ColumnRef> Select,
    IReadOnlyList<WhereItem> Where,
    IReadOnlyList<ColumnRef> GroupBy,
    IReadOnlyList<AggregateItem> Aggregates,
    int? Skip = null,
    int? Take = null,
    int? Limit = null);

/// <summary>Tabla que se añade a la consulta y sus condiciones. En cada condición, Left ya está colocada y Right es la tabla del paso.</summary>
public sealed record JoinStep(string SourceId, IReadOnlyList<QueryJoin> Conditions);

/// <summary>Columna de salida (con su alias) o agregación.</summary>
public sealed record OutputItem(string Alias, ColumnRef? Column, AggregateItem? Aggregate);

/// <summary>Clase de valor de una columna, para los literales de los filtros.</summary>
public enum ValueKind { Unsupported, Number, Date, Boolean, Text }

/// <summary>Validación, orden de los joins y columnas de salida. Lo comparten los generadores SQL y EF.</summary>
public static class QueryDesignPlan
{
    private static readonly Regex AliasPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>Comprueba el diseño completo. Lanza <see cref="QueryDesignException"/> con el primer error encontrado.</summary>
    public static void Validate(DatabaseEngine engine, QueryDesign design)
    {
        if (design.Sources.Count == 0)
            throw new QueryDesignException("Añade al menos una tabla al diagrama.");

        foreach (var join in design.Joins)
        {
            var left = ResolveColumn(engine, design, join.Left);
            var right = ResolveColumn(engine, design, join.Right);
            if (join.Left.SourceId == join.Right.SourceId)
                throw new QueryDesignException("Una unión debe conectar dos tablas distintas.");
            if (QueryLiterals.KindOf(engine, left) != QueryLiterals.KindOf(engine, right))
                throw new QueryDesignException(
                    $"No se puede unir {Describe(design, join.Left)} con {Describe(design, join.Right)}: tipos incompatibles.");
        }

        foreach (var reference in design.Select.Concat(design.GroupBy))
            ResolveColumn(engine, design, reference);

        foreach (var item in design.Where)
        {
            var info = ResolveColumn(engine, design, item.Column);
            if (item.Operator is FilterOperator.IsNull or FilterOperator.IsNotNull)
                continue;

            var name = Describe(design, item.Column);
            var value = item.Value;
            if (string.IsNullOrWhiteSpace(value))
                throw new QueryDesignException($"Falta el valor del filtro sobre {name}.");

            var kind = QueryLiterals.KindOf(engine, info);
            if (item.Operator == FilterOperator.Like)
            {
                if (kind != ValueKind.Text)
                    throw new QueryDesignException($"LIKE solo se puede usar con columnas de texto ({name}).");
            }
            else
            {
                QueryLiterals.Check(kind, value, name);
            }
        }

        foreach (var aggregate in design.Aggregates)
        {
            if (!AliasPattern.IsMatch(aggregate.Alias))
                throw new QueryDesignException(
                    $"El alias «{aggregate.Alias}» no es válido: usa letras, números y guion bajo, sin empezar por número.");

            if (aggregate.Column is null)
            {
                if (aggregate.Function != AggregateFunction.Count)
                    throw new QueryDesignException($"{aggregate.Function} necesita una columna.");
                continue;
            }

            var info = ResolveColumn(engine, design, aggregate.Column);
            if ((aggregate.Function is AggregateFunction.Sum or AggregateFunction.Avg)
                && QueryLiterals.KindOf(engine, info) != ValueKind.Number)
                throw new QueryDesignException(
                    $"{aggregate.Function} solo se puede usar con columnas numéricas ({Describe(design, aggregate.Column)}).");
        }

        if (design.Aggregates.GroupBy(a => a.Alias, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new QueryDesignException("Hay alias de agregación repetidos.");

        foreach (var n in new int?[] { design.Skip, design.Take, design.Limit })
            if (n < 0)
                throw new QueryDesignException("Skip, Take y Limit no pueden ser negativos.");

        var hasGroup = design.GroupBy.Count > 0;
        var hasAggregates = design.Aggregates.Count > 0;

        if (hasGroup)
        {
            foreach (var column in design.Select)
                if (!design.GroupBy.Contains(column))
                    throw new QueryDesignException(
                        $"{Describe(design, column)} debe estar en GroupBy o ser una agregación.");
        }

        if (hasAggregates && !hasGroup)
        {
            if (design.Select.Count > 0)
                throw new QueryDesignException("Con agregaciones sin GroupBy no se pueden seleccionar columnas sueltas.");
            if (design.Skip is not null || design.Take is not null || design.Limit is not null)
                throw new QueryDesignException("Skip, Take y Limit no tienen efecto con agregaciones sin GroupBy.");
        }

        // Comprueban la unión y los alias de salida.
        PlanJoins(design);
        Outputs(engine, design);
    }

    /// <summary>
    /// Pasos de unión en el orden en que se pueden añadir: la primera tabla es la base y cada paso enlaza
    /// con alguna tabla ya colocada. Una unión se asigna al paso de la tabla que se coloca más tarde.
    /// </summary>
    public static IReadOnlyList<JoinStep> PlanJoins(QueryDesign design)
    {
        var baseSource = design.Sources[0];
        var order = new List<string> { baseSource.Id };
        var pending = design.Sources.Skip(1).ToList();

        var progress = true;
        while (pending.Count > 0 && progress)
        {
            progress = false;
            foreach (var source in pending.ToList())
            {
                var linked = design.Joins.Any(j =>
                    (j.Left.SourceId == source.Id && order.Contains(j.Right.SourceId)) ||
                    (j.Right.SourceId == source.Id && order.Contains(j.Left.SourceId)));
                if (!linked)
                    continue;

                order.Add(source.Id);
                pending.Remove(source);
                progress = true;
            }
        }

        if (pending.Count > 0)
            throw new QueryDesignException($"La tabla {pending[0].Table} no está unida a la tabla base ({baseSource.Table}).");

        var index = order.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var conditions = order.ToDictionary(id => id, _ => new List<QueryJoin>());
        foreach (var join in design.Joins)
        {
            var (earlier, later) = index[join.Left.SourceId] < index[join.Right.SourceId]
                ? (join.Left, join.Right)
                : (join.Right, join.Left);
            conditions[later.SourceId].Add(new QueryJoin(earlier, later));
        }

        return order.Skip(1).Select(id => new JoinStep(id, conditions[id])).ToList();
    }

    /// <summary>Columnas de salida. Sin Select ni agregaciones, todas las columnas (o las de GroupBy si hay GroupBy).</summary>
    public static IReadOnlyList<OutputItem> Outputs(DatabaseEngine engine, QueryDesign design)
    {
        var aliases = AliasMap(design);
        var hasAggregates = design.Aggregates.Count > 0;

        IReadOnlyList<ColumnRef> columns;
        if (design.Select.Count > 0)
            columns = design.Select.Distinct().ToList();
        else if (hasAggregates)
            columns = [];
        else if (design.GroupBy.Count > 0)
            columns = design.GroupBy;
        else
            columns = AllColumns(engine, design);

        var items = columns.Select(c => new OutputItem(c.Column, c, null)).ToList();
        items.AddRange(design.Aggregates.Select(a => new OutputItem(a.Alias, null, a)));

        // Columnas con el mismo nombre en varias tablas: se prefijan con el alias de la tabla (t1_ID).
        var repeated = items
            .Where(i => i.Column is not null)
            .GroupBy(i => i.Alias, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        items = items
            .Select(i => i.Column is not null && repeated.Contains(i.Alias)
                ? i with { Alias = $"{aliases[i.Column.SourceId]}_{i.Column.Column}" }
                : i)
            .ToList();

        if (items.Count == 0)
            throw new QueryDesignException("No hay columnas que mostrar. Selecciona alguna columna o una agregación.");

        var duplicated = items.GroupBy(i => i.Alias, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null)
            throw new QueryDesignException(
                $"El alias «{duplicated.Key}» aparece más de una vez en el resultado. Cambia el alias de la agregación.");

        return items;
    }

    /// <summary>Alias de tabla en SQL según su posición en <see cref="QueryDesign.Sources"/>: t0, t1, ...</summary>
    public static IReadOnlyDictionary<string, string> AliasMap(QueryDesign design) =>
        design.Sources.Select((s, i) => (s.Id, Alias: "t" + i)).ToDictionary(x => x.Id, x => x.Alias);

    /// <summary>Columna del diseño, o excepción si no existe o su tipo no se puede usar.</summary>
    public static ColumnInfo ResolveColumn(DatabaseEngine engine, QueryDesign design, ColumnRef reference)
    {
        var source = design.Sources.FirstOrDefault(s => s.Id == reference.SourceId)
            ?? throw new QueryDesignException(
                $"La columna «{reference.Column}» pertenece a una tabla que ya no está en el diagrama.");

        var info = source.Columns.FirstOrDefault(c => c.Name == reference.Column)
            ?? throw new QueryDesignException($"La columna «{reference.Column}» no existe en la tabla {source.Table}.");

        if (SqlTypeMap.ToClrType(info.DataType, engine) is null)
            throw new QueryDesignException(
                $"La columna {source.Table}.{info.Name} (tipo {info.DataType}) no se puede usar en el editor visual.");

        return info;
    }

    /// <summary>Nombre legible «TABLA.COLUMNA» para los mensajes de error.</summary>
    public static string Describe(QueryDesign design, ColumnRef reference) =>
        design.Sources.FirstOrDefault(s => s.Id == reference.SourceId) is { } source
            ? $"{source.Table}.{reference.Column}"
            : reference.Column;

    private static IReadOnlyList<ColumnRef> AllColumns(DatabaseEngine engine, QueryDesign design) =>
        design.Sources
            .SelectMany(s => s.Columns
                .Where(c => SqlTypeMap.ToClrType(c.DataType, engine) is not null)
                .Select(c => new ColumnRef(s.Id, c.Name)))
            .ToList();
}

/// <summary>Literales SQL y C# para los valores de los filtros. Los valores se validan antes de escribirse.</summary>
public static class QueryLiterals
{
    private static readonly string[] DateFormats =
        ["yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"];

    public static Type? ClrOf(DatabaseEngine engine, ColumnInfo column) => SqlTypeMap.ToClrType(column.DataType, engine);

    /// <summary>Tipo CLR sin anulabilidad (int? → int).</summary>
    public static Type Underlying(DatabaseEngine engine, ColumnInfo column)
    {
        var clr = ClrOf(engine, column) ?? throw new QueryDesignException($"Tipo no soportado: {column.DataType}.");
        return Nullable.GetUnderlyingType(clr) ?? clr;
    }

    public static ValueKind KindOf(DatabaseEngine engine, ColumnInfo column)
    {
        if (ClrOf(engine, column) is null)
            return ValueKind.Unsupported;

        var type = Underlying(engine, column);
        if (type == typeof(bool)) return ValueKind.Boolean;
        if (type == typeof(DateTime)) return ValueKind.Date;
        if (type == typeof(string)) return ValueKind.Text;
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
            || type == typeof(decimal) || type == typeof(double) || type == typeof(float))
            return ValueKind.Number;
        return ValueKind.Unsupported;
    }

    /// <summary>Comprueba que el texto del filtro se puede convertir al tipo de la columna.</summary>
    public static void Check(ValueKind kind, string value, string name)
    {
        var valid = kind switch
        {
            ValueKind.Number => ParseNumber(value) is not null,
            ValueKind.Date => ParseDate(value) is not null,
            ValueKind.Boolean => ParseBool(value) is not null,
            ValueKind.Text => true,
            _ => false,
        };
        if (valid)
            return;

        throw new QueryDesignException(kind switch
        {
            ValueKind.Number => $"«{value}» no es un número válido para {name} (usa punto o coma decimal).",
            ValueKind.Date => $"«{value}» no es una fecha válida para {name} (usa AAAA-MM-DD o AAAA-MM-DD HH:mm).",
            ValueKind.Boolean => $"«{value}» no es un valor válido para {name} (usa 1, 0, true o false).",
            _ => $"No se puede filtrar por {name}: tipo no soportado.",
        });
    }

    public static string SqlOperator(FilterOperator op) => op switch
    {
        FilterOperator.Equals => "=",
        FilterOperator.NotEquals => "<>",
        FilterOperator.Greater => ">",
        FilterOperator.GreaterOrEqual => ">=",
        FilterOperator.Less => "<",
        FilterOperator.LessOrEqual => "<=",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Operador sin símbolo de comparación."),
    };

    public static string CSharpOperator(FilterOperator op) => op switch
    {
        FilterOperator.Equals => "==",
        FilterOperator.NotEquals => "!=",
        FilterOperator.Greater => ">",
        FilterOperator.GreaterOrEqual => ">=",
        FilterOperator.Less => "<",
        FilterOperator.LessOrEqual => "<=",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Operador sin símbolo de comparación."),
    };

    /// <summary>Literal SQL del valor para la columna dada.</summary>
    public static string Sql(DatabaseEngine engine, string value, ColumnInfo column) => KindOf(engine, column) switch
    {
        ValueKind.Number => ParseNumber(value)!.Value.ToString(CultureInfo.InvariantCulture),
        ValueKind.Date => SqlDate(engine, ParseDate(value)!.Value),
        ValueKind.Boolean => ParseBool(value)!.Value ? "1" : "0",
        ValueKind.Text => SqlText(value),
        _ => throw new QueryDesignException($"No se puede filtrar por {column.Name}: tipo no soportado."),
    };

    /// <summary>Literal C# del valor para la columna dada (para expresiones EF).</summary>
    public static string CSharp(DatabaseEngine engine, string value, ColumnInfo column)
    {
        switch (KindOf(engine, column))
        {
            case ValueKind.Number:
                var number = ParseNumber(value)!.Value.ToString(CultureInfo.InvariantCulture);
                var type = Underlying(engine, column);
                if (type == typeof(double)) return number + "d";
                if (type == typeof(float)) return number + "f";
                return number + "m";

            case ValueKind.Date:
                var date = ParseDate(value)!.Value;
                return $"new DateTime({date.Year}, {date.Month}, {date.Day}, {date.Hour}, {date.Minute}, {date.Second})";

            case ValueKind.Boolean:
                return ParseBool(value)!.Value ? "true" : "false";

            case ValueKind.Text:
                return CSharpText(value);

            default:
                throw new QueryDesignException($"No se puede filtrar por {column.Name}: tipo no soportado.");
        }
    }

    public static string SqlText(string value) => "'" + value.Replace("'", "''") + "'";

    public static string CSharpText(string value) =>
        "\"" + value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t") + "\"";

    public static decimal? ParseNumber(string value) =>
        decimal.TryParse(value.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;

    public static DateTime? ParseDate(string value) =>
        DateTime.TryParseExact(value.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;

    public static bool? ParseBool(string value) => value.Trim().ToLowerInvariant() switch
    {
        "1" or "true" => true,
        "0" or "false" => false,
        _ => null,
    };

    private static string SqlDate(DatabaseEngine engine, DateTime date) =>
        engine == DatabaseEngine.SqlServer
            ? "'" + date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "'"
            : "TO_DATE('" + date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "', 'YYYY-MM-DD HH24:MI:SS')";
}
