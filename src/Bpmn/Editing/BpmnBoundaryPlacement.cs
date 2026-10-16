using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// Works out which Activity a dropped Boundary Event attaches to, and where on it.
/// </summary>
/// <remarks>
/// <para>
/// Section 9.3: "Boundary Event creation requires an Activity host and immediately sets
/// <c>attachedToRef</c>; it cannot exist as a free-floating Event." So a Boundary Event is not
/// dropped at a point the way every other Flow Node is — it is dropped <em>onto</em> something, and
/// the drop has to say what. Resolving that here rather than on the canvas keeps it testable
/// against a document, and DI-010's requirement that the centre sit exactly on the host's outline
/// is geometry the document already has.
/// </para>
/// <para>
/// A drop that finds no Activity resolves to nothing, and the caller refuses the creation. That is
/// the whole reason this returns an option rather than a best guess: attaching to whichever
/// Activity happened to be nearest on a page-wide search is how a Boundary Event ends up on a
/// shape the user never aimed at.
/// </para>
/// </remarks>
public static class BpmnBoundaryPlacement
{
    /// <summary>
    /// How far outside an Activity a drop still counts as landing on it.
    /// </summary>
    /// <remarks>
    /// Generous enough that the Event does not have to be dropped pixel-perfect on the border, and
    /// small enough that it cannot claim a neighbouring shape — an Event is 36 units across, so
    /// this is roughly "the drop overlaps the border". Inside the Activity there is no limit at
    /// all: anywhere on it is on it.
    /// </remarks>
    public const double SnapDistance = 24d;

    /// <summary>
    /// The Activity a Boundary Event dropped here attaches to, and the bounds it takes.
    /// </summary>
    /// <param name="document">The document being edited.</param>
    /// <param name="dropped">Where the Event was dropped, in paper coordinates.</param>
    /// <param name="requireInsideHost">
    /// Whether the drop must land inside an Activity rather than within the ordinary snap distance
    /// of its outline.
    /// </param>
    /// <returns>The host and the snapped bounds, or <see langword="null"/> if no Activity is near.</returns>
    public static (string HostId, Bounds Bounds)? Resolve(
        BpmnDocument document,
        Bounds dropped,
        bool requireInsideHost = false) => Resolve(document, dropped, null, requireInsideHost);

    /// <summary>Resolves a host using only geometry from the requested diagram.</summary>
    /// <param name="document">The document being edited.</param>
    /// <param name="dropped">Where the Event was dropped, in paper coordinates.</param>
    /// <param name="diagramId">The diagram to search; null searches all diagrams.</param>
    /// <param name="requireInsideHost">Whether the drop must land inside an Activity.</param>
    /// <returns>The host and snapped bounds, or null when no Activity is near.</returns>
    public static (string HostId, Bounds Bounds)? Resolve(
        BpmnDocument document,
        Bounds dropped,
        string? diagramId,
        bool requireInsideHost = false)
    {
        ArgumentNullException.ThrowIfNull(document);

        var centre = CentreOf(dropped);

        /*
         * Anywhere on the Activity, not just near its border. Requiring the drop to land within a
         * few pixels of the outline meant that aiming at the middle of a Task — which is where a
         * user aims when they mean "this Task" — did nothing at all, and the larger the Activity
         * the larger the dead area in the middle of it. Where on the border the Event ends up is
         * this code's job to work out, not the user's to hit.
         */
        var candidates =
            from shape in document.Diagrams
                .Where(diagram => diagramId is null || diagram.Id == diagramId)
                .SelectMany(diagram => diagram.Plane.Shapes)
            let node = document.FindFlowNode(shape.BpmnElement)
            where node is not null && IsAttachable(node.Type)
            let inside = Contains(shape.Bounds, centre)
            let distance = inside ? 0d : DistanceToOutline(shape.Bounds, centre)
            where inside || (!requireInsideHost && distance <= SnapDistance)
            orderby distance, Area(shape.Bounds)
            select (shape.BpmnElement, shape.Bounds);

        // A drop that is on something beats one that is merely near it, and the smaller shape beats
        // the larger: dropping on a Task inside an expanded Sub-Process attaches to the Task, which
        // is the one the pointer was on.
        if (candidates.FirstOrDefault() is not { BpmnElement: { } hostId } nearest) return null;

        return (hostId, OnOutline(nearest.Bounds, dropped));
    }

    /// <summary>
    /// Moves a Boundary Event's bounds so its centre lies exactly on its host's outline (DI-010).
    /// </summary>
    /// <param name="host">The host Activity's bounds.</param>
    /// <param name="boundary">The Event's requested bounds, whose size is kept.</param>
    /// <returns>The same size, centred on the nearest point of the host's perimeter.</returns>
    public static Bounds OnOutline(Bounds host, Bounds boundary)
    {
        var (x, y) = CentreOf(boundary);
        var (px, py) = NearestOnPerimeter(host, x, y);

        return boundary with { X = px - (boundary.Width / 2d), Y = py - (boundary.Height / 2d) };
    }

    /// <summary>
    /// Keeps a Boundary Event where it was on a host that changed size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not <see cref="OnOutline(Bounds, Bounds)"/>, which finds the nearest point of the perimeter.
    /// That is right for a drop, where the pointer says where the user wants it, and wrong for a
    /// host that grew underneath it: after growing, the Event's old centre falls <em>inside</em> the
    /// new rectangle, and "nearest" then means whichever edge happens to be closest — so an Event on
    /// the bottom edge of a Sub-Process can surface on its left.
    /// </para>
    /// <para>
    /// So the edge it is on and how far along that edge it sits are measured first, and re-applied
    /// to the new outline. An Event a third of the way along the bottom stays a third of the way
    /// along the bottom, which is what "remains attached to and moves with the Sub-Process"
    /// (section 1.2.1) has to mean when the thing it is attached to changes shape.
    /// </para>
    /// </remarks>
    /// <param name="before">The host's bounds as the Event was placed against them.</param>
    /// <param name="after">The host's new bounds.</param>
    /// <param name="boundary">The Event's current bounds, whose size is kept.</param>
    /// <returns>The same size, in the same relative place on the new outline.</returns>
    public static Bounds Rehost(Bounds before, Bounds after, Bounds boundary)
    {
        var (x, y) = CentreOf(boundary);
        var (edge, along) = EdgeOf(before, x, y);

        var (px, py) = edge switch
        {
            Edge.Top => (after.X + (along * after.Width), after.Y),
            Edge.Bottom => (after.X + (along * after.Width), after.Y + after.Height),
            Edge.Left => (after.X, after.Y + (along * after.Height)),
            _ => (after.X + after.Width, after.Y + (along * after.Height)),
        };

        return boundary with { X = px - (boundary.Width / 2d), Y = py - (boundary.Height / 2d) };
    }

    private enum Edge
    {
        Top,
        Right,
        Bottom,
        Left,
    }

    /// <summary>
    /// Which edge a point sits on, and how far along it, as a fraction from the edge's start.
    /// </summary>
    /// <remarks>
    /// Decided by which edge the point is nearest, so it survives the Event's centre being a pixel
    /// off the outline — which it routinely is, because a shape's bounds are doubles and a drag
    /// reports integers. A degenerate host has no edges to be a fraction along, so the fraction is
    /// zero rather than a division by zero.
    /// </remarks>
    private static (Edge Edge, double Along) EdgeOf(Bounds host, double x, double y)
    {
        var left = Math.Abs(x - host.X);
        var right = Math.Abs(x - (host.X + host.Width));
        var top = Math.Abs(y - host.Y);
        var bottom = Math.Abs(y - (host.Y + host.Height));

        var nearest = Math.Min(Math.Min(left, right), Math.Min(top, bottom));

        // Top and bottom first, because a corner belongs to a horizontal edge by convention and
        // both answers are equally true there.
        if (nearest == top) return (Edge.Top, Fraction(x - host.X, host.Width));
        if (nearest == bottom) return (Edge.Bottom, Fraction(x - host.X, host.Width));
        if (nearest == left) return (Edge.Left, Fraction(y - host.Y, host.Height));

        return (Edge.Right, Fraction(y - host.Y, host.Height));
    }

    private static double Fraction(double offset, double extent) =>
        extent <= 0d ? 0d : Math.Clamp(offset / extent, 0d, 1d);

    /// <summary>Whether a Boundary Event may attach to this type.</summary>
    /// <param name="type">The candidate host's type.</param>
    /// <returns><see langword="true"/> for any Activity.</returns>
    public static bool IsAttachable(BpmnFlowNodeType type) => BpmnElementInspector.CategoryOf(type)
        == BpmnElementCategory.Activity;

    private static (double X, double Y) CentreOf(Bounds bounds) =>
        (bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));

    private static double Area(Bounds bounds) => bounds.Width * bounds.Height;

    /// <summary>Whether a point lies within a rectangle, border included.</summary>
    private static bool Contains(Bounds rectangle, (double X, double Y) point) =>
        point.X >= rectangle.X
        && point.X <= rectangle.X + rectangle.Width
        && point.Y >= rectangle.Y
        && point.Y <= rectangle.Y + rectangle.Height;

    /// <summary>
    /// The shortest distance from a point to a rectangle's perimeter.
    /// </summary>
    /// <remarks>
    /// Zero on the border and positive both outside and inside, so it says how far a point is from
    /// the border rather than whether it is within the shape. <see cref="Contains"/> answers that,
    /// and the two are used together: inside counts as a hit at any distance, outside only within
    /// <see cref="SnapDistance"/>.
    /// </remarks>
    private static double DistanceToOutline(Bounds rectangle, (double X, double Y) point)
    {
        var (px, py) = NearestOnPerimeter(rectangle, point.X, point.Y);
        var dx = px - point.X;
        var dy = py - point.Y;

        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static (double X, double Y) NearestOnPerimeter(Bounds rectangle, double x, double y)
    {
        var left = rectangle.X;
        var top = rectangle.Y;
        var right = rectangle.X + rectangle.Width;
        var bottom = rectangle.Y + rectangle.Height;

        var clampedX = Math.Clamp(x, left, right);
        var clampedY = Math.Clamp(y, top, bottom);

        // Outside the rectangle the clamp already lies on the perimeter.
        if (x < left || x > right || y < top || y > bottom) return (clampedX, clampedY);

        // Inside it, the clamp is the point itself, so push it out to whichever edge is closest.
        var toLeft = x - left;
        var toRight = right - x;
        var toTop = y - top;
        var toBottom = bottom - y;
        var nearest = Math.Min(Math.Min(toLeft, toRight), Math.Min(toTop, toBottom));

        if (nearest == toLeft) return (left, clampedY);
        if (nearest == toRight) return (right, clampedY);

        return nearest == toTop ? (clampedX, top) : (clampedX, bottom);
    }
}
