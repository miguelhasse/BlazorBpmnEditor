using System.Collections.Frozen;
using System.Globalization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Loretta.CodeAnalysis;
using Loretta.CodeAnalysis.Lua;

namespace Hasseware.AspNetCore.Components.Bpmn.Scripting.Lua;

/// <summary>Parses a Lua script and reports its syntax errors. The script is never run.</summary>
/// <remarks>
/// <para>
/// Loretta is a parser and nothing else: it builds a syntax tree and reports what it could not
/// read, so there is no interpreter to hand a script to. It recovers from an error and carries on,
/// which is why a script with three mistakes reports three findings, each with its exact span.
/// </para>
/// <para>
/// The dialect is the host's choice, because the engine that runs the script decides what is legal:
/// <c>//</c>, the bitwise operators and <c>&lt;const&gt;</c> are errors in Lua 5.1 and fine in 5.4.
/// It defaults to Lua 5.4. Only syntax is checked, so a <c>break</c> outside a loop or a
/// <c>goto</c> to a label that does not exist is not reported.
/// </para>
/// </remarks>
public sealed class BpmnLuaScriptValidator : IBpmnScriptValidator
{
    private const int MaxDiagnostics = 50;

    private readonly LuaParseOptions _options;

    /// <summary>Creates a validator for Lua 5.4.</summary>
    public BpmnLuaScriptValidator()
        : this(null)
    {
    }

    /// <summary>Creates a validator for a specific Lua dialect.</summary>
    /// <param name="syntax">The dialect, such as <see cref="LuaSyntaxOptions.Lua51"/> or <see cref="LuaSyntaxOptions.LuaJIT21"/>; Lua 5.4 when null.</param>
    public BpmnLuaScriptValidator(LuaSyntaxOptions? syntax)
    {
        _options = new LuaParseOptions(syntax ?? LuaSyntaxOptions.Lua54);
    }

    /// <inheritdoc />
    public IReadOnlySet<string> Formats { get; } = new[] { "lua" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IReadOnlyList<BpmnScriptDiagnostic> Validate(string format, string source, BpmnScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(source);

        var tree = LuaSyntaxTree.ParseText(source, _options);

        return
        [
            .. tree.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
                .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
                .Take(MaxDiagnostics)
                .Select(diagnostic => new BpmnScriptDiagnostic(
                    diagnostic.GetMessage(CultureInfo.InvariantCulture),
                    diagnostic.Location.SourceSpan.Start,
                    diagnostic.Location.SourceSpan.Length,
                    diagnostic.Severity == DiagnosticSeverity.Error ? BpmnSeverity.Error : BpmnSeverity.Warning)),
        ];
    }
}
