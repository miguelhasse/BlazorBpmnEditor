using System.Collections.Frozen;
using System.Globalization;

using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// Moving content from one Pool into another by dragging it there.
/// </summary>
/// <remarks>
/// <para>
/// A Pool is a Participant and the Process it references (section 1.5.1); what is drawn inside it
/// belongs to that Process. A shape dragged out of one Pool and dropped inside another therefore
/// changes owner — growing the source Pool to reach the drop point would draw it in a Pool whose
/// Process does not own it, and leave it overlapping the Pool it was actually dropped on.
/// </para>
/// <para>
/// A Sequence Flow cannot cross a Pool boundary (SF-007) and a Data Association cannot leave its
/// Process, so the connectors the transferred content takes with it are reconciled in the same
/// edit: a Sequence Flow that now crosses becomes a Message Flow where both ends may carry one,
/// a Message Flow whose ends now share a Pool becomes a Sequence Flow, and the rest are deleted.
/// </para>
/// </remarks>
public static partial class BpmnDocumentEditor
{
    private readonly record struct PoolTransfer(string ElementId, string Owner, string PoolId);

    /// <summary>
    /// Re-owns every moved shape whose centre lands in a Pool other than its own.
    /// </summary>
    /// <param name="document">The document before the move.</param>
    /// <param name="shapes">The current shapes, by id.</param>
    /// <param name="asked">The bounds the caller asked for.</param>
    /// <param name="carriedAnnotations">Notes moved implicitly with their targets.</param>
    /// <param name="diagnostics">Receives the EDIT-056 report.</param>
    /// <param name="rejection">The reason the move cannot be applied, when it cannot.</param>
    /// <returns>The document with the content transferred and its connectors reconciled.</returns>
    private static BpmnDocument TransferAcrossPools(
        BpmnDocument document,
        FrozenDictionary<string, BpmnShape> shapes,
        Dictionary<string, Bounds> asked,
        HashSet<string> carriedAnnotations,
        List<BpmnEditDiagnostic> diagnostics,
        out BpmnEditDiagnostic? rejection)
    {
        rejection = null;
        if (document.Collaboration is not { } collaboration) return document;

        var pools = collaboration.Participants
            .Select(participant => (Participant: participant,
                Shape: shapes.Values.FirstOrDefault(shape => shape.BpmnElement == participant.Id)))
            .Where(entry => entry.Shape is not null)
            .Select(entry => (entry.Participant, Shape: entry.Shape!))
            .ToList();
        if (pools.Count < 2) return document;

        var movedElements = asked.Keys
            .Select(id => shapes[id].BpmnElement)
            .ToHashSet(StringComparer.Ordinal);

        var transfers = new List<PoolTransfer>();

        foreach (var (shapeId, bounds) in asked)
        {
            var shape = shapes[shapeId];
            if (carriedAnnotations.Contains(shape.BpmnElement)) continue;
            var element = document.Find(shape.BpmnElement);

            var containerId = element switch
            {
                BpmnFlowNode { AttachedToRef: null } node => node.ContainerId,
                BpmnDataObjectReference reference => reference.ContainerId,
                BpmnDataStoreReference reference => reference.ContainerId,
                BpmnTextAnnotation annotation => annotation.ContainerId,
                BpmnGroup group => group.ContainerId,
                _ => null,
            };
            if (containerId is null) continue;

            // Only the outermost moved shape: content of a moved Sub-Process goes where it goes.
            if (AncestorMoved(document, containerId, movedElements)) continue;

            if (ProcessOfContainer(document, containerId) is not { } process) continue;

            var (Participant, Shape) = pools.FirstOrDefault(pool => pool.Participant.ProcessRef == process);
            if (Shape is null) continue;

            // A Pool dragged as a whole carries its content with it.
            if (asked.ContainsKey(Shape.Id)) continue;

            var centre = (X: bounds.X + (bounds.Width / 2), Y: bounds.Y + (bounds.Height / 2));
            if (Contains(asked.GetValueOrDefault(Shape.Id, Shape.Bounds), centre)) continue;

            var target = pools
                .Where(pool => pool.Participant.Id != Participant.Id && Contains(pool.Shape.Bounds, centre))
                .OrderBy(pool => pool.Shape.Bounds.Width * pool.Shape.Bounds.Height)
                .FirstOrDefault();
            if (target.Shape is null) continue;

            if (target.Participant.ProcessRef is not { } targetProcess)
            {
                rejection = new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-057",
                    $"'{target.Participant.Id}' is a black-box Pool with no Process to own '{shape.BpmnElement}'.",
                    shape.BpmnElement);
                return document;
            }

            var excluded = element is BpmnFlowNode moving ? Subtree(document, moving.Id) : [shape.BpmnElement];
            var owner = DeepestOwnerAt(document, shapes, targetProcess, centre, excluded) ?? targetProcess;

            transfers.Add(new PoolTransfer(shape.BpmnElement, owner, target.Participant.Id));
        }

        if (transfers.Count == 0) return document;

        // A carried note keeps its target's owner even when its offset puts it outside the Pool.
        foreach (var annotationId in carriedAnnotations)
        {
            if (BpmnAnnotationAttachments.UniqueConnection(document, annotationId) is not { } connection) continue;
            var targetId = BpmnAnnotationAttachments.OtherEnd(connection, annotationId);
            var targetTransfer = transfers.FindIndex(transfer => transfer.ElementId == targetId);
            if (targetTransfer >= 0) transfers.Add(transfers[targetTransfer] with { ElementId = annotationId });
        }

        var next = document;
        var transferred = new HashSet<string>(StringComparer.Ordinal);

        foreach (var transfer in transfers)
        {
            next = Transfer(next, transfer, transferred);
        }

        next = ReconcileConnectors(next, transferred, out var converted, out var deleted);

        var message = string.Create(
            CultureInfo.InvariantCulture,
            $"Moved {transfers.Count} element(s) into the Pool they were dropped in");
        if (converted > 0) message += string.Create(CultureInfo.InvariantCulture, $"; converted {converted} connector(s)");
        if (deleted > 0) message += string.Create(CultureInfo.InvariantCulture, $"; deleted {deleted} connector(s) that could not cross Pools");

        diagnostics.Add(new BpmnEditDiagnostic(BpmnEditSeverity.Info, "EDIT-056", message + "."));

        return next;
    }

    private static bool Contains(Bounds box, (double X, double Y) point) =>
        point.X >= box.X && point.X <= box.X + box.Width
        && point.Y >= box.Y && point.Y <= box.Y + box.Height;

    /// <summary>Whether a container, or any container above it, is itself being moved.</summary>
    private static bool AncestorMoved(BpmnDocument document, string containerId, HashSet<string> moved)
    {
        var current = containerId;

        for (var depth = 0; depth < 64; depth++)
        {
            if (moved.Contains(current)) return true;
            if (document.FindFlowNode(current) is not { } parent) return false;

            current = parent.ContainerId;
        }

        return false;
    }

    /// <summary>The Process a container ultimately belongs to, or <see langword="null"/> outside any Pool.</summary>
    private static string? ProcessOfContainer(BpmnDocument document, string containerId) =>
        document.Find(containerId) switch
        {
            BpmnProcess process => process.Id,
            BpmnFlowNode node => RootProcessOf(document, node),
            _ => null,
        };

    /// <summary>The Pool an element is drawn in after the transfer, or <see langword="null"/> when none.</summary>
    private static string? PoolOfElement(BpmnDocument document, string elementId)
    {
        var element = document.Find(elementId);
        if (element is BpmnParticipant self) return self.Id;

        var containerId = element switch
        {
            BpmnFlowNode node => node.ContainerId,
            BpmnDataObjectReference reference => reference.ContainerId,
            BpmnDataStoreReference reference => reference.ContainerId,
            BpmnTextAnnotation annotation => annotation.ContainerId,
            BpmnGroup group => group.ContainerId,
            _ => null,
        };

        if (containerId is null || ProcessOfContainer(document, containerId) is not { } process) return null;

        return document.Collaboration?.Participants.FirstOrDefault(participant => participant.ProcessRef == process)?.Id;
    }

    /// <summary>The innermost expanded Sub-Process of a Process drawn under the point.</summary>
    private static string? DeepestOwnerAt(
        BpmnDocument document,
        FrozenDictionary<string, BpmnShape> shapes,
        string process,
        (double X, double Y) point,
        HashSet<string> excluded) =>
        shapes.Values
            .Where(shape => shape.IsExpanded != false && Contains(shape.Bounds, point))
            .Select(shape => (Shape: shape, Node: document.FindFlowNode(shape.BpmnElement)))
            .Where(entry => entry.Node is { FlowElementsOwned: true }
                && !excluded.Contains(entry.Node.Id)
                && RootProcessOf(document, entry.Node) == process)
            .OrderBy(entry => entry.Shape.Bounds.Width * entry.Shape.Bounds.Height)
            .Select(entry => entry.Node!.Id)
            .FirstOrDefault();

    /// <summary>Re-owns one element, and everything it contains, by the target Pool.</summary>
    private static BpmnDocument Transfer(BpmnDocument document, PoolTransfer transfer, HashSet<string> transferred)
    {
        switch (document.Find(transfer.ElementId))
        {
            case BpmnFlowNode root:
                {
                    var subtree = Subtree(document, root.Id);
                    transferred.UnionWith(subtree);

                    return document with
                    {
                        FlowNodes = [.. document.FlowNodes.Select(node => !subtree.Contains(node.Id)
                        ? node
                        : node.Id == root.Id
                            ? node with { ContainerId = transfer.Owner, PoolId = transfer.PoolId, LaneId = null }
                            : node with { PoolId = transfer.PoolId, LaneId = null })],

                        // Membership is the old Process's; the new Lane is assigned from the drop point.
                        Lanes = [.. document.Lanes.Select(lane => lane.FlowNodeRefs.Any(subtree.Contains)
                        ? lane with { FlowNodeRefs = [.. lane.FlowNodeRefs.Where(id => !subtree.Contains(id))] }
                        : lane)],
                    };
                }

            case BpmnDataObjectReference reference:
                {
                    transferred.Add(reference.Id);
                    return TransferDataObjectReference(document, reference, transfer.Owner);
                }

            case BpmnDataStoreReference reference:
                transferred.Add(reference.Id);
                return document with
                {
                    DataStoreReferences = [.. document.DataStoreReferences.Select(item =>
                        item.Id == reference.Id ? item with { ContainerId = transfer.Owner } : item)],
                };

            case BpmnTextAnnotation annotation:
                transferred.Add(annotation.Id);
                return document with
                {
                    TextAnnotations = [.. document.TextAnnotations.Select(item =>
                        item.Id == annotation.Id ? item with { ContainerId = transfer.Owner } : item)],
                };

            case BpmnGroup group:
                transferred.Add(group.Id);
                return document with
                {
                    Groups = [.. document.Groups.Select(item =>
                        item.Id == group.Id ? item with { ContainerId = transfer.Owner } : item)],
                };

            default:
                return document;
        }
    }

    /// <summary>
    /// Re-owns a Data Object Reference, taking its Data Object along or copying it.
    /// </summary>
    /// <remarks>
    /// A Data Object is scoped to the Process that declares it (section 2.9), so a reference in
    /// another Process cannot point at it. When no other reference uses the definition it moves
    /// with this one; otherwise the others keep it and this reference gets a copy of its own.
    /// </remarks>
    private static BpmnDocument TransferDataObjectReference(
        BpmnDocument document,
        BpmnDataObjectReference reference,
        string owner)
    {
        var definition = reference.DataObjectRef is { } definitionId
            ? document.DataObjects.FirstOrDefault(item => item.Id == definitionId)
            : null;

        if (definition is null)
        {
            return document with
            {
                DataObjectReferences = [.. document.DataObjectReferences.Select(item =>
                    item.Id == reference.Id ? item with { ContainerId = owner } : item)],
            };
        }

        var shared = document.DataObjectReferences
            .Any(item => item.Id != reference.Id && item.DataObjectRef == definition.Id);

        if (!shared)
        {
            return document with
            {
                DataObjects = [.. document.DataObjects.Select(item =>
                    item.Id == definition.Id ? item with { ContainerId = owner } : item)],
                DataObjectReferences = [.. document.DataObjectReferences.Select(item =>
                    item.Id == reference.Id ? item with { ContainerId = owner } : item)],
            };
        }

        var copy = definition with { Id = BpmnId.New("DataObject"), ContainerId = owner };

        return document with
        {
            DataObjects = [.. document.DataObjects, copy],
            DataObjectReferences = [.. document.DataObjectReferences.Select(item =>
                item.Id == reference.Id ? item with { ContainerId = owner, DataObjectRef = copy.Id } : item)],
        };
    }

    /// <summary>
    /// Makes the connectors touching transferred content legal again.
    /// </summary>
    /// <remarks>
    /// A converted connector keeps its id, so its DI edge and waypoints carry over unchanged.
    /// A Sequence Flow that cannot become a Message Flow — a Gateway at either end (MF-003) or an
    /// End Event as its target (MF-006) — is deleted rather than left crossing the boundary.
    /// </remarks>
    private static BpmnDocument ReconcileConnectors(
        BpmnDocument document,
        HashSet<string> transferred,
        out int converted,
        out int deleted)
    {
        converted = 0;
        deleted = 0;

        var removed = new HashSet<string>(StringComparer.Ordinal);
        var sequenceFlows = new List<BpmnSequenceFlow>();
        var messageFlows = new List<BpmnMessageFlow>();

        foreach (var flow in document.SequenceFlows)
        {
            if (!transferred.Contains(flow.SourceRef) && !transferred.Contains(flow.TargetRef))
            {
                sequenceFlows.Add(flow);
                continue;
            }

            var source = document.FindFlowNode(flow.SourceRef);
            var target = document.FindFlowNode(flow.TargetRef);

            if (source is null || target is null)
            {
                sequenceFlows.Add(flow);
                continue;
            }

            if (PoolOfElement(document, source.Id) != PoolOfElement(document, target.Id))
            {
                var cannotCarry = IsGatewayNode(source) || IsGatewayNode(target)
                    || target.Type == BpmnFlowNodeType.EndEvent;

                if (cannotCarry)
                {
                    removed.Add(flow.Id);
                    deleted++;
                    continue;
                }

                messageFlows.Add(new BpmnMessageFlow(flow.Id, flow.SourceRef, flow.TargetRef, flow.Name)
                {
                    Extras = flow.Extras,
                });
                converted++;
                continue;
            }

            if (source.ContainerId != target.ContainerId)
            {
                removed.Add(flow.Id);
                deleted++;
                continue;
            }

            sequenceFlows.Add(flow with { ContainerId = source.ContainerId });
        }

        foreach (var flow in document.MessageFlows)
        {
            if (!transferred.Contains(flow.SourceRef) && !transferred.Contains(flow.TargetRef))
            {
                messageFlows.Add(flow);
                continue;
            }

            var sourcePool = PoolOfElement(document, flow.SourceRef);
            if (sourcePool is null || sourcePool != PoolOfElement(document, flow.TargetRef))
            {
                messageFlows.Add(flow);
                continue;
            }

            if (document.FindFlowNode(flow.SourceRef) is { } source
                && document.FindFlowNode(flow.TargetRef) is { } target
                && source.ContainerId == target.ContainerId)
            {
                sequenceFlows.Add(new BpmnSequenceFlow(flow.Id, flow.SourceRef, flow.TargetRef, source.ContainerId, flow.Name)
                {
                    Extras = flow.Extras,
                });
                converted++;
                continue;
            }

            removed.Add(flow.Id);
            deleted++;
        }

        bool Crosses(IEnumerable<string> ends)
        {
            var list = ends.ToList();
            if (!list.Any(transferred.Contains)) return false;

            return list
                .Select(end => PoolOfElement(document, end))
                .Where(pool => pool is not null)
                .Distinct(StringComparer.Ordinal)
                .Skip(1)
                .Any();
        }

        // A kept association whose ends all moved to another Pool moves with them, or it would be
        // written out under a Process that no longer owns either end.
        string Reowned(string containerId, IEnumerable<string> ends)
        {
            if (!ends.Any(transferred.Contains)) return containerId;
            if (ProcessOfContainer(document, containerId) is not { } current) return containerId;

            var pools = ends
                .Select(end => PoolOfElement(document, end))
                .Where(pool => pool is not null)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (pools.Count != 1) return containerId;

            var process = document.Collaboration?.Participants
                .FirstOrDefault(participant => participant.Id == pools[0])?.ProcessRef;

            return process is null || process == current ? containerId : process;
        }

        var associations = new List<BpmnAssociation>();
        foreach (var association in document.Associations)
        {
            if (Crosses([association.SourceRef, association.TargetRef]))
            {
                removed.Add(association.Id);
                deleted++;
                continue;
            }

            associations.Add(association with { ContainerId = Reowned(association.ContainerId, [association.SourceRef, association.TargetRef]) });
        }

        var dataAssociations = new List<BpmnDataAssociation>();
        foreach (var association in document.DataAssociations)
        {
            var ends = association.SourceRefs.Append(association.TargetRef);
            if (association.OwnerId is { } owner) ends = ends.Append(owner);

            if (Crosses(ends))
            {
                removed.Add(association.Id);
                deleted++;
                continue;
            }

            dataAssociations.Add(association with
            {
                ContainerId = Reowned(association.ContainerId, [.. association.SourceRefs, association.TargetRef]),
            });
        }

        // Converted flows keep their id; only the ones that are gone take their default with them.
        var sequenceIds = sequenceFlows.Select(flow => flow.Id).ToHashSet(StringComparer.Ordinal);

        return document with
        {
            SequenceFlows = sequenceFlows,
            MessageFlows = messageFlows,
            Associations = associations,
            DataAssociations = dataAssociations,
            FlowNodes = [.. document.FlowNodes.Select(node =>
                node.DefaultFlowRef is { } flow && !sequenceIds.Contains(flow) ? node with { DefaultFlowRef = null } : node)],
            Diagrams = [.. document.Diagrams.Select(diagram => diagram with
            {
                Plane = diagram.Plane with
                {
                    Edges = [.. diagram.Plane.Edges.Where(edge => !removed.Contains(edge.BpmnElement))],
                },
            })],
        };
    }

    private static bool IsGatewayNode(BpmnFlowNode node) =>
        BpmnElementInspector.CategoryOf(node.Type) == BpmnElementCategory.Gateway;
}
