using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Model;

/// <summary>A Sequence Flow (specification section 1.4).</summary>
/// <param name="Id">Element id, unique within the document.</param>
/// <param name="SourceRef">The originating flow node.</param>
/// <param name="TargetRef">The receiving flow node.</param>
/// <param name="ContainerId">
/// The Process or Sub-Process both endpoints belong to. A Sequence Flow never crosses this
/// boundary or a Pool boundary (SF-006, SF-007), which is why the scope is stored rather than
/// re-derived from the endpoints on every check.
/// </param>
/// <param name="Name">Connector label.</param>
/// <param name="ConditionExpression">
/// The guard expression, when the flow is conditional. Mutually exclusive with
/// <paramref name="IsDefault"/>: a default flow is taken precisely when no condition holds.
/// </param>
/// <param name="IsDefault">
/// Whether this is the source's default flow. A projection: the <c>default</c> attribute lives
/// on the source node, and <see cref="BpmnFlowNode.DefaultFlowRef"/> stays authoritative.
/// </param>
/// <param name="ConditionLanguage">
/// The condition's expression language, when the flow overrides the document default.
/// </param>
public sealed record BpmnSequenceFlow(
    string Id,
    string SourceRef,
    string TargetRef,
    string ContainerId,
    string? Name = null,
    string? ConditionExpression = null,
    bool IsDefault = false,
    string? ConditionLanguage = null)
{
    /// <summary>Documentation, foreign attributes and vendor extensions.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}


/// <summary>
/// A Message Flow, which always crosses exactly one Pool boundary (MF-002).
/// </summary>
/// <param name="Id">Element id.</param>
/// <param name="SourceRef">The sending Participant, Activity or throwing Event.</param>
/// <param name="TargetRef">The receiving Participant, Activity or catching Event.</param>
/// <param name="Name">Connector label.</param>
/// <param name="MessageRef">The Message definition carried, when declared.</param>
public sealed record BpmnMessageFlow(
    string Id,
    string SourceRef,
    string TargetRef,
    string? Name = null,
    string? MessageRef = null)
{
    /// <summary>Documentation, foreign attributes and vendor extensions.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}


/// <summary>An Association between an Artifact and a flow element (specification section 3.3).</summary>
/// <param name="Id">Element id.</param>
/// <param name="SourceRef">The Artifact or flow element the association starts at.</param>
/// <param name="TargetRef">The element the association points to.</param>
/// <param name="ContainerId">The Process or Sub-Process that owns the association.</param>
/// <param name="Direction">Whether the association is drawn with arrowheads.</param>
public sealed record BpmnAssociation(
    string Id,
    string SourceRef,
    string TargetRef,
    string ContainerId,
    BpmnAssociationDirection Direction = BpmnAssociationDirection.None)
{
    /// <summary>Documentation, foreign attributes and vendor extensions.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}


/// <summary>A Data Association (specification section 2.9).</summary>
/// <param name="Id">Element id.</param>
/// <param name="Kind">Whether data flows into or out of the Activity or Event.</param>
/// <param name="SourceRefs">
/// A list, not a single reference: without a transformation there must be exactly one source,
/// with one there may be zero or many (DA-007).
/// </param>
/// <param name="TargetRef">The receiving element.</param>
/// <param name="ContainerId">The Process or Sub-Process that owns the association.</param>
/// <param name="Transformation">The transformation expression, when declared.</param>
public sealed record BpmnDataAssociation(
    string Id,
    BpmnDataAssociationKind Kind,
    IReadOnlyList<string> SourceRefs,
    string TargetRef,
    string ContainerId,
    string? Transformation = null)
{
    /// <summary>The Activity or Event that owns this association in XML.</summary>
    public string? OwnerId { get; init; }

    /// <summary>Assignments evaluated as part of this association.</summary>
    public IReadOnlyList<BpmnDataAssignment> Assignments { get; init; } = [];

    /// <summary>Documentation, foreign attributes and vendor extensions.</summary>
    public BpmnElementExtras Extras { get; init; } = BpmnElementExtras.None;
}

/// <summary>A BPMN Data Association assignment from one expression to another.</summary>
public sealed record BpmnDataAssignment(
    BpmnFormalExpression From,
    BpmnFormalExpression To);
