using System.Text.Json;
using System.Text.Json.Serialization;
using LinqRover.Core.Models;

namespace LinqRover.Core.Storage;

/// <summary>Cifra/descifra texto. La implementación de Windows usa DPAPI (ligada al usuario actual).</summary>
public interface ISecretProtector
{
    string Protect(string plainText);
    string Unprotect(string protectedText);
}

/// <summary>Guarda los perfiles en un archivo local cifrado entero (el JSON completo pasa por <see cref="ISecretProtector"/>).</summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly ISecretProtector _protector;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public ProfileStore(string path, ISecretProtector protector)
    {
        _path = path;
        _protector = protector;
    }

    public async Task<IReadOnlyList<DbConnectionProfile>> GetAllAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var stored = await ReadAsync(ct);
            return stored
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ToProfile)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(DbConnectionProfile profile, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var stored = await ReadAsync(ct);
            stored.RemoveAll(s => s.Id == profile.Id);
            stored.Add(ToStored(profile));
            await WriteAsync(stored, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var stored = await ReadAsync(ct);
            if (stored.RemoveAll(s => s.Id == id) > 0)
                await WriteAsync(stored, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<StoredProfile>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
            return [];

        var content = (await File.ReadAllTextAsync(_path, ct)).Trim();

        // Formato antiguo: JSON en texto plano. Se lee y se reescribe cifrado de inmediato.
        if (content.StartsWith('['))
        {
            var legacy = JsonSerializer.Deserialize<List<StoredProfile>>(content, JsonOptions) ?? [];
            await WriteAsync(legacy, ct);
            return legacy;
        }

        var json = _protector.Unprotect(content);
        return JsonSerializer.Deserialize<List<StoredProfile>>(json, JsonOptions) ?? [];
    }

    /// <summary>Escribe todo el archivo cifrado (DPAPI del usuario actual). Nada queda en texto plano.</summary>
    private async Task WriteAsync(List<StoredProfile> profiles, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(profiles, JsonOptions);
        var encrypted = _protector.Protect(json);

        var tmp = _path + ".tmp";
        await File.WriteAllTextAsync(tmp, encrypted, ct);
        File.Move(tmp, _path, overwrite: true);
    }

    private StoredProfile ToStored(DbConnectionProfile p) => new(
        p.Id, p.Name, p.Engine, p.Host, p.Port, p.Database, p.User,
        string.IsNullOrEmpty(p.Password) ? "" : _protector.Protect(p.Password),
        p.TrustServerCertificate, p.DefaultSchema);

    private DbConnectionProfile ToProfile(StoredProfile s) => new(
        s.Name, s.Engine, s.Host, s.Port, s.Database, s.User,
        string.IsNullOrEmpty(s.EncryptedPassword) ? "" : _protector.Unprotect(s.EncryptedPassword))
    {
        Id = s.Id,
        TrustServerCertificate = s.TrustServerCertificate,
        DefaultSchema = s.DefaultSchema,
    };

    internal sealed record StoredProfile(
        Guid Id,
        string Name,
        DatabaseEngine Engine,
        string Host,
        int Port,
        string Database,
        string User,
        string EncryptedPassword,
        bool TrustServerCertificate,
        string? DefaultSchema = null);
}
