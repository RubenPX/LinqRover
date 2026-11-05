using LinqRover.Core.Config;
using LinqRover.Core.Models;

namespace LinqRover.Services;

/// <summary>Valores por defecto para el formulario de nueva conexión, leídos de un .env (solo desarrollo).</summary>
public sealed class DefaultConnectionSettings
{
    public DatabaseEngine? Engine { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? Database { get; init; }
    public string? User { get; init; }
    public string? Password { get; init; }
    public bool TrustServerCertificate { get; init; }

    public static DefaultConnectionSettings Load()
    {
        var path = EnvFile.FindUpwards(AppContext.BaseDirectory);
        if (path is null)
            return new DefaultConnectionSettings();

        var v = EnvFile.Parse(path);
        string? Get(string key) => v.TryGetValue(key, out var s) && s.Length > 0 ? s : null;

        return new DefaultConnectionSettings
        {
            Engine = Get("LR_ENGINE") is { } e && Enum.TryParse<DatabaseEngine>(e, ignoreCase: true, out var engine) ? engine : null,
            Host = Get("LR_HOST"),
            Port = Get("LR_PORT") is { } p && int.TryParse(p, out var port) ? port : null,
            Database = Get("LR_DB"),
            User = Get("LR_USER"),
            Password = Get("LR_PASS"),
            TrustServerCertificate = Get("LR_TRUST_CERT") == "1",
        };
    }
}
