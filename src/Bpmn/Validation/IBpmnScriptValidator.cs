namespace Hasseware.AspNetCore.Components.Bpmn.Validation;

/// <summary>A syntax finding in a script. Offsets and lengths are UTF-16 code units into the script body.</summary>
/// <param name="Message">What is wrong, in the language's own terms.</param>
/// <param name="Offset">Where it starts.</param>
/// <param name="Length">How much it covers; zero for a position.</param>
/// <param name="Severity">How much it matters.</param>
public sealed record BpmnScriptDiagnostic(
    string Message,
    int Offset,
    int Length,
    BpmnSeverity Severity = BpmnSeverity.Error);

/// <summary>Where a script lives, for a validator that wants to say more than "line 3".</summary>
/// <param name="ElementId">The Script Task's id.</param>
/// <param name="ElementName">The Script Task's name, when it has one.</param>
public sealed record BpmnScriptContext(string ElementId, string? ElementName);

/// <summary>
/// Replaceable, per-language syntax validation for a Script Task's script.
/// </summary>
/// <remarks>
/// <para>
/// One implementation owns one or more <c>scriptFormat</c> names. The core knows no language: the
/// C#, JavaScript and Lua backends are separate packages a host opts into, and a host with its own
/// engine registers its own.
/// </para>
/// <para>
/// A script in a BPMN document is code from whoever authored the document, so an implementation
/// must only parse. It must never run, compile-and-load or otherwise evaluate the script, and it
/// must bound its own work on hostile input.
/// </para>
/// </remarks>
public interface IBpmnScriptValidator
{
    /// <summary>The <c>scriptFormat</c> names this validator handles, compared ignoring case.</summary>
    public IReadOnlySet<string> Formats { get; }

    /// <summary>Checks a script's syntax.</summary>
    /// <param name="format">The task's <c>scriptFormat</c>, as written.</param>
    /// <param name="source">The script body.</param>
    /// <param name="context">The task the script belongs to.</param>
    /// <returns>Every syntax finding; empty when the script parses.</returns>
    public IReadOnlyList<BpmnScriptDiagnostic> Validate(string format, string source, BpmnScriptContext context);
}
