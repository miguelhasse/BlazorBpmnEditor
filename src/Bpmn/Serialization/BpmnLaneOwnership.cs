using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>
/// Gives an imported Lane the Pool it subdivides, when the file did not.
/// </summary>
/// <remarks>
/// <para>
/// Section 1.5 writes every rule about a Lane in terms of the Pool it divides: it is stacked inside
/// the Pool, its extent subdivides the Pool's, its orientation must match the Pool's. A Lane on a
/// Process no Participant references has none of those — no rectangle it is a share of, no
/// orientation to inherit, nowhere for a header band. The editor refuses to create one, so a
/// document that already contains one would otherwise be permanently invalid and uneditable.
/// </para>
/// <para>
/// This is one of two places the reader changes what a file said (the other moves connector ends onto
/// their shapes' outlines, see <c>ConnectorEndpoints</c>), and it is a deliberate exception to
/// sections 10.6 and 6.1: what it adds is a Participant, a Collaboration to hold it and one
/// <c>BPMNShape</c>, all derived from the lanes already there. Every Lane, every FlowNode and every
/// waypoint is left exactly as read. It reports itself, because a silent structural rewrite is the
/// thing those sections exist to prevent.
/// </para>
/// </remarks>
internal static class BpmnLaneOwnership
{
    /// <summary>The band a Pool header takes beside its lanes.</summary>
    private const double HeaderSize = 30d;

    /// <summary>
    /// Wraps every lane set whose Process no Participant references.
    /// </summary>
    /// <param name="document">The document as read.</param>
    /// <param name="report">Called once per Process wrapped, so the host is told.</param>
    /// <returns>The document, unchanged when every lane set already had a Pool.</returns>
    public static BpmnDocument WithPoolsForOrphanLanes(BpmnDocument document, Action<string, string> report)
    {
        var referenced = (document.Collaboration?.Participants ?? [])
            .Select(participant => participant.ProcessRef)
            .Where(process => process is not null)
            .ToHashSet(StringComparer.Ordinal);

        var orphans = document.LaneSets
            .Select(set => set.ContainerId)
            .Distinct(StringComparer.Ordinal)
            // Only a root Process. A lane set on an embedded Sub-Process is owned by whatever owns
            // the Sub-Process, which the Pool below covers once it exists.
            .Where(container => document.Processes.Any(process => process.Id == container))
            .Where(container => !referenced.Contains(container))
            .ToList();

        if (orphans.Count == 0) return document;

        var participants = new List<BpmnParticipant>(document.Collaboration?.Participants ?? []);
        // Each new Pool shape with the lanes it wraps, because the lookup that would find them
        // again runs against the document as it was — before this Pool existed in it.
        var added = new List<(BpmnShape Shape, IReadOnlyList<string> LaneIds)>();

        foreach (var container in orphans)
        {
            var pool = new BpmnParticipant(
                BpmnId.New("Participant"),
                document.Processes.FirstOrDefault(process => process.Id == container)?.Name,
                container);

            participants.Add(pool);
            added.Add((
                new BpmnShape(BpmnId.ForDiagramElement(pool.Id), pool.Id, Around(document, container), IsHorizontal: true),
                LaneIdsOf(document, container)));

            report(pool.Id, container);
        }

        var collaboration = document.Collaboration is { } existing
            ? existing with { Participants = participants }
            : new BpmnCollaboration(BpmnId.New("Collaboration"), null, participants);

        return document with
        {
            Collaboration = collaboration,
            Diagrams = [.. document.Diagrams.Select(diagram => WithShapes(diagram, added))],
        };
    }

    /// <summary>
    /// The box the new Pool takes: the lanes it now owns, plus room for its header band.
    /// </summary>
    /// <remarks>
    /// Derived from the lanes rather than invented, so the Pool lands exactly around the stack the
    /// file drew and nothing appears to move. The header goes on the left of a horizontal Pool,
    /// which is where the lanes' own bands already are.
    /// </remarks>
    private static Bounds Around(BpmnDocument document, string container)
    {
        var laneIds = document.LaneSets
            .Where(set => set.ContainerId == container)
            .SelectMany(set => set.LaneIds)
            .ToHashSet(StringComparer.Ordinal);

        var boxes = document.Diagrams
            .SelectMany(diagram => diagram.Plane.Shapes)
            .Where(shape => laneIds.Contains(shape.BpmnElement))
            .Select(shape => shape.Bounds)
            .ToList();

        if (boxes.Count == 0) return new Bounds(160, 80, 600, 250);

        var left = boxes.Min(box => box.X);
        var top = boxes.Min(box => box.Y);
        var right = boxes.Max(box => box.X + box.Width);
        var bottom = boxes.Max(box => box.Y + box.Height);

        return new Bounds(left - HeaderSize, top, (right - left) + HeaderSize, bottom - top);
    }

    /// <summary>Adds each Pool shape to the plane that draws the lanes it wraps.</summary>
    private static BpmnDiagram WithShapes(
        BpmnDiagram diagram,
        IReadOnlyList<(BpmnShape Shape, IReadOnlyList<string> LaneIds)> added)
    {
        var drawn = diagram.Plane.Shapes.Select(shape => shape.BpmnElement).ToHashSet(StringComparer.Ordinal);

        var wanted = added
            .Where(entry => entry.LaneIds.Any(drawn.Contains))
            .Select(entry => entry.Shape)
            .ToList();

        if (wanted.Count == 0) return diagram;

        // First, so a Pool is behind the lanes it encloses rather than over them.
        return diagram with { Plane = diagram.Plane with { Shapes = [.. wanted, .. diagram.Plane.Shapes] } };
    }

    private static IReadOnlyList<string> LaneIdsOf(BpmnDocument document, string container) =>
    [
        .. document.LaneSets
            .Where(set => set.ContainerId == container)
            .SelectMany(set => set.LaneIds),
    ];
}
