namespace LinqRover.Services;

/// <summary>
/// Estado compartido del «workbench» (barra de actividades y barra de estado).
/// Las páginas lo actualizan al abrir o cerrar un espacio de trabajo; el layout se vuelve a pintar al cambiar.
/// </summary>
public sealed class WorkbenchState
{
    /// <summary>Conexión abierta en el espacio de trabajo, o null si no hay ninguna.</summary>
    public Guid? ActiveProfileId { get; private set; }
    public string? ConnectionName { get; private set; }
    public string? EngineName { get; private set; }
    public string? Schema { get; private set; }

    public event Action? Changed;

    public void OpenWorkspace(Guid profileId, string connectionName, string engineName, string? schema)
    {
        ActiveProfileId = profileId;
        ConnectionName = connectionName;
        EngineName = engineName;
        Schema = schema;
        Changed?.Invoke();
    }

    public void SetSchema(string? schema)
    {
        if (Schema == schema) return;
        Schema = schema;
        Changed?.Invoke();
    }

    public void CloseWorkspace()
    {
        if (ActiveProfileId is null) return;
        ActiveProfileId = null;
        ConnectionName = null;
        EngineName = null;
        Schema = null;
        Changed?.Invoke();
    }
}
