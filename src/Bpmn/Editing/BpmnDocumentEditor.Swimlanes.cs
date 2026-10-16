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

    private static BpmnEditResult ApplyCreateLane(BpmnDocument document, CreateLaneCommand command)
    {
        if (Taken(document, command.Id) is { } taken) return BpmnEditResult.Reject(document, taken);

        var isContainer = document.Processes.Any(process => process.Id == command.ContainerId)
            || document.FlowNodes.Any(node => node.Id == command.ContainerId && node.FlowElementsOwned);

        if (!isContainer)
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "CT-001",
                $"'{command.ContainerId}' is not a Process or an embedded Sub-Process in this document.",
                command.Id));
        }

        // CT-004: membership and embedding must agree, so a Lane may only claim nodes that the
        // container it subdivides actually owns.
        var members = command.FlowNodeRefs ?? [];
        var stranger = members.FirstOrDefault(id => document.FindFlowNode(id)?.ContainerId != command.ContainerId);

        if (stranger is not null)
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "CT-004",
                $"'{stranger}' does not belong to '{command.ContainerId}', so this Lane cannot claim it.",
                command.Id));
        }

        /*
         * CT-012: a Lane subdivides a Pool.
         *
         * Every rule about a Lane is written in terms of the Pool it divides — stacked inside it,
         * subdividing its bounds, inheriting its orientation (section 1.5). Without one there is no
         * rectangle to subdivide, no orientation to take and nowhere to put a header band, so the
         * geometry has to be invented. Refused instead: the stencil already says as much, and a
         * command that arrived anyway must not be the way round it.
         */
        var pool = BpmnStencil.PoolOwning(document, command.ContainerId);

        if (pool is null)
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "CT-012",
                $"'{command.ContainerId}' is not held by a Pool, and a Lane subdivides a Pool.",
                command.Id));
        }

        var geometry = CreationGeometry(document, command.DiagramId);
        if (command.DiagramId is not null && ShapeFor(geometry, pool.Id) is null)
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected, "CT-012",
                $"Pool '{pool.Id}' is not drawn on diagram '{command.DiagramId}'.", command.Id));
        }

        var isHorizontal = ShapeFor(geometry, pool.Id)?.IsHorizontal != false;

        var laneSet = document.LaneSets.FirstOrDefault(set => set.ContainerId == command.ContainerId)
            ?? new BpmnLaneSet(command.LaneSetId ?? BpmnId.New("LaneSet"), command.ContainerId, []);

        var existing = BpmnLaneLayout.LanesOf(geometry, pool.Id, isHorizontal);

        var shapeId = command.ShapeId ?? BpmnId.ForDiagramElement(command.Id);
        var placed = Tile(geometry, pool, existing, shapeId, command.Id, isHorizontal);

        var lane = new BpmnLane(command.Id, command.Name, laneSet.Id, members);
        var updatedSet = laneSet with { LaneIds = [.. laneSet.LaneIds, command.Id] };

        var created = document with
        {
            Lanes = [.. document.Lanes, lane],
            LaneSets = document.LaneSets.Any(set => set.Id == laneSet.Id)
                ? [.. document.LaneSets.Select(set => set.Id == laneSet.Id ? updatedSet : set)]
                : [.. document.LaneSets, updatedSet],
            Diagrams = WithLaneGeometry(document, placed, command.DiagramId),
        };

        return BpmnEditResult.Applied(created) with { CreatedIds = [command.Id] };
    }

    /// <summary>
    /// Works out every Lane's bounds once the new one has joined the stack.
    /// </summary>
    /// <remarks>
    /// The stack is the Pool, and <see cref="BpmnLaneLayout.Retile"/> shares it out: the existing
    /// Lanes keep their proportions and give up an equal share to the newcomer, which is what
    /// section 1.5.3 means by the Lanes always tiling their Pool. There is always a Pool — a Lane
    /// without one is refused as CT-012 before this is reached.
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
        if (poolShape is null) return result;

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
        boxes.Aggregate((first, second) => Union(first, second));

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
