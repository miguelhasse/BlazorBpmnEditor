using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The canvas host an overlay is rendered inside, cascaded by <see cref="BpmnCanvas"/> to its
/// child content.
/// </summary>
/// <remarks>
/// <para>
/// Overlays such as the context menu, the inline label editor and the contextual actions are
/// positioned against the canvas host's box, so they only make sense as its children. Receiving
/// this cascade is how a component knows it is inside one; its absence means it was rendered
/// somewhere its coordinates mean nothing.
/// </para>
/// <para>
/// The constructor is internal so only a canvas can produce one. The cascade is fixed: the
/// surface's identity never changes for the lifetime of the canvas, and the members read through
/// to the canvas's current values.
/// </para>
/// </remarks>
public sealed class BpmnCanvasSurface
{
    private readonly BpmnCanvas _canvas;

    internal BpmnCanvasSurface(BpmnCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        _canvas = canvas;
    }

    /// <summary>The canvas host element overlays are positioned against.</summary>
    /// <remarks>Captured on the canvas's first render; a default reference before that.</remarks>
    public ElementReference Host => _canvas.HostElement;

    /// <summary>The diagram the canvas was asked to show; the first declared one when null.</summary>
    public string? DiagramId => _canvas.DiagramId;

    /// <summary>The canvas that owns this surface.</summary>
    internal BpmnCanvas Canvas => _canvas;
}
