using Microsoft.EntityFrameworkCore;

namespace LinqRover.Core.Scripting;

/// <summary>Base de los contextos de script: no permite guardar cambios.</summary>
public abstract class ReadOnlyDbContext : DbContext
{
    protected ReadOnlyDbContext(DbContextOptions options) : base(options)
    {
    }

    public override int SaveChanges() => throw ReadOnly();
    public override int SaveChanges(bool acceptAllChangesOnSuccess) => throw ReadOnly();
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw ReadOnly();
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) => throw ReadOnly();

    private static InvalidOperationException ReadOnly() =>
        new("Este contexto es de solo lectura: no se pueden guardar cambios.");
}
