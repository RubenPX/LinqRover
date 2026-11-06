using System.Text;
using LinqRover.Core.Models;
using LinqRover.Core.Scripting;

namespace LinqRover.Core.Query;

/// <summary>
/// Genera el script EF Core (C#) equivalente a un diseño visual, con la misma forma que los scripts de la
/// pestaña EF Script: <c>var query = db.Tabla...; return query;</c>.
/// </summary>
public static class QueryDesignLinq
{
    public static string Build(DatabaseEngine engine, QueryDesign design)
    {
        QueryDesignPlan.Validate(engine, design);
        var steps = QueryDesignPlan.PlanJoins(design);
        var outputs = QueryDesignPlan.Outputs(engine, design);
        var baseSource = design.Sources[0];
        var sources = design.Sources.ToDictionary(s => s.Id);

        // Posición en la tupla de los Join: 0 = tabla base (entidad r), N = N-ésimo paso (tN).
        var position = new Dictionary<string, int> { [baseSource.Id] = 0 };
        for (var i = 0; i < steps.Count; i++)
            position[steps[i].SourceId] = i + 1;

        var total = steps.Count + 1;
        var sb = new StringBuilder();
        sb.Append($"var query = db.{ClassGenerator.ContextMemberName(baseSource.Table)}");

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var joined = i + 1; // tablas colocadas antes de este Join
            var target = sources[step.SourceId];
            var leftKeys = new List<string>();
            var rightKeys = new List<string>();

            for (var c = 0; c < step.Conditions.Count; c++)
            {
                var condition = step.Conditions[c];
                var leftInfo = QueryDesignPlan.ResolveColumn(engine, design, condition.Left);
                var rightInfo = QueryDesignPlan.ResolveColumn(engine, design, condition.Right);

                var left = Member(condition.Left, joined, position);
                var right = "t." + ClassGenerator.Identifier(condition.Right.Column);

                // Las dos claves del Join deben tener el mismo tipo CLR (NUMBER(2) y NUMBER(10) no lo tienen).
                if (QueryLiterals.Underlying(engine, leftInfo) != QueryLiterals.Underlying(engine, rightInfo))
                {
                    left = $"(decimal?){left}";
                    right = $"(decimal?){right}";
                }

                leftKeys.Add($"K{c} = {left}");
                rightKeys.Add($"K{c} = {right}");
            }

            var members = new List<string>();
            if (joined == 1)
                members.Add("t0 = r");
            else
                for (var p = 0; p < joined; p++)
                    members.Add($"r.t{p}");
            members.Add($"t{joined} = t");

            sb.Append($"\n    .Join(db.{ClassGenerator.ContextMemberName(target.Table)},");
            sb.Append($"\n        r => new {{ {string.Join(", ", leftKeys)} }},");
            sb.Append($"\n        t => new {{ {string.Join(", ", rightKeys)} }},");
            sb.Append($"\n        (r, t) => new {{ {string.Join(", ", members)} }})");
        }

        string? predicate = null;
        foreach (var item in design.Where)
        {
            var condition = Predicate(engine, design, item, total, position);
            predicate = predicate is null ? condition : $"({predicate}) {(item.Or ? "||" : "&&")} {condition}";
        }
        if (predicate is not null)
            sb.Append($"\n    .Where(r => {predicate})");

        var hasGroup = design.GroupBy.Count > 0;
        var hasAggregates = design.Aggregates.Count > 0;

        if (hasGroup)
        {
            var keys = design.GroupBy.Select((g, n) => $"G{n} = {Member(g, total, position)}");
            sb.Append($"\n    .GroupBy(r => new {{ {string.Join(", ", keys)} }})");

            var groupList = design.GroupBy.ToList();
            var projection = outputs.Select(o =>
            {
                var value = o.Column is { } column
                    ? $"g.Key.G{groupList.IndexOf(column)}"
                    : Aggregate(o.Aggregate!, "g", total, position);
                return $"{ClassGenerator.Identifier(o.Alias)} = {value}";
            });
            sb.Append($"\n    .Select(g => new {{ {string.Join(", ", projection)} }})");
        }
        else if (!hasAggregates)
        {
            var projection = outputs.Select(o =>
                $"{ClassGenerator.Identifier(o.Alias)} = {Member(o.Column!, total, position)}");
            sb.Append($"\n    .Select(r => new {{ {string.Join(", ", projection)} }})");
        }

        if (hasAggregates && !hasGroup)
        {
            // Una sola fila: las agregaciones se calculan sobre toda la consulta.
            var projection = outputs.Select(o =>
                $"{ClassGenerator.Identifier(o.Alias)} = {Aggregate(o.Aggregate!, "query", total, position)}");
            sb.Append($";\n\nreturn new {{ {string.Join(", ", projection)} }};");
            return sb.ToString();
        }

        // DistinctBy: EF Core no traduce el operador DistinctBy, pero sí GroupBy + First(), que es la alternativa
        // que recomienda el equipo de EF. Se agrupa por las columnas de salida marcadas y se queda la primera fila.
        if (design.DistinctBy is { Count: > 0 } distinct)
        {
            var keys = distinct
                .Select(d => outputs.First(o => o.Column == d))
                .Select((o, n) => $"K{n} = x.{ClassGenerator.Identifier(o.Alias)}");
            sb.Append($"\n    .GroupBy(x => new {{ {string.Join(", ", keys)} }})");
            sb.Append("\n    .Select(g => g.First())");
        }

        // OrderBy por alias de salida: OrderBy/OrderByDescending para el primero, ThenBy/ThenByDescending para el resto.
        if (design.OrderBy is { Count: > 0 } order)
        {
            for (var i = 0; i < order.Count; i++)
            {
                var member = ClassGenerator.Identifier(
                    outputs.First(o => string.Equals(o.Alias, order[i].Alias, StringComparison.OrdinalIgnoreCase)).Alias);
                var method = (i == 0 ? "OrderBy" : "ThenBy") + (order[i].Descending ? "Descending" : "");
                sb.Append($"\n    .{method}(x => x.{member})");
            }
        }

        if (design.Skip is { } skip)
            sb.Append($"\n    .Skip({skip})");

        var take = design.Take ?? design.Limit;
        if (take is { } limit)
            sb.Append($"\n    .Take({limit})");

        sb.Append(";\n\nreturn query;");
        return sb.ToString();
    }

    /// <summary>Acceso a una columna. Antes del primer Join, r es la entidad; después, r es la tupla.</summary>
    private static string Member(ColumnRef reference, int joined, IReadOnlyDictionary<string, int> position) =>
        joined == 1
            ? "r." + ClassGenerator.Identifier(reference.Column)
            : $"r.t{position[reference.SourceId]}.{ClassGenerator.Identifier(reference.Column)}";

    private static string Predicate(
        DatabaseEngine engine,
        QueryDesign design,
        WhereItem item,
        int total,
        IReadOnlyDictionary<string, int> position)
    {
        var member = Member(item.Column, total, position);
        switch (item.Operator)
        {
            case FilterOperator.IsNull:
                return $"{member} == null";
            case FilterOperator.IsNotNull:
                return $"{member} != null";
            case FilterOperator.Like:
                return $"EF.Functions.Like({member}, {QueryLiterals.CSharpText(item.Value!)})";
            default:
                var info = QueryDesignPlan.ResolveColumn(engine, design, item.Column);
                return $"{member} {QueryLiterals.CSharpOperator(item.Operator)} {QueryLiterals.CSharp(engine, item.Value!, info)}";
        }
    }

    /// <summary>Agregación EF. <paramref name="source"/> es el grupo (g) o la consulta (query).</summary>
    private static string Aggregate(
        AggregateItem a,
        string source,
        int total,
        IReadOnlyDictionary<string, int> position)
    {
        string Acc() => Member(a.Column!, total, position);

        return a.Function switch
        {
            AggregateFunction.Count => a.Column is null
                ? $"{source}.Count()"
                : $"{source}.Count(r => {Acc()} != null)",
            AggregateFunction.Sum => $"{source}.Sum(r => {Acc()})",
            AggregateFunction.Avg => $"{source}.Average(r => {Acc()})",
            AggregateFunction.Min => $"{source}.Min(r => {Acc()})",
            _ => $"{source}.Max(r => {Acc()})",
        };
    }
}
