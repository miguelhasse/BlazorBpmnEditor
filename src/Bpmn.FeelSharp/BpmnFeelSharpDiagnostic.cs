namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>An advisory FEEL diagnostic. A missing range means the engine supplied no reliable location.</summary>
/// <param name="Code">Stable diagnostic identifier.</param>
/// <param name="Message">Original diagnostic message.</param>
/// <param name="Source">Diagnostic producer.</param>
/// <param name="Start">Optional UTF-16 offset in the expression without its persistence prefix.</param>
/// <param name="Length">Optional UTF-16 range length.</param>
public sealed record BpmnFeelSharpDiagnostic(string Code, string Message, string Source, int? Start = null, int? Length = null)
{
    /// <summary>Parse diagnostics are advisory warnings, not binding errors.</summary>
    public BpmnFeelSharpDiagnosticSeverity Severity { get; init; } = BpmnFeelSharpDiagnosticSeverity.Warning;
}
