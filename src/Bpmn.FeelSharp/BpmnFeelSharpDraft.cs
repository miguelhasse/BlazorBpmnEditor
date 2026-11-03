namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>A prefix-free native draft and its UTF-16 selection, for explicit host actions.</summary>
/// <param name="Text">Expression text without BPMN persistence prefix.</param>
/// <param name="Start">Selection start.</param>
/// <param name="End">Selection end.</param>
public sealed record BpmnFeelSharpDraft(string Text, int Start, int End)
{
    internal object? SourceIdentity { get; init; }
}
