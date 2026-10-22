namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>A diagram color channel.</summary>
public enum BpmnColorChannel
{
    /// <summary>Shape fill.</summary>
    Background,
    /// <summary>Shape outline or connector line.</summary>
    Border,
    /// <summary>Label text.</summary>
    Text,
    /// <summary>All channels; only valid for reset.</summary>
    All,
}

/// <summary>Sets one channel, or removes overrides when Color is null, on selected DI items.</summary>
public sealed record SetColorsCommand(
    string DiagramId,
    IReadOnlyList<string> ElementIds,
    BpmnColorChannel Channel,
    string? Color = null) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => Color is null ? "Reset colors" : "Change color";
}
