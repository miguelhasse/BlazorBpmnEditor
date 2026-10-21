using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// Creating Processes, Pools and Lanes (specification sections 9.1 and 9.3).
/// </summary>
/// <remarks>
/// Split from the Flow Node commands because none of these is a Flow Node, and the differences are
/// the whole content: a Process has no geometry at all, a Pool belongs to a Collaboration rather
/// than to a flow-elements container, and a Lane has no bounds of its own because it is a
/// subdivision of a stack that has to keep adding up (section 1.5.3).
/// </remarks>
public static partial class BpmnDocumentEditor
{
    // --- Process ------------------------------------------------------------------

    private static BpmnEditResult ApplyCreateProcess(BpmnDocument document, CreateProcessCommand command)
    {
        if (Taken(document, command.Id) is { } taken) return BpmnEditResult.Reject(document, taken);

        var created = document with
        {
            Processes = [.. document.Processes, new BpmnProcess(command.Id, command.Name, command.IsExecutable)],
        };

        return BpmnEditResult.Applied(created) with { CreatedIds = [command.Id] };
    }

    // --- Pool ---------------------------------------------------------------------

    private static BpmnEditResult ApplyCreateParticipant(
        BpmnDocument document,
        CreateParticipantCommand command,
        EditingOptions options)
    {
        if (Taken(document, command.Id) is { } taken) return BpmnEditResult.Reject(document, taken);
        if (Invalid(command.Bounds) is { } bad) return BpmnEditResult.Reject(document, bad with { ElementId = command.Id });

        if (document.Collaboration is not { } collaboration)
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "CT-001",
                "A Pool is a Collaboration's child, and this document has no Collaboration to hold one.",
                command.Id));
        }

        if (command.ProcessRef is { } processRef)
        {
            if (document.Find(processRef) is not BpmnProcess)
            {
                return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "CT-001",
                    $"'{processRef}' is not a Process in this document.",
                    command.Id));
            }

            // Two Pools drawing one Process would make containment ambiguous: a node in that
            // Process would belong to both and to neither.
            if (collaboration.Participants.Any(participant => participant.ProcessRef == processRef))
            {
                return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "CT-002",
                    $"'{processRef}' is already drawn by another Pool.",
                    command.Id));
            }
        }

        var shapeId = command.ShapeId ?? BpmnId.ForDiagramElement(command.Id);

        var created = document with
        {
            Collaboration = collaboration with
            {
                Participants =
                [
                    .. collaboration.Participants,
                    new BpmnParticipant(command.Id, command.Name, command.ProcessRef),
                ],
            },
            Diagrams = WithShape(
                document,
                new BpmnShape(shapeId, command.Id, command.Bounds, IsHorizontal: command.IsHorizontal),
                command.DiagramId),
        };

        /*
         * Pools do not overlap. DI-011 governs Events, Activities and Gateways rather than Pools,
         * but two Pools sharing a rectangle is the same defect one level up — every node inside
         * either of them would be geometrically in both.
         */
        if (options.PreventCollisions
            && OverlapsAnotherPool(CreationGeometry(created, command.DiagramId), command.Id, command.Bounds) is { } clash)
        {
            return BpmnEditResult.Reject(document, clash);
        }

        return BpmnEditResult.Applied(created) with { CreatedIds = [command.Id] };
    }

    private static BpmnEditDiagnostic? OverlapsAnotherPool(BpmnDocument document, string poolId, Bounds bounds)
    {
        var others = document.Collaboration?.Participants
            .Where(participant => participant.Id != poolId)
            .Select(participant => participant.Id)
            .ToHashSet(StringComparer.Ordinal) ?? [];

        var clash = document.Diagrams
            .SelectMany(diagram => diagram.Plane.Shapes)
            .FirstOrDefault(shape => others.Contains(shape.BpmnElement) && Overlaps(bounds, shape.Bounds, 0d));

        return clash is null
            ? null
            : new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "DI-011",
                "A Pool cannot overlap another Pool.",
                poolId);
    }

    // --- Lane ---------------------------------------------------------------------

    private static BpmnEditResult ApplyCreateLane(BpmnDocument document, CreateLaneCommand command, EditingOptions options)
    {
        if (Taken(document, command.Id) is { } taken)
            return BpmnEditResult.Reject(document, taken);

        if (command.ContentBounds is { } measured && Invalid(measured) is { } invalid)
            return BpmnEditResult.Reject(document, invalid);

        var original = document;
        var pool = BpmnStencil.PoolOwning(document, command.ContainerId);
        var root = RootProcessOf(document, command.ContainerId);

        if (pool is null && (root is not null || document.Processes.Count == 0 && document.Find(command.ContainerId) is null))
        {
            var wrapped = ApplyCreatePool(document, new CreatePoolCommand($"{command.Id}_Pool", root ?? command.ContainerId,
                new Bounds(0, 0, 600, 250))
            {
                DiagramId = command.DiagramId,
                ContentBounds = command.ContentBounds
            }, options);

            if (!wrapped.IsApplied)
                return wrapped;

            document = wrapped.Document;
            pool = BpmnStencil.PoolOwning(document, command.ContainerId);
        }

        var isContainer = document.Processes.Any(process => process.Id == command.ContainerId)
            || document.FlowNodes.Any(node => node.Id == command.ContainerId && node.FlowElementsOwned);

        if (!isContainer)
        {
            return BpmnEditResult.Reject(original, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "CT-001",
                $"'{command.ContainerId}' is not a Process or an embedded Sub-Process in this document.",
                command.Id));
        }

        // CT-004: membership and embedding must agree, so a Lane may only claim nodes that the
        // container it subdivides actually owns.
        var firstLane = !document.LaneSets.Any(set => set.ContainerId == command.ContainerId && set.LaneIds.Count > 0);
        var members = command.FlowNodeRefs ?? (firstLane
            ? document.FlowNodes.Where(node => node.ContainerId == command.ContainerId).Select(node => node.Id).ToArray()
            : []);
        var stranger = members.FirstOrDefault(id => document.FindFlowNode(id)?.ContainerId != command.ContainerId);

        if (stranger is not null)
        {
            return BpmnEditResult.Reject(original, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "CT-004",
                $"'{stranger}' does not belong to '{command.ContainerId}', so this Lane cannot claim it.",
                command.Id));
        }

        // Pool-backed Lane layout is editor policy; BPMN also permits standalone Process Lanes.
        if (pool is null)
        {
            return BpmnEditResult.Reject(original, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "CT-012",
                $"The editor requires a drawn Pool for Lanes in '{command.ContainerId}'.",
                command.Id));
        }

        var geometry = CreationGeometry(document, command.DiagramId);

        if (command.DiagramId is { } requested
            && CreationDestinationProblem(document, requested, command.ContainerId, command.Id) is { } destination)
            return BpmnEditResult.Reject(original, destination);

        if (command.DiagramId is not null && ShapeFor(geometry, pool.Id) is null)
        {
            return BpmnEditResult.Reject(original, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected, "CT-012",
                $"Pool '{pool.Id}' is not drawn on diagram '{command.DiagramId}'.", command.Id));
        }

        var isHorizontal = ShapeFor(geometry, pool.Id)?.IsHorizontal != false;

        if (firstLane && ShapeFor(geometry, pool.Id) is { } poolShape)
        {
            var diagram = geometry.Diagrams.Count > 0 ? geometry.Diagrams[0] : null;
            var enclosing = AroundProcessContent(document, diagram, RootProcessOf(document, command.ContainerId)!,
                poolShape.Bounds, isHorizontal, true, command.ContentBounds);
            var bounds = Union(poolShape.Bounds, enclosing);
            document = document with
            {
                Diagrams = [.. document.Diagrams.Select(candidate => diagram?.Id == candidate.Id ? candidate with
                {
                    Plane = candidate.Plane with
                    {
                        Shapes = [.. candidate.Plane.Shapes.Select(shape => shape.Id == poolShape.Id ? shape with { Bounds = bounds } : shape)],
                    },
                } : candidate)],
            };
            geometry = CreationGeometry(document, command.DiagramId);

            if (options.PreventCollisions && OverlapsAnotherPool(geometry, pool.Id, bounds) is { } clash)
                return BpmnEditResult.Reject(original, clash);
        }

        var laneSet = document.LaneSets.FirstOrDefault(set => set.ContainerId == command.ContainerId)
            ?? new BpmnLaneSet(command.LaneSetId ?? BpmnId.New("LaneSet"), command.ContainerId, []);

        var existing = BpmnLaneLayout.LanesOf(geometry, pool.Id, isHorizontal);

        var shapeId = command.ShapeId ?? BpmnId.ForDiagramElement(command.Id);
        var allocated = new List<string> { command.Id, shapeId };

        if (!document.LaneSets.Any(set => set.ContainerId == command.ContainerId))
            allocated.Add(laneSet.Id);

        if (CreationIdsProblem(document, allocated) is { } collision)
            return BpmnEditResult.Reject(original, collision);

        var placed = Tile(geometry, pool, existing, shapeId, command.Id, isHorizontal);

        var lane = new BpmnLane(command.Id, command.Name, laneSet.Id, members);
        var updatedSet = laneSet with { LaneIds = [.. laneSet.LaneIds, command.Id] };

        var created = document with
        {
            Lanes = [.. document.Lanes.Select(existingLane => existingLane with
                { FlowNodeRefs = [.. existingLane.FlowNodeRefs.Where(id => !members.Contains(id))] }), lane],
            LaneSets = document.LaneSets.Any(set => set.Id == laneSet.Id)
                ? [.. document.LaneSets.Select(set => set.Id == laneSet.Id ? updatedSet : set)]
                : [.. document.LaneSets, updatedSet],
            Diagrams = WithLaneGeometry(document, placed, command.DiagramId),
            FlowNodes = [.. document.FlowNodes.Select(node => members.Contains(node.Id) ? node with { LaneId = command.Id } : node)],
        };

        return BpmnEditResult.Applied(created) with { CreatedIds = [command.Id] };
    }

    /// <summary>
    /// Works out every Lane's bounds once the new one has joined the stack.
    /// </summary>
    /// <remarks>
    /// The stack is the Pool, and <see cref="BpmnLaneLayout.Retile"/> shares it out: the existing
    /// Lanes keep their proportions and give up an equal share to the newcomer, which is what
    /// section 1.5.3 means by the Lanes always tiling their Pool. Creation establishes a missing
    /// Pool before reaching this editor-specific layout.
    /// </remarks>
    private static List<(string ShapeId, string ElementId, Bounds Bounds, bool IsHorizontal)> Tile(
        BpmnDocument document,
        BpmnParticipant pool,
        IReadOnlyList<BpmnShape> existing,
        string newShapeId,
        string newLaneId,
        bool isHorizontal)
    {
        var result = new List<(string ShapeId, string ElementId, Bounds Bounds, bool IsHorizontal)>(existing.Count + 1);
        var poolShape = ShapeFor(document, pool.Id);

        if (poolShape is null)
            return result;

        // A placeholder for the newcomer, sized as an equal share of the stack, so Retile's
        // proportional split gives it room without disturbing the others' relative sizes.
        var share = existing.Count == 0
            ? poolShape.Bounds
            : Share(poolShape.Bounds, existing.Count + 1, isHorizontal);

        var stack = existing.Select(shape => shape.Bounds).Append(share).ToList();
        var tiled = BpmnLaneLayout.Retile(poolShape.Bounds, stack, isHorizontal);

        for (var index = 0; index < existing.Count; index++)
        {
            result.Add((existing[index].Id, existing[index].BpmnElement, tiled[index], isHorizontal));
        }

        result.Add((newShapeId, newLaneId, tiled[^1], isHorizontal));

        return result;
    }

    private static Bounds Share(Bounds pool, int count, bool isHorizontal) => isHorizontal
        ? pool with { Height = pool.Height / count }
        : pool with { Width = (pool.Width - BpmnLaneLayout.HeaderSize) / count };

    private static Bounds Union(IEnumerable<Bounds> boxes) =>
        boxes.Aggregate(Union);

    private static BpmnShape? ShapeFor(BpmnDocument document, string elementId) => document.Diagrams
        .SelectMany(diagram => diagram.Plane.Shapes)
        .FirstOrDefault(shape => shape.BpmnElement == elementId);

    /// <summary>Replaces the moved Lane shapes and adds the new one to the active plane.</summary>
    private static IReadOnlyList<BpmnDiagram> WithLaneGeometry(
        BpmnDocument document,
        IReadOnlyList<(string ShapeId, string ElementId, Bounds Bounds, bool IsHorizontal)> placed,
        string? diagramId = null)
    {
        var moved = placed.ToDictionary(item => item.ShapeId, item => item.Bounds, StringComparer.Ordinal);
        var existingIds = document.Diagrams
            .SelectMany(diagram => diagram.Plane.Shapes)
            .Select(shape => shape.Id)
            .ToHashSet(StringComparer.Ordinal);

        var repositioned = document with
        {
            Diagrams = [.. document.Diagrams.Select(diagram => diagramId is not null && diagram.Id != diagramId ? diagram : diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [.. diagram.Plane.Shapes.Select(shape => moved.TryGetValue(shape.Id, out var bounds)
                        ? shape with { Bounds = bounds }
                        : shape)],
                },
            })],
        };

        var fresh = placed.Where(item => !existingIds.Contains(item.ShapeId)).ToList();

        return fresh.Count == 0
            ? repositioned.Diagrams
            : WithShape(
                repositioned,
                new BpmnShape(fresh[0].ShapeId, fresh[0].ElementId, fresh[0].Bounds, IsHorizontal: fresh[0].IsHorizontal),
                diagramId);
    }
}
