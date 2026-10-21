using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>Creates a process-backed Pool, wrapping existing Process content without moving it.</summary>
/// <param name="Id">The Participant id.</param>
/// <param name="ProcessId">An existing root Process, or the id of a new Process.</param>
/// <param name="Bounds">Default bounds when the Process has no drawn content.</param>
/// <param name="Name">The Pool name.</param>
/// <param name="IsHorizontal">The Pool orientation.</param>
public sealed record CreatePoolCommand(
    string Id, string ProcessId, Bounds Bounds, string? Name = "Pool", bool IsHorizontal = true) : BpmnEditCommand
{
    /// <summary>The diagram to wrap; the first diagram is used when omitted.</summary>
    public string? DiagramId { get; init; }

    /// <summary>Optional measured content bounds, including external labels without persisted DI.</summary>
    public Bounds? ContentBounds { get; init; }

    /// <inheritdoc />
    public override string Label => "Create Pool";
}
