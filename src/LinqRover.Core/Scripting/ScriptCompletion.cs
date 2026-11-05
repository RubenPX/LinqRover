using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Text;

namespace LinqRover.Core.Scripting;

/// <summary>Sugerencia de autocompletado ya traducida a un formato simple para el editor.</summary>
public sealed record CompletionSuggestion(string Label, string Kind, string InsertText, string SortText);

/// <summary>
/// Autocompletado de C# con Roslyn para el script de EF Core. El script se envuelve en un método que recibe
/// <c>db</c> tipado con las clases generadas, así el compilador conoce tablas, columnas y tipos de las lambdas.
/// Una instancia está ligada a un modelo compilado (un esquema concreto).
/// </summary>
public sealed class ScriptCompletion : IDisposable
{
    private const int MaxItems = 200;
    private const string Tail = "\n}\n}\n}";

    // Etiquetas de tipo de elemento que Roslyn asigna a cada sugerencia (el resto son de accesibilidad o estáticas).
    private static readonly HashSet<string> KindTags = new(StringComparer.Ordinal)
    {
        "Method", "ExtensionMethod", "Property", "Field", "Event", "Class", "Struct", "Interface", "Enum",
        "EnumMember", "Delegate", "Namespace", "Module", "Local", "Parameter", "RangeVariable", "Constant",
        "Keyword", "Snippet", "TypeParameter", "Label",
    };

    private readonly AdhocWorkspace _workspace = new();
    private readonly Document _document;
    private readonly string _prefix;

    public ScriptCompletion(CompiledModel model)
    {
        var contextType = $"{model.Namespace}.{ClassGenerator.ContextTypeName}";
        _prefix = "using System;\nusing System.Linq;\nusing System.Collections.Generic;\nusing Microsoft.EntityFrameworkCore;\n"
                + $"namespace LinqRover.Completion {{\npublic static class Script {{\npublic static object Run({contextType} db) {{\n";

        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var references = TypedScriptRunner.RuntimeReferences()
            .Append(MetadataReference.CreateFromImage(model.Image))
            .ToList();

        _workspace.AddSolution(SolutionInfo.Create(SolutionId.CreateNewId(), VersionStamp.Default, projects:
        [
            ProjectInfo.Create(projectId, VersionStamp.Default, "script", "script", LanguageNames.CSharp,
                metadataReferences: references,
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                parseOptions: new CSharpParseOptions(LanguageVersion.Latest)),
        ]));
        _workspace.AddDocument(DocumentInfo.Create(documentId, "Script.cs"));
        _document = _workspace.CurrentSolution.GetDocument(documentId)!;
    }

    /// <summary>Sugerencias en la posición <paramref name="cursor"/> del script (índice de carácter).
    /// Se calculan en el threadpool: Roslyn es CPU intensivo y no debe bloquear la interfaz.</summary>
    public Task<IReadOnlyList<CompletionSuggestion>> GetAsync(string script, int cursor, CancellationToken ct = default) =>
        Task.Run(async () => await GetCoreAsync(script, cursor, ct), ct);

    private async Task<IReadOnlyList<CompletionSuggestion>> GetCoreAsync(string script, int cursor, CancellationToken ct)
    {
        cursor = Math.Clamp(cursor, 0, script.Length);
        var document = _document.WithText(SourceText.From(_prefix + script + Tail));

        var service = CompletionService.GetService(document);
        if (service is null)
            return [];

        var list = await service.GetCompletionsAsync(document, _prefix.Length + cursor, cancellationToken: ct);
        if (list is null)
            return [];

        // Los sobrecargas aparecen como elementos separados: en el editor basta una entrada por nombre.
        return list.ItemsList
            .DistinctBy(item => item.DisplayText)
            .Take(MaxItems)
            .Select(item => new CompletionSuggestion(
                item.DisplayText,
                item.Tags.FirstOrDefault(KindTags.Contains) ?? "Text",
                item.DisplayText,
                item.SortText))
            .ToList();
    }

    public void Dispose() => _workspace.Dispose();
}
