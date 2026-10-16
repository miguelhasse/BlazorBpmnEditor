using System.Collections.Frozen;
using System.Globalization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Hasseware.AspNetCore.Components.Bpmn.Scripting.CSharp;

/// <summary>Parses a C# script and reports its syntax errors. The script is never compiled or run.</summary>
/// <remarks>
/// The script is read as a C# script (<see cref="SourceCodeKind.Script"/>), so top-level statements
/// and a top-level <c>return</c> are legal, as they are in the scripting engines that run these.
/// Only the parser runs: names, types and references are not resolved, because which of them exist
/// is the engine's business and not something the document can know.
/// </remarks>
public sealed class BpmnCSharpScriptValidator : IBpmnScriptValidator
{
    private const int MaxDiagnostics = 50;

    private static readonly CSharpParseOptions ParseOptions =
        new(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Script);

    /// <inheritdoc />
    public IReadOnlySet<string> Formats { get; } = new[] { "csharp", "c#", "cs" }
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IReadOnlyList<BpmnScriptDiagnostic> Validate(string format, string source, BpmnScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tree = CSharpSyntaxTree.ParseText(source, ParseOptions);

        return
        [
            .. tree.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
                .Take(MaxDiagnostics)
                .Select(diagnostic => new BpmnScriptDiagnostic(
                    $"{diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}",
                    diagnostic.Location.SourceSpan.Start,
                    diagnostic.Location.SourceSpan.Length,
                    diagnostic.Severity == DiagnosticSeverity.Error ? BpmnSeverity.Error : BpmnSeverity.Warning)),
        ];
    }
}
