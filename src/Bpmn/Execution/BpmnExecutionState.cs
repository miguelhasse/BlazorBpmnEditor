using System.Text.Json.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Execution;

/// <summary>
/// What an engine reports an element is doing right now (implementation plan M6.5).
/// </summary>
/// <remarks>
/// Deliberately not a BPMN concept. Nothing here is read from or written to the document: an
/// execution state is supplied by a host that is watching a running instance, drawn over the
/// diagram, and forgotten. The specification is explicit that this editor does not execute BPMN
/// (section 1), so these statuses describe an observation, never a semantic the editor could
/// derive for itself.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<BpmnActivityStatus>))]
public enum BpmnActivityStatus
{
    /// <summary>Nothing reported. Draws no overlay at all.</summary>
    None,

    /// <summary>Running now.</summary>
    Active,

    /// <summary>Finished normally.</summary>
    Completed,

    /// <summary>Finished with an error.</summary>
    Failed,

    /// <summary>Stopped before it finished.</summary>
    Canceled,

    /// <summary>Reached, but blocked on something outside the process.</summary>
    Waiting,
}

/// <summary>How much an incident matters.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BpmnIncidentSeverity>))]
public enum BpmnIncidentSeverity
{
    /// <summary>Something to know about.</summary>
    Info,

    /// <summary>Something that may need attention.</summary>
    Warning,

    /// <summary>Something that has gone wrong.</summary>
    Error,
}

/// <summary>Whether animated tokens travel the traversed flows.</summary>
/// <remarks>
/// Off unless a host asks, and the plan is explicit about why: motion is a hazard for some users
/// and a cost for large diagrams, so the same traversed-flow data draws as a static highlight
/// until someone opts in.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<BpmnExecutionAnimation>))]
public enum BpmnExecutionAnimation
{
    /// <summary>Static traversal highlighting. The default.</summary>
    Disabled,

    /// <summary>A token marker moves along each traversed flow.</summary>
    Enabled,
}

/// <summary>Something the engine reported against one element.</summary>
/// <param name="Id">The engine's identifier for this incident.</param>
/// <param name="Message">What to show a reader.</param>
/// <param name="Severity">How much it matters.</param>
public sealed record BpmnIncident(string Id, string Message, BpmnIncidentSeverity Severity = BpmnIncidentSeverity.Error);

/// <summary>What one element is doing.</summary>
/// <param name="ElementId">The BPMN element this describes. Unknown ids are ignored, not an error.</param>
/// <param name="Status">Its reported status.</param>
/// <param name="TokenCount">
/// How many tokens are sitting on it. Zero draws no counter: a badge reading "0" says something
/// different from no badge at all, and only one of those is true.
/// </param>
/// <param name="Incidents">Incidents raised against it.</param>
public sealed record BpmnElementExecution(
    string ElementId,
    BpmnActivityStatus Status = BpmnActivityStatus.None,
    int TokenCount = 0,
    IReadOnlyList<BpmnIncident>? Incidents = null)
{
    /// <summary>Incidents raised against this element, never null.</summary>
    public IReadOnlyList<BpmnIncident> Incidents { get; init; } = Incidents ?? [];
}

/// <summary>Whether a connector has carried anything.</summary>
/// <param name="FlowId">The Sequence Flow this describes.</param>
/// <param name="Traversed">Whether a token has crossed it at least once.</param>
/// <param name="TraversalCount">How many times, when the engine counts.</param>
public sealed record BpmnFlowExecution(string FlowId, bool Traversed = true, int TraversalCount = 0);

/// <summary>
/// One engine's view of one running instance, as the host supplies it.
/// </summary>
/// <remarks>
/// A whole snapshot rather than a stream of deltas. An overlay that accumulated patches would
/// drift from the engine the first time one was missed, and a host watching a process already has
/// the current state in hand — sending all of it is both simpler and self-correcting.
/// </remarks>
/// <param name="InstanceId">The engine's identifier for the running instance.</param>
/// <param name="Elements">Per-element status, tokens and incidents.</param>
/// <param name="Flows">Which connectors have carried a token.</param>
public sealed record BpmnExecutionState(
    string InstanceId,
    IReadOnlyList<BpmnElementExecution>? Elements = null,
    IReadOnlyList<BpmnFlowExecution>? Flows = null)
{
    /// <summary>Per-element status, tokens and incidents; never null.</summary>
    public IReadOnlyList<BpmnElementExecution> Elements { get; init; } = Elements ?? [];

    /// <summary>Which connectors have carried a token; never null.</summary>
    public IReadOnlyList<BpmnFlowExecution> Flows { get; init; } = Flows ?? [];

    /// <summary>Whether this state would draw nothing.</summary>
    public bool IsEmpty => Elements.Count == 0 && Flows.Count == 0;
}
