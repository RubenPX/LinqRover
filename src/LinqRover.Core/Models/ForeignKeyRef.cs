namespace LinqRover.Core.Models;

/// <summary>
/// Una columna de una clave foránea. Una FK compuesta da una entrada por columna, con el mismo <see cref="ConstraintName"/>.
/// <see cref="Child"/> es la tabla que contiene la FK; <see cref="Parent"/> la tabla referenciada.
/// </summary>
public sealed record ForeignKeyRef(
    string ConstraintName,
    TableInfo Child,
    string ChildColumn,
    TableInfo Parent,
    string ParentColumn);
