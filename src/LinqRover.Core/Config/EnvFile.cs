namespace LinqRover.Core.Config;

/// <summary>Lector mínimo de ficheros .env (CLAVE=valor). Ignora líneas vacías y comentarios (#).</summary>
public static class EnvFile
{
    public static IReadOnlyDictionary<string, string> Parse(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
                value = value[1..^1];

            values[key] = value;
        }
        return values;
    }

    /// <summary>Busca un .env subiendo desde <paramref name="startDirectory"/>. Devuelve null si no existe.</summary>
    public static string? FindUpwards(string startDirectory, string fileName = ".env")
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
