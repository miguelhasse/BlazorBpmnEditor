namespace Hasseware.AspNetCore.Components.Bpmn.Configuration;

/// <summary>The parts and property editors a UI kit's own composition renders when the host chose none.</summary>
/// <remarks>
/// <para>
/// A composition (<c>BpmnEditor</c>, <c>FluentBpmnEditor</c>) hands its kit's defaults to its
/// <see cref="BpmnEditorScope"/>, so two kits can share one DI container: each composition renders
/// its own kit, whichever kit registered first.
/// </para>
/// <para>
/// Resolution order: instance slot, then the host's <see cref="BpmnEditorComponentOptions.Use{TComponent}"/> /
/// <c>AddEditor</c> choices, then these
/// kit defaults, then the app-wide <c>UseDefault</c> registrations.
/// </para>
/// </remarks>
public sealed class BpmnKitDefaults
{
    /// <summary>The parts the kit renders by default; fill with <see cref="BpmnEditorComponentOptions.UseDefault{TComponent}"/>.</summary>
    public BpmnEditorComponentOptions Components { get; } = new();

    /// <summary>The property editors the kit renders by default; fill with <c>UseDefaultEditor</c>.</summary>
    public BpmnPropertyEditorRegistry Editors { get; } = new();
}
