using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using LinqRover.Core.Models;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace LinqRover.Core.Scripting;

/// <summary>
/// Ejecuta un script C# con EF Core. El script recibe <c>db</c> y debe devolver un IQueryable,
/// una lista u otro valor. Todo corre dentro de una transacción que siempre se deshace.
/// </summary>
public static class ScriptRunner
{
    public static async Task<QueryResult> ExecuteAsync(
        string connectionString,
        IReadOnlyList<DynamicTable> tables,
        string code,
        int maxRows,
        CancellationToken ct = default)
    {
        var clock = Stopwatch.StartNew();

        var options = new DbContextOptionsBuilder<DynamicDbContext>()
            .UseSqlServer(connectionString)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
            .Options;

        await using var db = new DynamicDbContext(options, tables);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var scriptOptions = ScriptOptions.Default
            .WithReferences(
                typeof(DbContext).Assembly,
                typeof(EntityFrameworkQueryableExtensions).Assembly,
                typeof(ScriptGlobals).Assembly)
            .WithImports("System", "System.Linq", "System.Collections.Generic", "Microsoft.EntityFrameworkCore");

        try
        {
            var value = await CSharpScript.EvaluateAsync<object?>(
                code, scriptOptions, new ScriptGlobals { db = db }, typeof(ScriptGlobals), ct);

            // Las filas se leen dentro de la transacción, antes de deshacerla.
            var items = await ToListAsync(value, maxRows + 1, ct);
            var truncated = items.Count > maxRows;
            if (truncated)
                items.RemoveAt(items.Count - 1);

            var (columns, rows) = ToGrid(items);
            return new QueryResult(columns, rows, truncated, null, clock.Elapsed);
        }
        finally
        {
            // Se deshace aunque la petición se haya cancelado.
            await tx.RollbackAsync(CancellationToken.None);
        }
    }

    /// <summary>Materializa el valor devuelto por el script como mucho hasta <paramref name="take"/> elementos.</summary>
    private static async Task<List<object?>> ToListAsync(object? value, int take, CancellationToken ct)
    {
        if (value is null)
            return [];

        if (value is IQueryable queryable)
        {
            var elementType = queryable.ElementType;

            var takeMethod = typeof(Queryable).GetMethods()
                .Single(m => m.Name == nameof(Queryable.Take)
                             && m.GetParameters().Length == 2
                             && m.GetParameters()[1].ParameterType == typeof(int))
                .MakeGenericMethod(elementType);
            var limited = (IQueryable)takeMethod.Invoke(null, [queryable, take])!;

            var listMethod = typeof(EntityFrameworkQueryableExtensions).GetMethods()
                .Single(m => m.Name == nameof(EntityFrameworkQueryableExtensions.ToListAsync)
                             && m.GetParameters().Length == 2
                             && m.GetParameters()[1].ParameterType == typeof(CancellationToken))
                .MakeGenericMethod(elementType);
            var task = (Task)listMethod.Invoke(null, [limited, ct])!;
            await task;

            var list = (IEnumerable)task.GetType().GetProperty("Result")!.GetValue(task)!;
            return list.Cast<object?>().ToList();
        }

        if (value is string)
            return [value];

        if (value is IEnumerable enumerable)
            return enumerable.Cast<object?>().Take(take).ToList();

        return [value];
    }

    /// <summary>Convierte elementos sueltos en filas: diccionarios, objetos (propiedades) o valores simples.</summary>
    private static (List<string> Columns, List<string?[]> Rows) ToGrid(List<object?> items)
    {
        var records = items.Select(ToRecord).ToList();

        var columns = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
            foreach (var key in record.Keys)
                if (seen.Add(key))
                    columns.Add(key);

        var rows = new List<string?[]>(records.Count);
        foreach (var record in records)
        {
            var row = new string?[columns.Count];
            for (var i = 0; i < columns.Count; i++)
                row[i] = record.TryGetValue(columns[i], out var v) ? Format(v) : null;
            rows.Add(row);
        }

        return (columns, rows);
    }

    private static IDictionary<string, object?> ToRecord(object? item) => item switch
    {
        null => new Dictionary<string, object?>(),
        IDictionary<string, object?> dict => dict,
        string or ValueType => new Dictionary<string, object?> { ["value"] = item },
        _ => item.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToDictionary(p => p.Name, p => p.GetValue(item)),
    };

    private static string? Format(object? value) => value switch
    {
        null => null,
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        byte[] bytes => $"<binary {bytes.Length} bytes>",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
