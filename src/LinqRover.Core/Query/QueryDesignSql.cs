using LinqRover.Core.Models;

namespace LinqRover.Core.Query;

/// <summary>Genera el SQL de un diseño visual para SQL Server u Oracle (11g o superior).</summary>
public static class QueryDesignSql
{
    public static string Build(DatabaseEngine engine, QueryDesign design)
    {
        QueryDesignPlan.Validate(engine, design);
        var aliases = QueryDesignPlan.AliasMap(design);
        var steps = QueryDesignPlan.PlanJoins(design);
        var outputs = QueryDesignPlan.Outputs(engine, design);
        var baseSource = design.Sources[0];

        string Column(ColumnRef reference) => $"{aliases[reference.SourceId]}.{Quote(engine, reference.Column)}";

        string Aggregate(AggregateItem a) => a.Function switch
        {
            AggregateFunction.Count => a.Column is null ? "COUNT(*)" : $"COUNT({Column(a.Column)})",
            AggregateFunction.Sum => $"SUM({Column(a.Column!)})",
            AggregateFunction.Avg => $"AVG({Column(a.Column!)})",
            AggregateFunction.Min => $"MIN({Column(a.Column!)})",
            _ => $"MAX({Column(a.Column!)})",
        };

        string Condition(WhereItem item)
        {
            var column = Column(item.Column);
            if (item.Operator == FilterOperator.IsNull) return $"{column} IS NULL";
            if (item.Operator == FilterOperator.IsNotNull) return $"{column} IS NOT NULL";
            if (item.Operator == FilterOperator.Like) return $"{column} LIKE {QueryLiterals.SqlText(item.Value!)}";

            var info = QueryDesignPlan.ResolveColumn(engine, design, item.Column);
            return $"{column} {QueryLiterals.SqlOperator(item.Operator)} {QueryLiterals.Sql(engine, item.Value!, info)}";
        }

        var select = string.Join(", ", outputs.Select(o =>
            $"{(o.Column is { } c ? Column(c) : Aggregate(o.Aggregate!))} AS {Quote(engine, o.Alias)}"));

        var from = $"{TableRef(engine, baseSource)} {aliases[baseSource.Id]}";
        foreach (var step in steps)
        {
            var target = design.Sources.First(s => s.Id == step.SourceId);
            var on = string.Join(" AND ", step.Conditions.Select(c => $"{Column(c.Left)} = {Column(c.Right)}"));
            from += $" INNER JOIN {TableRef(engine, target)} {aliases[target.Id]} ON {on}";
        }

        // Condiciones encadenadas por la izquierda: ((c1) OR c2) AND c3
        string? where = null;
        foreach (var item in design.Where)
        {
            var condition = Condition(item);
            where = where is null ? condition : $"({where}) {(item.Or ? "OR" : "AND")} {condition}";
        }

        var tail = (where is null ? "" : $" WHERE {where}")
            + (design.GroupBy.Count == 0 ? "" : " GROUP BY " + string.Join(", ", design.GroupBy.Select(Column)));

        var take = design.Take ?? design.Limit;
        var skip = design.Skip;

        if (engine == DatabaseEngine.SqlServer)
        {
            var top = skip is null && take is not null ? $"TOP ({take}) " : "";
            var sql = $"SELECT {top}{select} FROM {from}{tail}";
            if (skip is not null)
            {
                sql += $" ORDER BY (SELECT NULL) OFFSET {skip} ROWS";
                if (take is not null)
                    sql += $" FETCH NEXT {take} ROWS ONLY";
            }
            return sql;
        }

        // Oracle 11g no tiene OFFSET/FETCH: se usa ROWNUM.
        var inner = $"SELECT {select} FROM {from}{tail}";
        if (skip is null)
            return take is null ? inner : $"SELECT * FROM ({inner}) WHERE ROWNUM <= {take}";

        var numbered = take is null
            ? $"SELECT a__.*, ROWNUM AS \"RN__\" FROM ({inner}) a__"
            : $"SELECT a__.*, ROWNUM AS \"RN__\" FROM ({inner}) a__ WHERE ROWNUM <= {skip + take}";
        var columns = string.Join(", ", outputs.Select(o => Quote(engine, o.Alias)));
        return $"SELECT {columns} FROM ({numbered}) WHERE \"RN__\" > {skip}";
    }

    private static string TableRef(DatabaseEngine engine, QuerySource source) =>
        $"{Quote(engine, source.Schema)}.{Quote(engine, source.Table)}";

    private static string Quote(DatabaseEngine engine, string identifier) => engine == DatabaseEngine.SqlServer
        ? "[" + identifier.Replace("]", "]]") + "]"
        : "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
