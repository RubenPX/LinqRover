namespace LinqRover.Core.Models;

/// <summary>
/// Perfil de conexión guardado por el usuario. La contraseña solo vive en memoria; el almacén la cifra en disco.
/// En SQL Server, <see cref="Database"/> es la base de datos inicial; en Oracle, el nombre de servicio.
/// </summary>
public sealed record DbConnectionProfile(
    string Name,
    DatabaseEngine Engine,
    string Host,
    int Port,
    string Database,
    string User,
    string Password)
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>SQL Server: acepta el certificado del servidor sin validarlo (solo redes de confianza).</summary>
    public bool TrustServerCertificate { get; init; }
}
