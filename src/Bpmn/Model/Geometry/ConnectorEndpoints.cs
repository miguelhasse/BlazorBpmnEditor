using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Model.Geometry;

/// <summary>
/// Keeps every connector's ends on the outline of the shapes they connect (specification section 4.6).
/// </summary>
/// <remarks>
/// <para>
/// A connector end that is not on the boundary of its shape is never what anyone meant: it is a
/// file written by a tool with a different idea of the outline, a shape that was resized under it,
/// or a route someone typed. So the document is made to say what the canvas draws. This is the one
/// place geometry is corrected on import, and the spec permits it because it is reported
/// (section 10.6: every normalized item); section 4.6 is the outline rule. The interior of a route is
/// still left exactly as it was, apart from the one bend that has to move with an end to keep the
/// segment beside it straight.
/// </para>
/// <para>
/// Projection only, never magnet snapping: the result has to be stable, and a second pass over its
/// own output must change nothing.
/// </para>
/// </remarks>
internal static class ConnectorEndpoints
{
    /// <summary>Points this close together are the same point, in diagram units.</summary>
    private const double Epsilon = 0.5;

    /// <summary>How far from a box an end may be and still count as attached to it.</summary>
    private const double Attached = 2;

    /// <summary>Puts the ends of every connector on the outline of its source and target.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The document, or the same instance when every end already was.</returns>
    public static BpmnDocument Normalize(BpmnDocument document) => Normalize(document, null);

    /// <summary>
    /// Puts the ends of every connector on the outline of its source and target, and says which
    /// connectors had to move.
    /// </summary>
    /// <remarks>
    /// Specification section 10.6 asks for every normalised item to be reported, so a caller that
    /// changes a document the user did not edit (an import) can name each connector it touched.
    /// </remarks>
    /// <param name="document">The document.</param>
    /// <param name="moved">Receives one entry per connector whose ends moved, or <see langword="null"/> to not ask.</param>
    /// <returns>The document, or the same instance when every end already was.</returns>
    public static BpmnDocument Normalize(BpmnDocument document, ICollection<MovedConnector>? moved)
    {
        ArgumentNullException.ThrowIfNull(document);

        var changed = false;
        var diagrams = new List<BpmnDiagram>(document.Diagrams.Count);

        foreach (var diagram in document.Diagrams)
        {
            var shapes = diagram.Plane.Shapes
                .GroupBy(shape => shape.BpmnElement, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            var edges = new List<BpmnEdge>(diagram.Plane.Edges.Count);
            var planeChanged = false;

            foreach (var edge in diagram.Plane.Edges)
            {
                var normalised = NormalizeEdge(document, shapes, edge, moved);
                planeChanged |= !ReferenceEquals(normalised, edge);
                edges.Add(normalised);
            }

            if (!planeChanged)
            {
                diagrams.Add(diagram);
                continue;
            }

            changed = true;
            diagrams.Add(diagram with { Plane = diagram.Plane with { Edges = edges } });
        }

        return changed ? document with { Diagrams = diagrams } : document;
    }

    /// <summary>
    /// Carries the ends of connectors along with shapes that changed size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A connector attached to the middle of a side stays in the middle of that side when the shape
    /// is resized, and one attached a third of the way along stays a third of the way along: the
    /// end keeps its position relative to the shape's box. Without this, normalisation alone would
    /// re-project the end onto whatever outline point is now nearest, which for a side that moved
    /// away can be a different side altogether.
    /// </para>
    /// <para>
    /// Only shapes whose <em>size</em> changed are followed. A move already carries its routes, so
    /// following it as well would move the ends twice. The result is not yet on the outline (a box
    /// position is not an outline position for a circle, say), so <see cref="Normalize(BpmnDocument)"/> runs after.
    /// </para>
    /// </remarks>
    /// <param name="before">The document before the edit.</param>
    /// <param name="after">The document after it.</param>
    /// <returns>The document after, with attached ends moved, or the same instance if none were.</returns>
    public static BpmnDocument FollowResizedShapes(BpmnDocument before, BpmnDocument after) =>
        FollowResizedShapes(before, after, null);

    /// <summary>
    /// Carries the ends of connectors along with shapes that changed size, leaving alone connectors
    /// whose routes were already worked out against the new size.
    /// </summary>
    /// <param name="before">The document before the edit.</param>
    /// <param name="after">The document after it.</param>
    /// <param name="alreadyRouted">Connectors whose route came with the edit, or <see langword="null"/>.</param>
    /// <returns>The document after, with attached ends moved, or the same instance if none were.</returns>
    public static BpmnDocument FollowResizedShapes(
        BpmnDocument before,
        BpmnDocument after,
        ISet<string>? alreadyRouted)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var oldBounds = before.Diagrams
            .SelectMany(diagram => diagram.Plane.Shapes)
            .GroupBy(shape => shape.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Bounds, StringComparer.Ordinal);

        var changed = false;
        var diagrams = new List<BpmnDiagram>(after.Diagrams.Count);

        foreach (var diagram in after.Diagrams)
        {
            var resized = new Dictionary<string, (Bounds Old, Bounds New)>(StringComparer.Ordinal);

            foreach (var shape in diagram.Plane.Shapes)
            {
                if (oldBounds.TryGetValue(shape.Id, out var old) && SizeChanged(old, shape.Bounds))
                {
                    resized[shape.BpmnElement] = (old, shape.Bounds);
                }
            }

            if (resized.Count == 0)
            {
                diagrams.Add(diagram);
                continue;
            }

            var edges = new List<BpmnEdge>(diagram.Plane.Edges.Count);
            var planeChanged = false;

            foreach (var edge in diagram.Plane.Edges)
            {
                var followed = alreadyRouted?.Contains(edge.BpmnElement) == true
                    ? edge
                    : FollowEdge(after, resized, edge);
                planeChanged |= !ReferenceEquals(followed, edge);
                edges.Add(followed);
            }

            changed |= planeChanged;
            diagrams.Add(planeChanged ? diagram with { Plane = diagram.Plane with { Edges = edges } } : diagram);
        }

        return changed ? after with { Diagrams = diagrams } : after;
    }

    private static BpmnEdge FollowEdge(
        BpmnDocument document,
        Dictionary<string, (Bounds Old, Bounds New)> resized,
        BpmnEdge edge)
    {
        if (edge.Waypoints.Count < 2) return edge;
        if (Ends(document.Find(edge.BpmnElement)) is not var (sourceRef, targetRef, orthogonal)) return edge;

        var route = edge.Waypoints.ToList();
        var newFirst = Followed(route[0], sourceRef, resized);
        var newLast = Followed(route[^1], targetRef, resized);

        if (Near(route[0], newFirst) && Near(route[^1], newLast)) return edge;

        if (orthogonal) Square(route, newFirst, newLast);

        route[0] = newFirst;
        route[^1] = newLast;

        return edge with { Waypoints = route };
    }

    /// <summary>The end's place in the resized box, when it was attached to the box it had.</summary>
    private static DiPoint Followed(
        DiPoint end,
        string? elementId,
        Dictionary<string, (Bounds Old, Bounds New)> resized)
    {
        if (elementId is null || !resized.TryGetValue(elementId, out var box)) return end;

        // An end that was not on (or beside) the old box was not attached to it; leave it to
        // normalisation rather than inventing a relationship.
        if (!box.Old.IsPositive || !box.New.IsPositive || BpmnOutline.DistanceToBounds(end, box.Old) > Attached) return end;

        return new DiPoint(
            box.New.X + ((end.X - box.Old.X) / box.Old.Width * box.New.Width),
            box.New.Y + ((end.Y - box.Old.Y) / box.Old.Height * box.New.Height));
    }

    private static bool SizeChanged(Bounds old, Bounds current) =>
        Math.Abs(old.Width - current.Width) > 0.01 || Math.Abs(old.Height - current.Height) > 0.01;

    private static BpmnEdge NormalizeEdge(
        BpmnDocument document,
        Dictionary<string, BpmnShape> shapes,
        BpmnEdge edge,
        ICollection<MovedConnector>? moved)
    {
        if (edge.Waypoints.Count < 2) return edge;
        if (Ends(document.Find(edge.BpmnElement)) is not var (sourceRef, targetRef, orthogonal)) return edge;
        if (sourceRef is null || targetRef is null) return edge;
        if (!shapes.TryGetValue(sourceRef, out var sourceShape) || !shapes.TryGetValue(targetRef, out var targetShape)) return edge;

        var source = OutlineOf(document, sourceShape);
        var target = OutlineOf(document, targetShape);

        var route = edge.Waypoints.ToList();
        var first = route[0];
        var last = route[^1];

        var newFirst = BpmnOutline.Normalize(first, source, BpmnOutline.Centre(target.Bounds));
        var newLast = BpmnOutline.Normalize(last, target, BpmnOutline.Centre(source.Bounds));

        var endsMoved = !(Near(first, newFirst) && Near(last, newLast));

        if (endsMoved)
        {
            if (orthogonal) Square(route, newFirst, newLast);

            route[0] = newFirst;
            route[^1] = newLast;
        }

        // Sequence and Message Flows are always drawn with right angles. A route with any other
        // segment cannot be repaired by moving one bend, so it is replaced by the simplest
        // orthogonal route between the same ends. The canvas's router supplies better ones while a
        // shape is dragged; this only guarantees the document never holds a diagonal flow.
        var reshaped = orthogonal && !AllAxial(route);

        if (reshaped)
        {
            route = OrthogonalRoute(route[0], route[^1], source.Bounds, target.Bounds);
        }
        else if (orthogonal && endsMoved)
        {
            DropRepeats(route);
        }

        if (!endsMoved && !reshaped) return edge;

        moved?.Add(new MovedConnector(
            edge.BpmnElement,
            Math.Max(Distance(first, newFirst), Distance(last, newLast)),
            reshaped));

        return edge with { Waypoints = route };
    }

    private static bool AllAxial(List<DiPoint> route)
    {
        for (var index = 1; index < route.Count; index++)
        {
            if (!Axial(route[index - 1], route[index])) return false;
        }

        return true;
    }

    /// <summary>
    /// The simplest right-angled route between two points on the outlines of two shapes.
    /// </summary>
    /// <remarks>
    /// Each end leaves along the axis of the side it is on. Two ends leaving along the same axis
    /// are joined by a Z through the middle of the gap; along different axes, by one elbow. Not
    /// obstacle-aware, and not meant to be: it exists so the invariant holds, deterministically and
    /// identically on every pass.
    /// </remarks>
    private static List<DiPoint> OrthogonalRoute(DiPoint from, DiPoint to, Bounds source, Bounds target)
    {
        if (Axial(from, to)) return [from, to];

        var leavesHorizontally = OnVerticalSide(from, source);
        var entersHorizontally = OnVerticalSide(to, target);

        if (leavesHorizontally && entersHorizontally)
        {
            var middle = (from.X + to.X) / 2;
            return [from, new DiPoint(middle, from.Y), new DiPoint(middle, to.Y), to];
        }

        if (!leavesHorizontally && !entersHorizontally)
        {
            var middle = (from.Y + to.Y) / 2;
            return [from, new DiPoint(from.X, middle), new DiPoint(to.X, middle), to];
        }

        return leavesHorizontally
            ? [from, new DiPoint(to.X, from.Y), to]
            : [from, new DiPoint(from.X, to.Y), to];
    }

    /// <summary>Whether a point on an outline is on its left or right side rather than its top or bottom.</summary>
    private static bool OnVerticalSide(DiPoint point, Bounds box)
    {
        var centre = BpmnOutline.Centre(box);
        var horizontal = Math.Abs(point.X - centre.X) / Math.Max(box.Width / 2, 1e-9);
        var vertical = Math.Abs(point.Y - centre.Y) / Math.Max(box.Height / 2, 1e-9);

        return horizontal >= vertical;
    }

    /// <summary>Removes consecutive points that are the same point, keeping at least the two ends.</summary>
    private static void DropRepeats(List<DiPoint> route)
    {
        for (var index = route.Count - 1; index > 0 && route.Count > 2; index--)
        {
            if (Near(route[index], route[index - 1])) route.RemoveAt(index);
        }
    }

    private static double Distance(DiPoint first, DiPoint second) =>
        Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));

    /// <summary>
    /// Moves the bends beside the ends so segments that were straight stay straight.
    /// </summary>
    /// <remarks>
    /// The same rule as <c>alignToEndpoints</c> on the canvas, applied only to a segment that was
    /// axial to begin with: a route that was diagonal on purpose is left diagonal.
    /// </remarks>
    private static void Square(List<DiPoint> route, DiPoint newFirst, DiPoint newLast)
    {
        var count = route.Count;
        if (count < 3) return;

        var firstWasAxial = Axial(route[0], route[1]);
        var lastWasAxial = Axial(route[count - 1], route[count - 2]);

        if (count == 3)
        {
            // One bend serves both ends: it takes one coordinate from each.
            if (!firstWasAxial || !lastWasAxial) return;

            route[1] = Math.Abs(route[0].Y - route[1].Y) < Epsilon
                ? new DiPoint(newLast.X, newFirst.Y)
                : new DiPoint(newFirst.X, newLast.Y);

            return;
        }

        if (firstWasAxial) route[1] = OnAxisOf(newFirst, route[1], route[2]);
        if (lastWasAxial) route[count - 2] = OnAxisOf(newLast, route[count - 2], route[count - 3]);
    }

    /// <summary>Moves a bend onto the end's axis, keeping the axis it shares with its neighbour.</summary>
    private static DiPoint OnAxisOf(DiPoint end, DiPoint bend, DiPoint neighbour) =>
        Math.Abs(neighbour.X - bend.X) < Epsilon
            ? new DiPoint(bend.X, end.Y)
            : new DiPoint(end.X, bend.Y);

    private static bool Axial(DiPoint from, DiPoint to) =>
        Math.Abs(from.X - to.X) < Epsilon || Math.Abs(from.Y - to.Y) < Epsilon;

    private static bool Near(DiPoint first, DiPoint second) =>
        Math.Abs(first.X - second.X) < Epsilon && Math.Abs(first.Y - second.Y) < Epsilon;

    private static Outline OutlineOf(BpmnDocument document, BpmnShape shape) =>
        document.Find(shape.BpmnElement) is BpmnFlowNode node
            ? BpmnOutline.OfFlowNode(node.Type, shape.Bounds)
            : BpmnOutline.Rectangle(shape.Bounds);

    /// <summary>The two ends a connector joins, and whether its routes are orthogonal.</summary>
    private static (string? Source, string? Target, bool Orthogonal)? Ends(object? connector) => connector switch
    {
        BpmnSequenceFlow flow => (flow.SourceRef, flow.TargetRef, true),
        BpmnMessageFlow flow => (flow.SourceRef, flow.TargetRef, true),
        BpmnAssociation association => (association.SourceRef, association.TargetRef, false),
        BpmnDataAssociation association => (association.SourceRefs.Count > 0 ? association.SourceRefs[0] : null, association.TargetRef, false),
        _ => null,
    };
}

/// <summary>A connector whose ends had to be moved onto their shapes' outlines.</summary>
/// <param name="ConnectorId">The connector's id.</param>
/// <param name="Distance">The farthest either end moved, in diagram units.</param>
/// <param name="Reshaped">Whether the route also had a diagonal segment and was replaced by a right-angled one.</param>
internal readonly record struct MovedConnector(string ConnectorId, double Distance, bool Reshaped = false);
