using System.Globalization;
using System.Text;
using LinqRover.Core.Models;

namespace LinqRover.Core.Scripting;

/// <summary>
/// Genera el código C# de las clases de entidad de un esquema: una clase por tabla, un DbSet por tabla
/// en un contexto (<c>db.Asignaturas</c>) y una clase con la variable <c>db</c> para los scripts.
/// </summary>
public static class ClassGenerator
{
    public const string ContextTypeName = "GeneratedDbContext";
    public const string GlobalsTypeName = "ScriptGlobals";

    private static readonly Dictionary<Type, string> TypeNames = new()
    {
        [typeof(int)] = "int",
        [typeof(long)] = "long",
        [typeof(short)] = "short",
        [typeof(byte)] = "byte",
        [typeof(bool)] = "bool",
        [typeof(decimal)] = "decimal",
        [typeof(double)] = "double",
        [typeof(float)] = "float",
        [typeof(DateTime)] = "DateTime",
        [typeof(DateTimeOffset)] = "DateTimeOffset",
        [typeof(TimeSpan)] = "TimeSpan",
        [typeof(Guid)] = "Guid",
        [typeof(string)] = "string",
        [typeof(byte[])] = "byte[]",
    };

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    // Miembros de DbContext que no pueden usarse como nombre de DbSet.
    private static readonly HashSet<string> ReservedContextMembers = new(StringComparer.Ordinal)
    {
        "Database", "Model", "ChangeTracker", "Set", "Entry", "Add", "Find", "Update", "Remove",
        "SaveChanges", "Dispose", "Equals", "GetHashCode", "GetType", "ToString",
    };

    /// <summary>Nombre del DbSet de una tabla en el contexto: <c>db.&lt;nombre&gt;</c>.</summary>
    public static string ContextMemberName(string tableName)
    {
        var id = Identifier(tableName);
        return ReservedContextMembers.Contains(id) ? id + "_" : id;
    }

    /// <summary>Genera el código fuente completo para un esquema, en el namespace indicado.</summary>
    public static string Generate(IReadOnlyList<DynamicTable> tables, DatabaseEngine engine, string ns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using Microsoft.EntityFrameworkCore;");
        sb.AppendLine("using LinqRover.Core.Scripting;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ns};");
        sb.AppendLine();

        var usedTypes = new HashSet<string>(StringComparer.Ordinal) { ContextTypeName, GlobalsTypeName };
        var entities = new List<(DynamicTable Table, string TypeName, string Member, List<(string Prop, string Column, string CSharp)> Props)>();

        foreach (var table in tables)
        {
            var typeName = Unique(Identifier(table.Name), usedTypes);
            var member = ContextMemberName(table.Name);
            var usedProps = new HashSet<string>(StringComparer.Ordinal);
            var props = new List<(string Prop, string Column, string CSharp)>();

            foreach (var column in table.Columns)
            {
                var clr = SqlTypeMap.ToClrType(column.SqlType, engine);
                if (clr is null)
                    continue; // tipo no soportado: la columna no se mapea

                var prop = Unique(Identifier(column.Name), usedProps);
                props.Add((prop, column.Name, CSharpType(clr)));
            }

            entities.Add((table, typeName, member, props));

            sb.AppendLine($"public class {typeName}");
            sb.AppendLine("{");
            foreach (var p in props)
                sb.AppendLine($"    public {p.CSharp} {p.Prop} {{ get; set; }}");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        sb.AppendLine($"public sealed class {ContextTypeName} : ReadOnlyDbContext");
        sb.AppendLine("{");
        sb.AppendLine($"    public {ContextTypeName}(DbContextOptions options) : base(options) {{ }}");
        sb.AppendLine();
        foreach (var e in entities)
            sb.AppendLine($"    public DbSet<{e.TypeName}> {e.Member} => Set<{e.TypeName}>();");
        sb.AppendLine();
        sb.AppendLine("    protected override void OnModelCreating(ModelBuilder modelBuilder)");
        sb.AppendLine("    {");
        foreach (var e in entities)
        {
            sb.AppendLine($"        modelBuilder.Entity<{e.TypeName}>(b =>");
            sb.AppendLine("        {");
            sb.AppendLine("            b.HasNoKey();");
            sb.AppendLine($"            b.ToTable({Literal(e.Table.Name)}, {Literal(e.Table.Schema)});");
            foreach (var p in e.Props)
                sb.AppendLine($"            b.Property(x => x.{p.Prop}).HasColumnName({Literal(p.Column)});");
            sb.AppendLine("        });");
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        // Variables del script: db es el contexto generado.
        sb.AppendLine($"public sealed class {GlobalsTypeName}");
        sb.AppendLine("{");
        sb.AppendLine($"    public {ContextTypeName} db {{ get; set; }} = null!;");
        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>Tipo C# de una columna: los valores pueden ser nulos, así que llevan <c>?</c>.</summary>
    public static string CSharpType(Type clr)
    {
        var baseType = Nullable.GetUnderlyingType(clr) ?? clr;
        var name = TypeNames[baseType];
        var nullable = clr != baseType || baseType == typeof(string) || baseType == typeof(byte[]);
        return nullable ? name + "?" : name;
    }

    /// <summary>Identificador válido de C#: caracteres no válidos → "_", sin dígito inicial, sin palabras reservadas.</summary>
    public static string Identifier(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name)
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');

        var id = sb.ToString();
        if (id.Length == 0 || char.IsDigit(id[0]))
            id = "_" + id;
        if (Keywords.Contains(id))
            id += "_";
        return id;
    }

    private static string Unique(string name, HashSet<string> used)
    {
        var candidate = name;
        var n = 2;
        while (!used.Add(candidate))
            candidate = name + "_" + n++.ToString(CultureInfo.InvariantCulture);
        return candidate;
    }

    /// <summary>Literal de cadena C# con las comillas y barras escapadas.</summary>
    private static string Literal(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
