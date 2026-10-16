using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Model.Geometry;

/// <summary>The shape of the boundary a connector attaches to.</summary>
internal enum OutlineKind
{
    /// <summary>A plain rectangle: Pools, Lanes, Data and Artifacts.</summary>
    Rect,

    /// <summary>A rectangle with rounded corners: Activities.</summary>
    RoundedRect,

    /// <summary>An ellipse: Events.</summary>
    Ellipse,

    /// <summary>A diamond: Gateways.</summary>
    Diamond,
}

/// <summary>A shape's outline: its kind and the box it is drawn in.</summary>
/// <param name="Kind">Which boundary it is.</param>
/// <param name="Bounds">The box it fills.</param>
internal readonly record struct Outline(OutlineKind Kind, Bounds Bounds);

/// <summary>
/// Shape outlines and nearest-point projection (specification section 4.6).
/// </summary>
/// <remarks>
/// <para>
/// The same geometry as <c>geometry/outline.ts</c>, and held to it by one table of cases
/// (<c>test/fixtures/outline-cases.json</c>) that both test suites run. A connector end is always on
/// the boundary of the shape it belongs to; the canvas keeps it there while it is dragged, and this
/// is what keeps it there in the document, so a file that was written with ends off the boundary
/// comes out with them on it.
/// </para>
/// <para>
/// Ties are broken in the same order on both sides (left, right, top, bottom; polygon edges in
/// vertex order), which is why the loops here are written the way they are.
/// </para>
/// </remarks>
internal static class BpmnOutline
{
    /// <summary>The corner radius of an Activity. Matches <c>TASK_CORNER_RADIUS</c>.</summary>
    public const double TaskCornerRadius = 10;

    /// <summary>The outline of a Flow Node.</summary>
    /// <param name="type">Its type.</param>
    /// <param name="bounds">Its box.</param>
    /// <returns>The outline it is drawn with.</returns>
    public static Outline OfFlowNode(BpmnFlowNodeType type, Bounds bounds) => type switch
    {
        BpmnFlowNodeType.StartEvent
            or BpmnFlowNodeType.EndEvent
            or BpmnFlowNodeType.IntermediateCatchEvent
            or BpmnFlowNodeType.IntermediateThrowEvent
            or BpmnFlowNodeType.BoundaryEvent => new Outline(OutlineKind.Ellipse, bounds),

        BpmnFlowNodeType.ExclusiveGateway
            or BpmnFlowNodeType.InclusiveGateway
            or BpmnFlowNodeType.ParallelGateway
            or BpmnFlowNodeType.ComplexGateway
            or BpmnFlowNodeType.EventBasedGateway => new Outline(OutlineKind.Diamond, bounds),

        _ => new Outline(OutlineKind.RoundedRect, bounds),
    };

    /// <summary>The outline of anything drawn as a rectangle.</summary>
    /// <param name="bounds">Its box.</param>
    /// <returns>A rectangular outline.</returns>
    public static Outline Rectangle(Bounds bounds) => new(OutlineKind.Rect, bounds);

    /// <summary>The centre of a box.</summary>
    /// <param name="bounds">The box.</param>
    /// <returns>Its centre.</returns>
    public static DiPoint Centre(Bounds bounds) => new(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));

    /// <summary>The nearest point of an outline to a point.</summary>
    /// <param name="point">The point.</param>
    /// <param name="outline">The outline.</param>
    /// <returns>A point on the boundary.</returns>
    public static DiPoint Project(DiPoint point, Outline outline) => outline.Kind switch
    {
        OutlineKind.RoundedRect => ProjectToRoundedRect(point, outline.Bounds, TaskCornerRadius),
        OutlineKind.Ellipse => ProjectToEllipse(point, outline.Bounds),
        OutlineKind.Diamond => ProjectToPolygon(point, DiamondVertices(outline.Bounds)),
        _ => ProjectToRect(point, outline.Bounds),
    };

    /// <summary>Where a ray from the shape's centre towards a target leaves the outline.</summary>
    /// <param name="outline">The outline.</param>
    /// <param name="target">The point the ray is aimed at.</param>
    /// <returns>A point on the boundary.</returns>
    public static DiPoint Intersection(Outline outline, DiPoint target)
    {
        var centre = Centre(outline.Bounds);
        if (centre.X == target.X && centre.Y == target.Y) return centre;

        var dx = target.X - centre.X;
        var dy = target.Y - centre.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        var reach = Math.Max(outline.Bounds.Width, outline.Bounds.Height);

        return Project(new DiPoint(centre.X + (dx / length * reach), centre.Y + (dy / length * reach)), outline);
    }

    /// <summary>
    /// Where a connector's end belongs on a shape, given the waypoint that was stored for it.
    /// </summary>
    /// <remarks>
    /// A waypoint farther from the shape than the shape is large cannot be an attachment to it, and
    /// projecting it would pile every such connector onto one corner, so it leaves the outline on the
    /// line towards the other end instead.
    /// </remarks>
    /// <param name="waypoint">The stored end.</param>
    /// <param name="outline">The outline of the shape it belongs to.</param>
    /// <param name="other">The centre of the shape at the other end.</param>
    /// <returns>A point on the boundary.</returns>
    public static DiPoint Normalize(DiPoint waypoint, Outline outline, DiPoint other)
    {
        var reach = Math.Max(outline.Bounds.Width, outline.Bounds.Height);

        return DistanceToBounds(waypoint, outline.Bounds) <= reach
            ? Project(waypoint, outline)
            : Intersection(outline, other);
    }

    /// <summary>The distance from a point to a rectangle, zero inside it.</summary>
    /// <param name="point">The point.</param>
    /// <param name="box">The rectangle.</param>
    /// <returns>The distance.</returns>
    public static double DistanceToBounds(DiPoint point, Bounds box)
    {
        var dx = Math.Max(Math.Max(box.X - point.X, 0), point.X - (box.X + box.Width));
        var dy = Math.Max(Math.Max(box.Y - point.Y, 0), point.Y - (box.Y + box.Height));

        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static DiPoint ProjectToRect(DiPoint point, Bounds box)
    {
        var left = box.X;
        var right = box.X + box.Width;
        var top = box.Y;
        var bottom = box.Y + box.Height;

        var clampedX = Math.Min(right, Math.Max(left, point.X));
        var clampedY = Math.Min(bottom, Math.Max(top, point.Y));

        var inside = point.X > left && point.X < right && point.Y > top && point.Y < bottom;
        if (!inside) return new DiPoint(clampedX, clampedY);

        // Inside, the nearest boundary point is on whichever edge is closest.
        var best = (Edge: new DiPoint(left, clampedY), Distance: point.X - left);

        var candidates = new[]
        {
            (Edge: new DiPoint(right, clampedY), Distance: right - point.X),
            (Edge: new DiPoint(clampedX, top), Distance: point.Y - top),
            (Edge: new DiPoint(clampedX, bottom), Distance: bottom - point.Y),
        };

        foreach (var candidate in candidates)
        {
            if (candidate.Distance < best.Distance) best = candidate;
        }

        return best.Edge;
    }

    private static DiPoint ProjectToRoundedRect(DiPoint point, Bounds box, double radius)
    {
        var candidate = ProjectToRect(point, box);
        var r = Math.Min(radius, Math.Min(box.Width / 2, box.Height / 2));
        if (r <= 0) return candidate;

        var left = box.X + r;
        var right = box.X + box.Width - r;
        var top = box.Y + r;
        var bottom = box.Y + box.Height - r;

        var inLeft = candidate.X < left;
        var inRight = candidate.X > right;
        var inTop = candidate.Y < top;
        var inBottom = candidate.Y > bottom;
        if (!(inLeft || inRight) || !(inTop || inBottom)) return candidate;

        var centreX = inLeft ? left : right;
        var centreY = inTop ? top : bottom;
        var dx = point.X - centreX;
        var dy = point.Y - centreY;

        // Only within the arc's own quadrant; outside it the straight side is nearer.
        var outward = (inLeft ? dx <= 0 : dx >= 0) && (inTop ? dy <= 0 : dy >= 0);
        if (!outward) return candidate;

        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length == 0)
        {
            return new DiPoint(
                centreX + ((inLeft ? -r : r) * Math.Sqrt(0.5)),
                centreY + ((inTop ? -r : r) * Math.Sqrt(0.5)));
        }

        return new DiPoint(centreX + (dx / length * r), centreY + (dy / length * r));
    }

    private static DiPoint ProjectToEllipse(DiPoint point, Bounds box)
    {
        var centre = Centre(box);
        var radiusX = box.Width / 2;
        var radiusY = box.Height / 2;
        if (radiusX == 0 || radiusY == 0) return centre;

        var dx = point.X - centre.X;
        var dy = point.Y - centre.Y;
        if (dx == 0 && dy == 0) return new DiPoint(centre.X + radiusX, centre.Y);

        var normalised = Math.Sqrt(Math.Pow(dx / radiusX, 2) + Math.Pow(dy / radiusY, 2));

        return new DiPoint(centre.X + (dx / normalised), centre.Y + (dy / normalised));
    }

    private static DiPoint[] DiamondVertices(Bounds box)
    {
        var centre = Centre(box);

        return
        [
            new DiPoint(centre.X, box.Y),
            new DiPoint(box.X + box.Width, centre.Y),
            new DiPoint(centre.X, box.Y + box.Height),
            new DiPoint(box.X, centre.Y),
        ];
    }

    private static DiPoint ProjectToPolygon(DiPoint point, DiPoint[] vertices)
    {
        var best = point;
        var bestDistance = double.PositiveInfinity;

        for (var index = 0; index < vertices.Length; index++)
        {
            var candidate = ProjectToSegment(point, vertices[index], vertices[(index + 1) % vertices.Length]);
            var distance = Math.Sqrt(Math.Pow(candidate.X - point.X, 2) + Math.Pow(candidate.Y - point.Y, 2));

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    private static DiPoint ProjectToSegment(DiPoint point, DiPoint from, DiPoint to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared == 0) return from;

        var t = Math.Min(1, Math.Max(0, (((point.X - from.X) * dx) + ((point.Y - from.Y) * dy)) / lengthSquared));

        return new DiPoint(from.X + (t * dx), from.Y + (t * dy));
    }
}
