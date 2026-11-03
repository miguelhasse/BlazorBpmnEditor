namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Host-supplied function documentation; execution requires a separately authorized provider.</summary>
/// <param name="Name">Case-sensitive FEEL name.</param>
/// <param name="Signature">Representative call signature.</param>
/// <param name="Description">Escaped textual documentation.</param>
/// <param name="Example">Optional trusted example text, never automatically evaluated.</param>
public sealed record BpmnFeelSharpFunction(string Name, string Signature, string Description, string? Example = null);
