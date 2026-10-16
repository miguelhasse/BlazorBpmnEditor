using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// Where Lanes sit inside their Pool.
/// </summary>
/// <remarks>
/// <para>
/// Section 1.5.3 makes Lane geometry derived, not independent: in a horizontal Pool the Lanes are
/// stacked top to bottom, each spanning the Pool's width minus its header band, and their heights
/// subdivide the Pool's height. A vertical Pool is the same statement with the axes exchanged. So
/// a Pool's bounds and its Lanes' bounds are one fact expressed twice, and any edit that changes
/// one has to recompute the other or the file stops describing a diagram anyone else can draw.
/// </para>
/// <para>
/// The header band is deliberately not subtracted twice. A Lane sits <em>inside</em> the Pool's
/// content area and carries its own thin name strip, which the renderer draws within the Lane's
/// own bounds — so the Lane's origin is past the Pool's header and no further.
/// </para>
/// </remarks>
public static class BpmnLaneLayout
{
    /// <summary>The thickness of a Pool or Lane name band. Matches the canvas constant.</summary>
    public const double HeaderSize = 30;

    /// <summary>The smallest a Lane may be along the axis it subdivides.</summary>
    public const double MinimumLaneExtent = 40;

    /// <summary>
    /// Re-tiles a Pool's Lanes to fill its new bounds.
    /// </summary>
    /// <remarks>
    /// Proportional rather than equal: a Pool whose Lanes were deliberately uneven should stay
    /// uneven when it is made taller. Rounding is absorbed by the last Lane so the stack always
    /// adds up to exactly the Pool, which is what stops a one-pixel gap appearing after a few
    /// resizes.
    /// </remarks>
    /// <param name="pool">The Pool's new bounds.</param>
    /// <param name="lanes">The Lane bounds in stacking order.</param>
    /// <param name="isHorizontal">The Pool's orientation, which every Lane shares (DI-006).</param>
    /// <returns>The Lane bounds, in the order given.</returns>
    public static IReadOnlyList<Bounds> Retile(
        Bounds pool,
        IReadOnlyList<Bounds> lanes,
        bool isHorizontal)
    {
        ArgumentNullException.ThrowIfNull(lanes);

        if (lanes.Count == 0) return [];

        var available = isHorizontal ? pool.Height : pool.Width - HeaderSize;
        var previous = lanes.Sum(lane => isHorizontal ? lane.Height : lane.Width);

        // A degenerate stack — every Lane zero-sized, or a document that never had geometry — is
        // shared out evenly, because there are no proportions to preserve.
        var shares = previous > 0
            ? lanes.Select(lane => (isHorizontal ? lane.Height : lane.Width) / previous).ToList()
            : Enumerable.Repeat(1d / lanes.Count, lanes.Count).ToList();

        var result = new List<Bounds>(lanes.Count);
        var offset = 0d;

        for (var index = 0; index < lanes.Count; index++)
        {
            var isLast = index == lanes.Count - 1;
            var extent = isLast ? available - offset : Math.Round(available * shares[index], 2);

            result.Add(isHorizontal
                ? new Bounds(pool.X + HeaderSize, pool.Y + offset, pool.Width - HeaderSize, extent)
                : new Bounds(pool.X + offset, pool.Y + HeaderSize, extent, pool.Height - HeaderSize));

            offset += extent;
        }

        return result;
    }

    /// <summary>
    /// Moves the splitter between two adjacent Lanes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A splitter is the only way Lanes are sized against one another. The Lanes always tile their
    /// whole Pool, so the shared edge moves and nothing else does: the Lane before it grows by
    /// exactly what the Lane after it loses, and the Pool's total is held constant.
    /// </para>
    /// <para>
    /// Returns <see langword="null"/> when there is no such boundary or either Lane would fall
    /// below <see cref="MinimumLaneExtent"/>. The caller then refuses the gesture rather than
    /// producing a stack that no longer sums to its Pool.
    /// </para>
    /// </remarks>
    /// <param name="lanes">The Lane bounds in stacking order.</param>
    /// <param name="boundary">Index of the Lane before the splitter; the splitter sits between it and the next.</param>
    /// <param name="position">The splitter's new coordinate along the stacking axis (Y for horizontal, X for vertical).</param>
    /// <param name="isHorizontal">The Pool's orientation.</param>
    /// <returns>Every Lane's new bounds, or <see langword="null"/> if it does not fit.</returns>
    public static IReadOnlyList<Bounds>? MoveSplitter(
        IReadOnlyList<Bounds> lanes,
        int boundary,
        double position,
        bool isHorizontal)
    {
        ArgumentNullException.ThrowIfNull(lanes);

        if (boundary < 0 || boundary + 1 >= lanes.Count) return null;

        var origin = isHorizontal ? lanes[boundary].Y : lanes[boundary].X;
        var end = isHorizontal
            ? lanes[boundary + 1].Y + lanes[boundary + 1].Height
            : lanes[boundary + 1].X + lanes[boundary + 1].Width;

        var before = position - origin;
        var after = end - position;

        if (before < MinimumLaneExtent || after < MinimumLaneExtent) return null;

        var extents = lanes
            .Select(lane => isHorizontal ? lane.Height : lane.Width)
            .ToList();

        extents[boundary] = before;
        extents[boundary + 1] = after;

        return Stack(lanes[0], extents, isHorizontal);
    }

    /// <summary>Lays a list of extents out end to end from the first Lane's origin.</summary>
    private static List<Bounds> Stack(Bounds first, List<double> extents, bool isHorizontal)
    {
        var result = new List<Bounds>(extents.Count);
        var offset = 0d;

        foreach (var extent in extents)
        {
            result.Add(isHorizontal
                ? new Bounds(first.X, first.Y + offset, first.Width, extent)
                : new Bounds(first.X + offset, first.Y, extent, first.Height));

            offset += extent;
        }

        return result;
    }

    /// <summary>
    /// The Lanes of a Pool, in stacking order.
    /// </summary>
    /// <remarks>
    /// Ordered by position rather than by declaration, because tiling is a geometric statement and
    /// a file may list Lanes in any order. A Lane with no shape is left out: it has no geometry to
    /// tile with.
    /// </remarks>
    /// <param name="document">The document being edited.</param>
    /// <param name="participantId">The Pool whose Lanes to find.</param>
    /// <param name="isHorizontal">The Pool's orientation.</param>
    /// <returns>The Lane shapes, ordered along the axis they subdivide.</returns>
    public static IReadOnlyList<BpmnShape> LanesOf(
        BpmnDocument document,
        string participantId,
        bool isHorizontal)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Find(participantId) is not BpmnParticipant { ProcessRef: { } processId }) return [];

        var laneIds = document.LaneSets
            .Where(set => set.ContainerId == processId)
            .SelectMany(set => set.LaneIds)
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. document.Diagrams
                .SelectMany(diagram => diagram.Plane.Shapes)
                .Where(shape => laneIds.Contains(shape.BpmnElement))
                .OrderBy(shape => isHorizontal ? shape.Bounds.Y : shape.Bounds.X),
        ];
    }

    /// <summary>The Pool a Lane belongs to, or <see langword="null"/> when it has none.</summary>
    /// <param name="document">The document being edited.</param>
    /// <param name="laneId">The Lane to look up.</param>
    /// <returns>The owning Participant, if there is one.</returns>
    public static BpmnParticipant? PoolOfLane(BpmnDocument document, string laneId)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Find(laneId) is not BpmnLane lane) return null;

        var containerId = document.LaneSets.FirstOrDefault(set => set.Id == lane.LaneSetId)?.ContainerId;
        if (containerId is null) return null;

        return document.Collaboration?.Participants.FirstOrDefault(participant => participant.ProcessRef == containerId);
    }
}
