using System.Xml;

using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// Renaming an element's id, and everything that names it.
/// </summary>
/// <remarks>
/// <para>
/// An id is not a property. Changing it without moving every reference produces a document that
/// still serialises and no longer loads — a Sequence Flow pointing at nothing, a
/// <c>BPMNShape</c> drawing an element that is not there. So this is its own command rather than a
/// value in <see cref="UpdatePropertiesCommand"/>: it is a whole-document rewrite that either
/// happens everywhere or nowhere.
/// </para>
/// <para>
/// It is deliberately not a rename of the *label*. The label is <c>name</c>, which is an ordinary
/// editable property; this is the identity other elements were written against.
/// </para>
/// </remarks>
public static partial class BpmnDocumentEditor
{
    private static BpmnEditResult ApplyRename(BpmnDocument document, RenameElementCommand command)
    {
        var element = document.Find(command.ElementId);
        if (element is null)
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-072",
                $"There is no element with id '{command.ElementId}' to rename.",
                command.ElementId));
        }

        if (element is not (BpmnFlowNode
            or BpmnSequenceFlow
            or BpmnMessageFlow
            or BpmnAssociation
            or BpmnDataAssociation
            or BpmnTextAnnotation
            or BpmnGroup
            or BpmnDataObjectReference
            or BpmnDataStoreReference
            or BpmnDataObject
            or BpmnProcess
            or BpmnCollaboration
            or BpmnParticipant
            or BpmnLaneSet
            or BpmnLane))
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-075",
                $"'{command.ElementId}' has references this editor cannot safely rewrite when renaming.",
                command.ElementId));
        }

        var proposed = command.NewId?.Trim() ?? string.Empty;

        if (proposed.Length == 0)
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-073",
                "An element must have an id.",
                command.ElementId));
        }

        if (string.Equals(proposed, command.ElementId, StringComparison.Ordinal))
        {
            // Not an error and not a change: renaming a thing to what it is called already is a
            // no-op, and pushing it onto the undo stack would give the user a step that undoes
            // nothing.
            return BpmnEditResult.Applied(document, []);
        }

        // An id is an xsd:ID, so it has to be an NCName: no spaces, no colon, not starting with a
        // digit. A document with an invalid id is one other tools refuse to open (section 10.6).
        if (!IsNCName(proposed))
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-073",
                $"'{proposed}' is not a valid BPMN id: an id must be an XML name, without spaces or a colon.",
                command.ElementId));
        }

        if (document.Find(proposed) is not null)
        {
            // D018 by another route: two elements sharing an id means every later reference to it
            // resolves to whichever the index happened to keep.
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-074",
                $"'{proposed}' is already the id of another element.",
                command.ElementId));
        }

        return BpmnEditResult.Applied(Rename(document, command.ElementId, proposed), []);
    }

    /// <summary>Rewrites an id and every reference to it, in one pass over the document.</summary>
    /// <remarks>
    /// The companion to <see cref="BpmnReferenceMap.Sites"/>, and the two are held together by a
    /// test rather than by hope: after a rename no site may still name the old id, and the number
    /// naming the new one must be the number that named the old one.
    /// </remarks>
    private static BpmnDocument Rename(BpmnDocument document, string oldId, string newId)
    {
        string Id(string value) => string.Equals(value, oldId, StringComparison.Ordinal) ? newId : value;
        string? Ref(string? value) => value is null ? null : Id(value);

        IReadOnlyList<string> Each(IReadOnlyList<string> values) =>
            values.Any(value => value == oldId) ? [.. values.Select(Id)] : values;

        return document with
        {
            FlowNodes = [.. document.FlowNodes.Select(node => node with
            {
                Id = Id(node.Id),
                ContainerId = Id(node.ContainerId),
                PoolId = Ref(node.PoolId),
                LaneId = Ref(node.LaneId),
                AttachedToRef = Ref(node.AttachedToRef),
                CalledElementRef = Ref(node.CalledElementRef),
                CalledElementQName = RenameCalledElementQName(node.CalledElementQName, oldId, newId, document.Definitions.TargetNamespace),
                MessageRef = Ref(node.MessageRef),
                DefaultFlowRef = Ref(node.DefaultFlowRef),
                EventDefinitions = [.. node.EventDefinitions.Select(definition => definition with
                {
                    Ref = Ref(definition.Ref),
                    ActivityRef = Ref(definition.ActivityRef),
                })],
                MultiInstanceLoop = node.MultiInstanceLoop is { } multi
                    ? multi with
                    {
                        LoopDataInputRef = Ref(multi.LoopDataInputRef),
                        LoopDataOutputRef = Ref(multi.LoopDataOutputRef),
                        InputDataItem = RenameLoopDataItem(multi.InputDataItem, oldId, newId),
                        OutputDataItem = RenameLoopDataItem(multi.OutputDataItem, oldId, newId),
                    }
                    : null,
            })],

            SequenceFlows = [.. document.SequenceFlows.Select(flow => flow with
            {
                Id = Id(flow.Id),
                SourceRef = Id(flow.SourceRef),
                TargetRef = Id(flow.TargetRef),
                ContainerId = Id(flow.ContainerId),
            })],

            MessageFlows = [.. document.MessageFlows.Select(flow => flow with
            {
                Id = Id(flow.Id),
                SourceRef = Id(flow.SourceRef),
                TargetRef = Id(flow.TargetRef),
                MessageRef = Ref(flow.MessageRef),
            })],

            Associations = [.. document.Associations.Select(association => association with
            {
                Id = Id(association.Id),
                SourceRef = Id(association.SourceRef),
                TargetRef = Id(association.TargetRef),
                ContainerId = Id(association.ContainerId),
            })],

            DataAssociations = [.. document.DataAssociations.Select(association => association with
            {
                Id = Id(association.Id),
                SourceRefs = Each(association.SourceRefs),
                TargetRef = Id(association.TargetRef),
                ContainerId = Id(association.ContainerId),
                OwnerId = Ref(association.OwnerId),
            })],

            TextAnnotations = [.. document.TextAnnotations.Select(annotation => annotation with
            {
                Id = Id(annotation.Id),
                ContainerId = Id(annotation.ContainerId),
            })],

            Groups = [.. document.Groups.Select(group => group with
            {
                Id = Id(group.Id),
                ContainerId = Id(group.ContainerId),
                CategoryValueRef = Ref(group.CategoryValueRef),
            })],

            DataObjectReferences = [.. document.DataObjectReferences.Select(reference => reference with
            {
                Id = Id(reference.Id),
                ContainerId = Id(reference.ContainerId),
                DataObjectRef = Ref(reference.DataObjectRef),
            })],

            DataStoreReferences = [.. document.DataStoreReferences.Select(reference => reference with
            {
                Id = Id(reference.Id),
                ContainerId = Id(reference.ContainerId),
                DataStoreRef = Ref(reference.DataStoreRef),
            })],

            DataObjects = [.. document.DataObjects.Select(data => data with
            {
                Id = Id(data.Id),
                ContainerId = Id(data.ContainerId),
                ItemSubjectRef = Ref(data.ItemSubjectRef),
            })],

            Processes = [.. document.Processes.Select(process => process with { Id = Id(process.Id) })],

            Collaboration = document.Collaboration is { } collaboration
                ? collaboration with
                {
                    Id = Id(collaboration.Id),
                    Participants = [.. collaboration.Participants.Select(participant => participant with
                    {
                        Id = Id(participant.Id),
                        ProcessRef = Ref(participant.ProcessRef),
                    })],
                }
                : null,

            LaneSets = [.. document.LaneSets.Select(laneSet => laneSet with
            {
                Id = Id(laneSet.Id),
                ContainerId = Id(laneSet.ContainerId),
                LaneIds = Each(laneSet.LaneIds),
            })],

            Lanes = [.. document.Lanes.Select(lane => lane with
            {
                Id = Id(lane.Id),
                LaneSetId = Id(lane.LaneSetId),
                ChildLaneSetId = Ref(lane.ChildLaneSetId),
                FlowNodeRefs = Each(lane.FlowNodeRefs),
            })],

            // DI last, and never skipped: a shape whose bpmnElement still names the old id draws
            // nothing, which is the failure a rename is most likely to leave behind.
            Diagrams = [.. document.Diagrams.Select(diagram => diagram with
            {
                Plane = diagram.Plane with
                {
                    BpmnElement = Ref(diagram.Plane.BpmnElement),
                    Shapes = [.. diagram.Plane.Shapes.Select(shape => shape with { BpmnElement = Id(shape.BpmnElement) })],
                    Edges = [.. diagram.Plane.Edges.Select(edge => edge with { BpmnElement = Id(edge.BpmnElement) })],
                },
            })],
        };
    }

    private static BpmnLoopDataItem? RenameLoopDataItem(BpmnLoopDataItem? item, string oldId, string newId) =>
        item is null
            ? null
            : item with
            {
                Id = item.Id is null ? null : string.Equals(item.Id, oldId, StringComparison.Ordinal) ? newId : item.Id,
                ItemSubjectRef = item.ItemSubjectRef is null
                    ? null
                    : string.Equals(item.ItemSubjectRef, oldId, StringComparison.Ordinal) ? newId : item.ItemSubjectRef,
            };

    private static BpmnQName? RenameCalledElementQName(
        BpmnQName? qname,
        string oldId,
        string newId,
        string targetNamespace)
    {
        if (qname is null
            || !string.Equals(qname.NamespaceUri, targetNamespace, StringComparison.Ordinal)
            || !string.Equals(qname.LocalName, oldId, StringComparison.Ordinal))
        {
            return qname;
        }

        var separator = qname.LexicalValue.IndexOf(':');
        var lexical = separator < 0
            ? newId
            : $"{qname.LexicalValue[..(separator + 1)]}{newId}";

        return qname with { LocalName = newId, LexicalValue = lexical };
    }

    /// <summary>True when the value is a valid XML NCName, which is what an id has to be.</summary>
    private static bool IsNCName(string value)
    {
        try
        {
            return string.Equals(XmlConvert.VerifyNCName(value), value, StringComparison.Ordinal);
        }
        catch (XmlException)
        {
            return false;
        }
    }
}
