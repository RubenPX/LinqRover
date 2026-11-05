using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.SqlExpressions;
using Microsoft.EntityFrameworkCore.Storage;
using Oracle.EntityFrameworkCore;
using Oracle.EntityFrameworkCore.Query.Sql.Internal;

namespace LinqRover.Core.Scripting;

/// <summary>
/// Oracle.EntityFrameworkCore 10 solo genera <c>FETCH FIRST</c> y <c>OFFSET</c> (Oracle 12c o superior).
/// En Oracle 11g eso falla con ORA-00933. Este generador usa <c>ROWNUM</c>:
/// <list type="bullet">
/// <item>Take: <c>SELECT * FROM (consulta) WHERE ROWNUM &lt;= n</c>.</item>
/// <item>Skip (con Take opcional): ROWNUM en una subconsulta interior y filtro exterior sobre el número de fila.</item>
/// </list>
/// </summary>
public sealed class Oracle11QuerySqlGenerator(
    QuerySqlGeneratorDependencies dependencies,
    IRelationalTypeMappingSource typeMappingSource)
    : OracleQuerySqlGenerator(dependencies, typeMappingSource, OracleSQLCompatibility.DatabaseVersion19)
{
    private const string RowNumber = "RN__";

    // Oracle 11g no admite TRUE/FALSE en SQL. EF genera CASE WHEN … THEN True ELSE False (p. ej. en Any()):
    // dentro de un CASE con resultados booleanos se emiten 1 y 0.
    private bool _boolAsNumber;

    protected override Expression VisitCase(CaseExpression caseExpression)
    {
        var previous = _boolAsNumber;
        _boolAsNumber = caseExpression.ElseResult is SqlConstantExpression { Value: bool }
            || caseExpression.WhenClauses.Any(w => w.Result is SqlConstantExpression { Value: bool });
        try
        {
            return base.VisitCase(caseExpression);
        }
        finally
        {
            _boolAsNumber = previous;
        }
    }

    protected override Expression VisitSqlConstant(SqlConstantExpression sqlConstantExpression)
    {
        if (sqlConstantExpression.Value is bool value)
        {
            Sql.Append(value ? "1" : "0");
            return sqlConstantExpression;
        }
        return base.VisitSqlConstant(sqlConstantExpression);
    }

    protected override Expression VisitSelect(SelectExpression selectExpression)
    {
        var limit = selectExpression.Limit;
        var offset = selectExpression.Offset;
        if (limit is null && offset is null)
            return base.VisitSelect(selectExpression);

        // Se quitan límite y desplazamiento para que la generación base no emita FETCH/OFFSET.
        SetProperty(selectExpression, "Limit", null);
        SetProperty(selectExpression, "Offset", null);
        try
        {
            if (offset is null)
            {
                Sql.Append("SELECT * FROM (");
                base.VisitSelect(selectExpression);
                Sql.Append(") WHERE ROWNUM <= ");
                Visit(limit!);
            }
            else
            {
                // Las columnas de la proyección sin la columna auxiliar de número de fila.
                var columns = string.Join(", ", selectExpression.Projection.Select(p => Quote(p.Alias)));
                Sql.Append($"SELECT {columns} FROM (SELECT a__.*, ROWNUM AS {Quote(RowNumber)} FROM (");
                base.VisitSelect(selectExpression);
                Sql.Append(") a__");
                if (limit is not null)
                {
                    Sql.Append(" WHERE ROWNUM <= (");
                    Visit(offset);
                    Sql.Append(" + ");
                    Visit(limit);
                    Sql.Append(")");
                }
                Sql.Append($") WHERE {Quote(RowNumber)} > ");
                Visit(offset);
            }
        }
        finally
        {
            SetProperty(selectExpression, "Limit", limit);
            SetProperty(selectExpression, "Offset", offset);
        }

        return selectExpression;
    }

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static void SetProperty(SelectExpression select, string name, SqlExpression? value) =>
        typeof(SelectExpression)
            .GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetSetMethod(nonPublic: true)!
            .Invoke(select, [value]);
}

/// <summary>Sustituye la fábrica de generadores SQL de EF Core por <see cref="Oracle11QuerySqlGenerator"/>.</summary>
public sealed class Oracle11QuerySqlGeneratorFactory(
    QuerySqlGeneratorDependencies dependencies,
    IRelationalTypeMappingSource typeMappingSource) : IQuerySqlGeneratorFactory
{
    public QuerySqlGenerator Create() => new Oracle11QuerySqlGenerator(dependencies, typeMappingSource);
}
