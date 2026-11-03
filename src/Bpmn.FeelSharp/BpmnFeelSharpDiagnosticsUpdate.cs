using System.Collections.Immutable;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>A diagnostics snapshot for one editor generation. Does not include expression text or variable values.</summary>
/// <param name="ElementId">Selected BPMN element.</param>
/// <param name="EntryId">Property identifier.</param>
/// <param name="Revision">Property reset revision.</param>
/// <param name="Generation">Input generation within this editor.</param>
/// <param name="Diagnostics">Immutable advisory diagnostics.</param>
public sealed record BpmnFeelSharpDiagnosticsUpdate(string? ElementId, string EntryId, int Revision, int Generation,
    ImmutableArray<BpmnFeelSharpDiagnostic> Diagnostics);
