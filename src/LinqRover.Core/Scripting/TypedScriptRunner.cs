using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using LinqRover.Core.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Oracle.EntityFrameworkCore;

namespace LinqRover.Core.Scripting;

/// <summary>Clases generadas y compiladas en memoria para un esquema concreto.</summary>
public sealed record CompiledModel(string Namespace, Assembly Assembly, byte[] Image, string Source);

/// <summary>
/// Genera las clases del esquema, las compila con Roslyn en memoria y ejecuta el script contra el contexto tipado.
/// </summary>
public static class TypedScriptRunner
{
    public static CompiledModel Compile(DatabaseEngine engine, IReadOnlyList<DynamicTable> tables)
    {
        var ns = "LinqRover.Generated.M" + Guid.NewGuid().ToString("N");
        var source = ClassGenerator.Generate(tables, engine, ns);

        var tree = CSharpSyntaxTree.ParseText(source);
        var references = RuntimeReferences()
            .Append(MetadataReference.CreateFromFile(typeof(DbContext).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(ClassGenerator).Assembly.Location))
            // System.Linq puede no estar cargado todavía en el proceso: se referencia explícitamente.
            .Append(MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location))
            .DistinctBy(r => r.Display)
            .ToList();

        var compilation = CSharpCompilation.Create(ns, [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        if (!emit.Success)
        {
            var errors = emit.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.GetMessage());
            throw new InvalidOperationException("No se pudieron generar las clases: " + string.Join("\n", errors));
        }

        var image = ms.ToArray();
        // Contexto por defecto: el script (que se compila contra este ensamblado) lo resuelve por nombre.
        var assembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(image));
        return new CompiledModel(ns, assembly, image, source);
    }

    public static async Task<QueryResult> ExecuteAsync(
        DatabaseEngine engine,
        string connectionString,
        CompiledModel model,
        string code,
        int maxRows,
        CancellationToken ct = default)
    {
        var clock = Stopwatch.StartNew();

        var (columns, rows, truncated) = await EvaluateAsync(engine, connectionString, model, code, ct, async value =>
        {
            var items = await ScriptRunner.ToListAsync(value, maxRows + 1, ct);
            var isTruncated = items.Count > maxRows;
            if (isTruncated)
                items.RemoveAt(items.Count - 1);

            var (cols, grid) = ScriptRunner.ToGrid(items);
            return (cols, grid, isTruncated);
        });

        return new QueryResult(columns, rows, truncated, null, clock.Elapsed);
    }

    /// <summary>SQL que EF Core generaría para la consulta del script. La consulta no se ejecuta.</summary>
    public static Task<string> GenerateSqlAsync(
        DatabaseEngine engine,
        string connectionString,
        CompiledModel model,
        string code,
        CancellationToken ct = default) =>
        EvaluateAsync(engine, connectionString, model, code, ct, value =>
        {
            if (value is not IQueryable query)
                throw new InvalidOperationException("El script no devuelve una consulta (IQueryable), así que no hay SQL que mostrar.");
            return Task.FromResult(query.ToQueryString());
        });

    /// <summary>
    /// Evalúa el script contra el contexto tipado dentro de una transacción que siempre se deshace,
    /// y entrega el valor devuelto a <paramref name="onValue"/> antes de deshacerla.
    /// </summary>
    private static async Task<T> EvaluateAsync<T>(
        DatabaseEngine engine,
        string connectionString,
        CompiledModel model,
        string code,
        CancellationToken ct,
        Func<object?, Task<T>> onValue)
    {
        var contextType = model.Assembly.GetType($"{model.Namespace}.{ClassGenerator.ContextTypeName}")!;
        var globalsType = model.Assembly.GetType($"{model.Namespace}.{ClassGenerator.GlobalsTypeName}")!;

        var builder = new DbContextOptionsBuilder();
        switch (engine)
        {
            case DatabaseEngine.SqlServer:
                builder.UseSqlServer(connectionString);
                break;
            case DatabaseEngine.Oracle:
                builder.UseOracle(connectionString).ReplaceService<Microsoft.EntityFrameworkCore.Query.IQuerySqlGeneratorFactory, Oracle11QuerySqlGeneratorFactory>();
                break;
            default:
                throw new NotSupportedException($"Motor no soportado para scripts: {engine}");
        }
        var options = builder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options;

        await using var db = (DbContext)Activator.CreateInstance(contextType, options)!;
        var globals = Activator.CreateInstance(globalsType)!;
        globalsType.GetProperty("db")!.SetValue(globals, db);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var scriptOptions = ScriptOptions.Default
                .WithReferences(typeof(DbContext).Assembly, typeof(EntityFrameworkQueryableExtensions).Assembly, typeof(System.Linq.Enumerable).Assembly)
                .AddReferences(RuntimeReferences())
                // AddReferences, no WithReferences: WithReferences reemplazaría las referencias anteriores.
                .AddReferences(MetadataReference.CreateFromImage(model.Image))
                .WithImports("System", "System.Linq", "System.Collections.Generic", "Microsoft.EntityFrameworkCore", model.Namespace);

            var value = await CSharpScript.EvaluateAsync<object?>(code, scriptOptions, globals, globalsType, ct);
            return await onValue(value);
        }
        finally
        {
            // Se deshace aunque la consulta haya escrito algo.
            await tx.RollbackAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Referencias para Roslyn: las plataformas de confianza y todos los ensamblados cargados en el proceso.
    /// La lista de confianza sola no siempre incluye System.Linq en una app MAUI.
    /// </summary>
    internal static IEnumerable<MetadataReference> RuntimeReferences()
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => a.Location))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return paths.Select(path => MetadataReference.CreateFromFile(path));
    }
}
