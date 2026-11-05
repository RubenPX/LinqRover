using System.Text.RegularExpressions;

namespace LinqRover.Core.Sql;

/// <summary>Comprobaciones previas a ejecutar SQL libre. Es un aviso, no una barrera: la seguridad real la da el rollback.</summary>
public static partial class SqlSafety
{
    private static readonly HashSet<string> ReadKeywords =
        new(StringComparer.OrdinalIgnoreCase) { "SELECT", "WITH", "SHOW", "DESCRIBE", "DESC", "EXPLAIN" };

    /// <summary>True si la primera sentencia parece de lectura (ignora comentarios y espacios iniciales).</summary>
    public static bool LooksReadOnly(string sql)
    {
        var text = CommentPattern().Replace(sql, " ").TrimStart();
        var first = FirstWord().Match(text);
        return first.Success && ReadKeywords.Contains(first.Value);
    }

    /// <summary>Quita el punto y coma final, que Oracle no admite dentro de un comando.</summary>
    public static string Normalize(string sql) => sql.Trim().TrimEnd(';').TrimEnd();

    [GeneratedRegex(@"--[^\r\n]*|/\*[\s\S]*?\*/")]
    private static partial Regex CommentPattern();

    [GeneratedRegex(@"^[A-Za-z]+")]
    private static partial Regex FirstWord();
}
