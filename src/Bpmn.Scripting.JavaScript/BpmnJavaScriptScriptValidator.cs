using System.Collections.Frozen;
using Acornima;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Scripting.JavaScript;

/// <summary>Parses a JavaScript script and reports its first syntax error. The script is never run.</summary>
/// <remarks>
/// A top-level <c>return</c> is accepted, because script engines run these bodies as the inside of a
/// function. The parser stops at the first error, so at most one finding is reported per script.
/// </remarks>
public sealed class BpmnJavaScriptScriptValidator : IBpmnScriptValidator
{
    private static readonly ParserOptions Options = ParserOptions.Default with { AllowReturnOutsideFunction = true };

    /// <inheritdoc />
    public IReadOnlySet<string> Formats { get; } =
        new[] { "javascript", "js", "ecmascript", "text/javascript", "application/javascript" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IReadOnlyList<BpmnScriptDiagnostic> Validate(string format, string source, BpmnScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            new Parser(Options).ParseScript(source);
            return [];
        }
        catch (ParseErrorException error)
        {
            return [new BpmnScriptDiagnostic(error.Error.Description, error.Error.Index, 0)];
        }
    }
}
