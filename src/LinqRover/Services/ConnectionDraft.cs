using LinqRover.Core.Models;

namespace LinqRover.Services;

/// <summary>Estado editable del formulario de conexión. Se convierte a <see cref="DbConnectionProfile"/> al guardar o probar.</summary>
public sealed class ConnectionDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DatabaseEngine Engine { get; set; } = DatabaseEngine.Oracle;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = DefaultPort(DatabaseEngine.Oracle);
    public string Database { get; set; } = "";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public bool TrustServerCertificate { get; set; }

    /// <summary>Esquema por defecto (solo Oracle). Se rellena con la lista de esquemas del servidor.</summary>
    public string? DefaultSchema { get; set; }

    public static int DefaultPort(DatabaseEngine engine) => engine switch
    {
        DatabaseEngine.Oracle => 1521,
        DatabaseEngine.SqlServer => 1433,
        _ => 0,
    };

    /// <summary>Borrador nuevo con los valores del .env (si existe) como punto de partida.</summary>
    public static ConnectionDraft FromDefaults(DefaultConnectionSettings d)
    {
        var draft = new ConnectionDraft();
        if (d.Engine is { } engine)
        {
            draft.Engine = engine;
            draft.Port = DefaultPort(engine);
        }
        draft.Host = d.Host ?? draft.Host;
        draft.Port = d.Port ?? draft.Port;
        draft.Database = d.Database ?? draft.Database;
        draft.User = d.User ?? draft.User;
        draft.Password = d.Password ?? draft.Password;
        draft.TrustServerCertificate = d.TrustServerCertificate;
        return draft;
    }

    public static ConnectionDraft From(DbConnectionProfile? profile) => profile is null
        ? new ConnectionDraft()
        : new ConnectionDraft
        {
            Id = profile.Id,
            Name = profile.Name,
            Engine = profile.Engine,
            Host = profile.Host,
            Port = profile.Port,
            Database = profile.Database,
            User = profile.User,
            Password = profile.Password,
            TrustServerCertificate = profile.TrustServerCertificate,
            DefaultSchema = profile.DefaultSchema,
        };

    public DbConnectionProfile ToProfile() => new(
        Name.Trim(), Engine, Host.Trim(), Port, Database.Trim(), User.Trim(), Password)
    {
        Id = Id,
        TrustServerCertificate = Engine == DatabaseEngine.SqlServer && TrustServerCertificate,
        DefaultSchema = Engine == DatabaseEngine.Oracle ? DefaultSchema : null,
    };

    /// <summary>Devuelve un mensaje de error o null si el formulario es válido.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "El nombre es obligatorio.";
        if (string.IsNullOrWhiteSpace(Host)) return "El host es obligatorio.";
        if (Port is <= 0 or > 65535) return "El puerto debe estar entre 1 y 65535.";
        if (string.IsNullOrWhiteSpace(Database))
            return Engine == DatabaseEngine.Oracle ? "El servicio es obligatorio." : "La base de datos es obligatoria.";
        if (string.IsNullOrWhiteSpace(User)) return "El usuario es obligatorio.";
        return null;
    }
}
