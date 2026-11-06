using LinqRover.Core.Models;

namespace LinqRover.Core.Query;

/// <summary>
/// Genera el SQL de un diseño visual para SQL Server u Oracle (11g o superior).
/// El resultado va formateado en varias líneas, con una columna por línea y cada cláusula en su sitio.
/// </summary>
public static class QueryDesignSql
{
    private const string Indent = "    ";
    private const string RowNumberAlias = "RN__";

    public static string Build(DatabaseEngine engine, QueryDesign design)
    {
        QueryDesignPlan.Validate(engine, design);
        var aliases = QueryDesignPlan.AliasMap(design);
        var steps = QueryDesignPlan.PlanJoins(design);
        var outputs = QueryDesignPlan.Outputs(engine, design);
        var baseSource = design.Sources[0];
        var outputByAlias = outputs.ToDictionary(o => o.Alias, StringComparer.OrdinalIgnoreCase);

        string Column(ColumnRef reference) => $"{aliases[reference.SourceId]}.{Quote(engine, reference.Column)}";

        string Aggregate(AggregateItem a) => a.Function switch
        {
            AggregateFunction.Count => a.Column is null ? "COUNT(*)" : $"COUNT({Column(a.Column)})",
            AggregateFunction.Sum => $"SUM({Column(a.Column!)})",
            AggregateFunction.Avg => $"AVG({Column(a.Column!)})",
            AggregateFunction.Min => $"MIN({Column(a.Column!)})",
            _ => $"MAX({Column(a.Column!)})",
        };

        string Expression(OutputItem o) => o.Column is { } c ? Column(c) : Aggregate(o.Aggregate!);

        string Condition(WhereItem item)
        {
            var column = Column(item.Column);
            if (item.Operator == FilterOperator.IsNull) return $"{column} IS NULL";
            if (item.Operator == FilterOperator.IsNotNull) return $"{column} IS NOT NULL";
            if (item.Operator == FilterOperator.Like) return $"{column} LIKE {QueryLiterals.SqlText(item.Value!)}";

            var info = QueryDesignPlan.ResolveColumn(engine, design, item.Column);
            return $"{column} {QueryLiterals.SqlOperator(item.Operator)} {QueryLiterals.Sql(engine, item.Value!, info)}";
        }

        // SELECT: una columna por línea.
        var selectItems = outputs
            .Select(o => $"{Expression(o)} AS {Quote(engine, o.Alias)}")
            .ToList();

        // FROM y JOIN: cada join ocupa tres líneas (tabla, ON, AND adicionales).
        var from = new List<string> { $"FROM {TableRef(engine, baseSource)} {aliases[baseSource.Id]}" };
        foreach (var step in steps)
        {
            var target = design.Sources.First(s => s.Id == step.SourceId);
            var on = step.Conditions
                .Select(c => $"{Column(c.Left)} = {Column(c.Right)}")
                .ToList();

            from.Add($"{Indent}INNER JOIN {TableRef(engine, target)} {aliases[target.Id]}");
            from.Add($"{Indent}{Indent}ON {on[0]}");
            from.AddRange(on.Skip(1).Select(condition => $"{Indent}{Indent}AND {condition}"));
        }

        var tail = new List<string>();

        if (design.Where.Count > 0)
        {
            var where = WhereLines(design.Where, Condition);
            tail.Add($"WHERE {where[0]}");
            tail.AddRange(where.Skip(1).Select(line => "  " + line));
        }

        if (design.GroupBy.Count > 0)
            tail.Add("GROUP BY " + string.Join(", ", design.GroupBy.Select(Column)));

        // OrderBy: por alias de salida. Se usa en el ORDER BY final y, con DistinctBy, en el ROW_NUMBER.
        var orderBy = design.OrderBy ?? Array.Empty<OrderItem>();
        var orderAliases = orderBy
            .Select(o => $"{Quote(engine, outputByAlias[o.Alias].Alias)}{(o.Descending ? " DESC" : "")}")
            .ToList();
        var orderLines = orderAliases.Count == 0 ? new List<string>() : new List<string> { "ORDER BY " + string.Join(", ", orderAliases) };

        // DistinctBy: ROW_NUMBER() por cada combinación de claves; se queda con la fila 1 según el orden pedido.
        var distinct = design.DistinctBy ?? Array.Empty<ColumnRef>();
        var items = selectItems;
        var outerFrom = from;
        var outerTail = tail;

        if (distinct.Count > 0)
        {
            var rowNumber = Quote(engine, RowNumberAlias);
            var partition = string.Join(", ", distinct.Select(Column));
            var rowOrder = orderBy.Count == 0
                ? (engine == DatabaseEngine.SqlServer ? "(SELECT NULL)" : "NULL")
                : string.Join(", ", orderBy.Select(o =>
                    Expression(outputByAlias[o.Alias]) + (o.Descending ? " DESC" : "")));
            var inner = Query("SELECT",
                [.. selectItems, $"ROW_NUMBER() OVER (PARTITION BY {partition} ORDER BY {rowOrder}) AS {rowNumber}"],
                from,
                tail);

            items = outputs.Select(o => Quote(engine, o.Alias)).ToList();
            outerFrom = ["FROM (", IndentBlock(string.Join("\n", inner)), ") d__"];
            outerTail = [$"WHERE {rowNumber} = 1"];
        }

        var take = design.Take ?? design.Limit;
        var skip = design.Skip;

        if (engine == DatabaseEngine.SqlServer)
        {
            var top = skip is null && take is not null ? $" TOP ({take})" : "";
            var lines = Query($"SELECT{top}", items, outerFrom, outerTail);

            // OFFSET/FETCH exige ORDER BY: el pedido, o uno neutro si no hay OrderBy.
            if (orderLines.Count > 0)
                lines.AddRange(orderLines);
            else if (skip is not null)
                lines.Add("ORDER BY (SELECT NULL)");

            if (skip is not null)
            {
                lines.Add($"OFFSET {skip} ROWS");
                if (take is not null)
                    lines.Add($"FETCH NEXT {take} ROWS ONLY");
            }
            return string.Join("\n", lines);
        }

        // Oracle 11g no tiene OFFSET/FETCH: se usa ROWNUM sobre la consulta ordenada.
        var query = string.Join("\n", Query("SELECT", items, [.. outerFrom], [.. outerTail, .. orderLines]));
        if (skip is null)
        {
            if (take is null) return query;
            return $"SELECT *\nFROM (\n{IndentBlock(query)}\n)\nWHERE ROWNUM <= {take}";
        }

        var numbered = new List<string>
        {
            "SELECT a__.*, ROWNUM AS \"RN__\"",
            "FROM (",
            IndentBlock(query),
            ") a__",
        };
        if (take is not null)
            numbered.Add($"WHERE ROWNUM <= {skip + take}");

        var columns = string.Join(", ", outputs.Select(o => Quote(engine, o.Alias)));
        return $"SELECT {columns}\nFROM (\n{IndentBlock(string.Join("\n", numbered))}\n)\nWHERE \"RN__\" > {skip}";
    }

    /// <summary>
    /// Condiciones encadenadas por la izquierda, con el mismo significado que antes: ((c1) OR c2) AND c3.
    /// Devuelve las líneas de la expresión; la primera va pegada a la cláusula WHERE.
    /// </summary>
    private static List<string> WhereLines(IReadOnlyList<WhereItem> items, Func<WhereItem, string> condition)
    {
        List<string> expression = [];
        for (var i = 0; i < items.Count; i++)
        {
            var text = condition(items[i]);
            if (i == 0)
            {
                expression = [text];
                continue;
            }

            var op = items[i].Or ? "OR" : "AND";
            expression = i == 1
                ? [expression[0], $"{op} {text}"]
                : ["(", .. expression.Select(line => Indent + line), ")", $"{op} {text}"];
        }
        return expression;
    }

    /// <summary>Une SELECT, FROM y las cláusulas finales en líneas, con las columnas sangradas.</summary>
    private static List<string> Query(string head, IReadOnlyList<string> selectItems, IReadOnlyList<string> from, IReadOnlyList<string> tail)
    {
        var lines = new List<string> { head };
        for (var i = 0; i < selectItems.Count; i++)
            lines.Add(Indent + selectItems[i] + (i < selectItems.Count - 1 ? "," : ""));
        lines.AddRange(from);
        lines.AddRange(tail);
        return lines;
    }

    /// <summary>Sangra cada línea del texto con cuatro espacios.</summary>
    private static string IndentBlock(string text) =>
        string.Join("\n", text.Split('\n').Select(line => Indent + line));

    private static string TableRef(DatabaseEngine engine, QuerySource source) =>
        $"{Quote(engine, source.Schema)}.{Quote(engine, source.Table)}";

    private static string Quote(DatabaseEngine engine, string identifier) => engine == DatabaseEngine.SqlServer
        ? "[" + identifier.Replace("]", "]]") + "]"
        : "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
