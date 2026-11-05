using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LinqRover.Core.Scripting;

/// <summary>Tabla descrita en tiempo de ejecución. Sus filas se leen como diccionarios de columna → valor.</summary>
public sealed record DynamicTable(string Schema, string Name, IReadOnlyList<DynamicColumn> Columns)
{
    public string Key => $"{Schema}.{Name}";
}

/// <summary>
/// DbContext de solo lectura creado en tiempo de ejecución. Cada tabla es una entidad de tipo compartido
/// sin clave, así no hace falta generar clases. Cualquier intento de guardar cambios lanza una excepción.
/// </summary>
public sealed class DynamicDbContext : DbContext
{
    private readonly IReadOnlyList<DynamicTable> _tables;

    public DynamicDbContext(DbContextOptions<DynamicDbContext> options, IReadOnlyList<DynamicTable> tables)
        : base(options)
    {
        _tables = tables;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        foreach (var table in _tables)
        {
            modelBuilder.SharedTypeEntity<Dictionary<string, object?>>(table.Key, b =>
            {
                b.ToTable(table.Name, table.Schema);
                b.HasNoKey();
                foreach (var column in table.Columns)
                    MapColumn(b, column);
            });
        }
    }

    /// <summary>
    /// Mapea una columna con su tipo CLR real. EF no sabe mapear propiedades de tipo object,
    /// así que se llama a IndexerProperty&lt;T&gt; por reflexión con el T que corresponde al tipo SQL.
    /// Las columnas de tipos no soportados se omiten.
    /// </summary>
    private static void MapColumn(EntityTypeBuilder<Dictionary<string, object?>> builder, DynamicColumn column)
    {
        var clrType = SqlTypeMap.ToClrType(column.SqlType);
        if (clrType is null)
            return;

        var method = typeof(EntityTypeBuilder<Dictionary<string, object?>>)
            .GetMethods()
            .Single(m => m.Name == nameof(EntityTypeBuilder.IndexerProperty)
                         && m.IsGenericMethodDefinition
                         && m.GetParameters().Length == 1);

        method.MakeGenericMethod(clrType).Invoke(builder, [column.Name]);
    }

    public override int SaveChanges() => throw ReadOnly();
    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw ReadOnly();
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw ReadOnly();
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) => throw ReadOnly();

    private static InvalidOperationException ReadOnly() =>
        new("Este contexto es de solo lectura: no se pueden guardar cambios.");
}

/// <summary>Variables globales disponibles en el script. <c>db</c> es el contexto de la conexión.</summary>
public sealed class ScriptGlobals
{
    public DynamicDbContext db { get; init; } = null!;
}
