namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Opt-in native/Fluent editor spacing, with matching input/highlight metrics.</summary>
public enum BpmnFeelSharpDensity
{
    /// <summary>Existing editor metrics.</summary>
    Standard,
    /// <summary>Reduced padding for compact property panels.</summary>
    Compact,
    /// <summary>Increased padding for expanded authoring surfaces.</summary>
    Comfortable,
}
