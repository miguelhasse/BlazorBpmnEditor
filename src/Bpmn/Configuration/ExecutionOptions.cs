using Hasseware.AspNetCore.Components.Bpmn.Execution;

namespace Hasseware.AspNetCore.Components.Bpmn.Configuration;

/// <summary>
/// How execution overlays are drawn (implementation plan M6.5).
/// </summary>
/// <remarks>
/// <para>
/// Only the motion is configurable. Status outlines, token counters and incident indicators are
/// the baseline presentation and are always drawn when a host supplies an execution state: they
/// are how the overlay says anything at all, and a configuration flag that could turn them off
/// would just be a slower way of sending no state.
/// </para>
/// <para>
/// An application-wide default only. It is deliberately <em>not</em> a section of
/// <c>EditorConfig</c>: that snapshot is sent to the canvas once, at initialisation, and nothing
/// applies a later revision to a live paper — so an animation setting carried in it would have
/// silently done nothing whenever it changed. Motion is a matter of presentation, the stylesheet
/// already owns every other aspect of how execution state looks, and the editor root is where
/// theme and contrast are announced for exactly the same reason.
/// </para>
/// </remarks>
/// <param name="Animation">Whether traversed flows animate a travelling token.</param>
public sealed record ExecutionOptions(BpmnExecutionAnimation Animation)
{
    /// <summary>Static traversal highlighting, which is the default presentation.</summary>
    public static ExecutionOptions Default { get; } = new(BpmnExecutionAnimation.Disabled);
}
