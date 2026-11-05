namespace LinqRover.Core.Models;

/// <summary>Motores soportados.</summary>
public enum DatabaseEngine
{
    Oracle,
    SqlServer
}

public static class DatabaseEngineInfo
{
    public static string DisplayName(this DatabaseEngine engine) => engine switch
    {
        DatabaseEngine.Oracle => "Oracle",
        DatabaseEngine.SqlServer => "SQL Server",
        _ => engine.ToString(),
    };
}
