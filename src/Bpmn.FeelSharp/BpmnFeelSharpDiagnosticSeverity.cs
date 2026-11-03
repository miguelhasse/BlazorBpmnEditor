namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Severity of an advisory language diagnostic, independent of property binding errors.</summary>
public enum BpmnFeelSharpDiagnosticSeverity
{
    /// <summary>Informational language guidance.</summary>
    Information,
    /// <summary>Advisory warning that does not block a commit.</summary>
    Warning,
    /// <summary>Language error that remains advisory to the property editor.</summary>
    Error,
}
