using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// One place in a document that names an element by id.
/// </summary>
/// <param name="OwnerId">The element that holds the reference.</param>
/// <param name="OwnerKind">What kind of thing holds it, for display.</param>
/// <param name="Attribute">The BPMN or DI attribute it is written in.</param>
/// <param name="TargetId">The id it names.</param>
/// <param name="DiagramId">The diagram it lives on, for a DI reference.</param>
public sealed record BpmnReferenceSite(
    string OwnerId,
    string OwnerKind,
    string Attribute,
    string TargetId,
    string? DiagramId = null);

/// <summary>
/// Where a document names its own elements, and how to rewrite those names.
/// </summary>
/// <remarks>
/// <para>
/// Three questions share one answer here: what does this element point at (the property panel),
/// what points at this element (find usages), and what has to move when an id changes (rename).
/// They were on their way to being three separate lists of attribute names, and the third one
/// missing a site does not produce a wrong answer — it produces a document that no longer loads.
/// </para>
/// <para>
/// Containment counts. A node's <c>ContainerId</c>, a Lane's <c>flowNodeRefs</c> and a
/// <c>BPMNShape</c>'s <c>bpmnElement</c> are references by every meaning that matters to a rename,
/// even though a property panel would never show them and section 5.5 checks them in a different
/// layer. Leaving them out is how a rename silently orphans a shape.
/// </para>
/// <para>
/// The enumeration is explicit rather than reflective, so every site is reviewable — and
/// <c>ReferenceMapCoverageTests</c> fails if the model grows a reference-shaped property this file
/// has not been told about, which is what keeps "explicit" from meaning "out of date".
/// </para>
/// </remarks>
public static class BpmnReferenceMap
{
    /// <summary>Every reference in the document, semantic and DI.</summary>
    /// <param name="document">The document to walk.</param>
    /// <returns>One entry per reference actually written; empty attributes are not sites.</returns>
    public static IEnumerable<BpmnReferenceSite> Sites(BpmnDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (var node in document.FlowNodes)
        {
            yield return Site(node.Id, "Flow node", "containerId", node.ContainerId);
            // laneId is the node's projection of Lane membership. The Lane stays authoritative,
            // but the projection still names it, and a rename that leaves it behind points the
            // node at a Lane that is gone.
            foreach (var site in Optional(node.Id, "Flow node",
                ("poolId", node.PoolId),
                ("laneId", node.LaneId),
                ("attachedToRef", node.AttachedToRef),
                ("calledElementRef", node.CalledElementRef),
                ("messageRef", node.MessageRef),
                ("defaultFlowRef", node.DefaultFlowRef)))
            {
                yield return site;
            }

            foreach (var definition in node.EventDefinitions)
            {
                foreach (var site in Optional(node.Id, "Flow node",
                    ($"{Wire(definition.Type)}/ref", definition.Ref),
                    ($"{Wire(definition.Type)}/activityRef", definition.ActivityRef)))
                {
                    yield return site;
                }
            }

            if (node.MultiInstanceLoop is { } multi)
            {
                foreach (var site in Optional(node.Id, "Flow node",
                    ("multiInstance/loopDataInputRef", multi.LoopDataInputRef),
                    ("multiInstance/loopDataOutputRef", multi.LoopDataOutputRef),
                    ("multiInstance/inputDataItem/itemSubjectRef", multi.InputDataItem?.ItemSubjectRef),
                    ("multiInstance/outputDataItem/itemSubjectRef", multi.OutputDataItem?.ItemSubjectRef)))
                {
                    yield return site;
                }
            }
        }

        foreach (var flow in document.SequenceFlows)
        {
            yield return Site(flow.Id, "Sequence Flow", "sourceRef", flow.SourceRef);
            yield return Site(flow.Id, "Sequence Flow", "targetRef", flow.TargetRef);
            yield return Site(flow.Id, "Sequence Flow", "containerId", flow.ContainerId);
        }

        foreach (var flow in document.MessageFlows)
        {
            yield return Site(flow.Id, "Message Flow", "sourceRef", flow.SourceRef);
            yield return Site(flow.Id, "Message Flow", "targetRef", flow.TargetRef);
            foreach (var site in Optional(flow.Id, "Message Flow", ("messageRef", flow.MessageRef))) yield return site;
        }

        foreach (var association in document.Associations)
        {
            yield return Site(association.Id, "Association", "sourceRef", association.SourceRef);
            yield return Site(association.Id, "Association", "targetRef", association.TargetRef);
            yield return Site(association.Id, "Association", "containerId", association.ContainerId);
        }

        foreach (var association in document.DataAssociations)
        {
            foreach (var source in association.SourceRefs)
            {
                yield return Site(association.Id, "Data Association", "sourceRef", source);
            }

            yield return Site(association.Id, "Data Association", "targetRef", association.TargetRef);
            yield return Site(association.Id, "Data Association", "containerId", association.ContainerId);
            foreach (var site in Optional(association.Id, "Data Association", ("ownerId", association.OwnerId))) yield return site;
        }

        foreach (var annotation in document.TextAnnotations)
        {
            yield return Site(annotation.Id, "Text Annotation", "containerId", annotation.ContainerId);
        }

        foreach (var group in document.Groups)
        {
            yield return Site(group.Id, "Group", "containerId", group.ContainerId);
            foreach (var site in Optional(group.Id, "Group", ("categoryValueRef", group.CategoryValueRef))) yield return site;
        }

        foreach (var reference in document.DataObjectReferences)
        {
            yield return Site(reference.Id, "Data Object Reference", "containerId", reference.ContainerId);
            foreach (var site in Optional(reference.Id, "Data Object Reference", ("dataObjectRef", reference.DataObjectRef))) yield return site;
        }

        foreach (var reference in document.DataStoreReferences)
        {
            yield return Site(reference.Id, "Data Store Reference", "containerId", reference.ContainerId);
            foreach (var site in Optional(reference.Id, "Data Store Reference", ("dataStoreRef", reference.DataStoreRef))) yield return site;
        }

        foreach (var data in document.DataObjects)
        {
            yield return Site(data.Id, "Data Object", "containerId", data.ContainerId);
            foreach (var site in Optional(data.Id, "Data Object", ("itemSubjectRef", data.ItemSubjectRef))) yield return site;
        }

        foreach (var participant in document.Collaboration?.Participants ?? [])
        {
            foreach (var site in Optional(participant.Id, "Participant", ("processRef", participant.ProcessRef))) yield return site;
        }

        foreach (var laneSet in document.LaneSets)
        {
            yield return Site(laneSet.Id, "Lane set", "containerId", laneSet.ContainerId);
            foreach (var laneId in laneSet.LaneIds) yield return Site(laneSet.Id, "Lane set", "lane", laneId);
        }

        foreach (var lane in document.Lanes)
        {
            yield return Site(lane.Id, "Lane", "laneSetId", lane.LaneSetId);
            foreach (var site in Optional(lane.Id, "Lane", ("childLaneSet", lane.ChildLaneSetId))) yield return site;
            foreach (var member in lane.FlowNodeRefs) yield return Site(lane.Id, "Lane", "flowNodeRef", member);
        }

        foreach (var diagram in document.Diagrams)
        {
            foreach (var site in Optional(diagram.Plane.Id, "Plane", ("bpmnElement", diagram.Plane.BpmnElement), diagram.Id))
            {
                yield return site;
            }

            foreach (var shape in diagram.Plane.Shapes)
            {
                foreach (var site in Optional(shape.Id, "Shape", ("bpmnElement", shape.BpmnElement), diagram.Id)) yield return site;
            }

            foreach (var edge in diagram.Plane.Edges)
            {
                foreach (var site in Optional(edge.Id, "Edge", ("bpmnElement", edge.BpmnElement), diagram.Id)) yield return site;
            }
        }
    }

    /// <summary>Every place the document names <paramref name="elementId"/>.</summary>
    /// <param name="document">The document to search.</param>
    /// <param name="elementId">The id to look for.</param>
    /// <returns>The sites naming it, in document order.</returns>
    public static IReadOnlyList<BpmnReferenceSite> UsagesOf(BpmnDocument document, string elementId)
    {
        ArgumentNullException.ThrowIfNull(elementId);

        return [.. Sites(document).Where(site => string.Equals(site.TargetId, elementId, StringComparison.Ordinal))];
    }

    private static BpmnReferenceSite Site(string ownerId, string kind, string attribute, string targetId) =>
        new(ownerId, kind, attribute, targetId);

    /// <summary>Sites for attributes that may be absent; an unwritten reference is not a site.</summary>
    private static IEnumerable<BpmnReferenceSite> Optional(
        string ownerId,
        string kind,
        params (string Attribute, string? TargetId)[] candidates) => Optional(ownerId, kind, null, candidates);

    private static IEnumerable<BpmnReferenceSite> Optional(
        string ownerId,
        string kind,
        string? diagramId,
        params (string Attribute, string? TargetId)[] candidates) => candidates
            .Where(candidate => !string.IsNullOrEmpty(candidate.TargetId))
            .Select(candidate => new BpmnReferenceSite(ownerId, kind, candidate.Attribute, candidate.TargetId!, diagramId));

    private static IEnumerable<BpmnReferenceSite> Optional(
        string ownerId,
        string kind,
        (string Attribute, string? TargetId) candidate,
        string diagramId) => Optional(ownerId, kind, diagramId, candidate);

    private static string Wire(BpmnEventDefinitionType type) =>
        System.Text.Json.JsonSerializer.Serialize(type).Trim('"');
}
