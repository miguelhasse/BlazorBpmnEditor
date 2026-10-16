using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// Where an expanded Sub-Process sits, and how big it is.
/// </summary>
/// <remarks>
/// <para>
/// Expanding is not only a flag: section 1.2.1 says the command "updates <c>BPMNShape.isExpanded</c>
/// and the outer shape bounds", because a container that draws its content has to be big enough to
/// hold it and a collapsed one has to be small enough to read as a leaf. This is that arithmetic,
/// kept apart from the command so the component can predict what the editor will do — the same
/// reason <see cref="BpmnLaneLayout"/> is public.
/// </para>
/// <para>
/// The load-bearing choice is <see cref="Resized"/>. A Sub-Process could be expanded by keeping its
/// content where the content already is and drawing the container around it, which is the literal
/// reading of "never changes child BPMNDI coordinates". On a real file that makes the Sub-Process
/// teleport: in the nested-subprocesses sample it would move from (345, 80) to about (130, 59) and
/// land on top of the Start Event and the Gateway. So the container stays where the user put it and
/// the content is placed inside, which keeps the diagram legible and keeps the children's geometry
/// relative to each other exactly as it was.
/// </para>
/// <para>
/// It keeps the corner rather than the centre, and that is not arbitrary. Growing from the centre
/// pushes the neighbours on <em>every</em> side, including leftwards and upwards, where there is
/// usually nothing to give: the Pool around it has to grow backwards to follow content shoved out
/// of its leading edge, which reads as the whole diagram sliding. Growing from the corner opens the
/// diagram in one direction, down and to the right, the way it is read.
/// </para>
/// <para>
/// Either way it is its own inverse, which is what makes expanding and collapsing a cycle rather
/// than a drift — the shape ends where it started, and so does everything that moved aside for it.
/// </para>
/// </remarks>
public static class BpmnSubProcessLayout
{
    /// <summary>
    /// The box that encloses everything drawn on a plane.
    /// </summary>
    /// <remarks>
    /// Waypoints count. A connector that loops out past its endpoints — a boundary event's flow
    /// going back to a task above it, say — is content the container has to hold, and sizing from
    /// shapes alone would clip it.
    /// </remarks>
    /// <param name="shapes">The shapes to enclose.</param>
    /// <param name="edges">The edges whose routes to enclose.</param>
    /// <returns>The enclosing box, or <see langword="null"/> when there is nothing to enclose.</returns>
    public static Bounds? ContentBox(IEnumerable<BpmnShape> shapes, IEnumerable<BpmnEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        ArgumentNullException.ThrowIfNull(edges);

        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;

        foreach (var bounds in shapes.Select(shape => shape.Bounds))
        {
            left = Math.Min(left, bounds.X);
            top = Math.Min(top, bounds.Y);
            right = Math.Max(right, bounds.X + bounds.Width);
            bottom = Math.Max(bottom, bounds.Y + bounds.Height);
        }

        foreach (var point in edges.SelectMany(edge => edge.Waypoints))
        {
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }

        return double.IsInfinity(left) ? null : new Bounds(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// The size an expanded Sub-Process takes to hold its content.
    /// </summary>
    /// <param name="content">The content box, or <see langword="null"/> when nothing is drawn.</param>
    /// <param name="options">Where the padding and the minimum come from.</param>
    /// <returns>A width and height, never below the configured minimum.</returns>
    public static (double Width, double Height) ExpandedSize(Bounds? content, EditingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var padding = options.SubProcessPadding;

        return (
            Math.Max(options.MinimumExpandedWidth, (content?.Width ?? 0d) + (2d * padding)),
            Math.Max(options.MinimumExpandedHeight, (content?.Height ?? 0d) + (2d * padding)));
    }

    /// <summary>
    /// A box of the given size in the same place as the one it replaces.
    /// </summary>
    /// <remarks>
    /// The origin is kept, so a container grows down and to the right and shrinks back up and to
    /// the left. Exactly reversible, and one-directional, which is what lets the neighbours be
    /// moved aside and returned by the same arithmetic rather than a remembered list.
    /// </remarks>
    /// <param name="current">The bounds being replaced.</param>
    /// <param name="size">The new size.</param>
    /// <returns>The new bounds, anchored where the old ones were.</returns>
    public static Bounds Resized(Bounds current, (double Width, double Height) size) =>
        current with { Width = size.Width, Height = size.Height };

    /// <summary>The footprint a Sub-Process collapses to, in the place it already occupies.</summary>
    /// <param name="current">The expanded bounds.</param>
    /// <param name="options">Where the collapsed size comes from.</param>
    /// <returns>The collapsed bounds.</returns>
    public static Bounds CollapsedBounds(Bounds current, EditingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return Resized(current, (options.CollapsedWidth, options.CollapsedHeight));
    }

    /// <summary>
    /// How far content must move to sit inside a container at its padding inset.
    /// </summary>
    /// <remarks>
    /// A plane's origin is arbitrary — nothing in BPMNDI ties one plane's coordinates to another's
    /// — so bringing content across from a Sub-Process's own plane is a rigid translation, and
    /// which corner it is measured from is a choice. The top-left inset is the one that reads as
    /// deliberate: the content sits a uniform padding in from the container's leading corner rather
    /// than wherever the source plane's author happened to leave whitespace.
    /// </remarks>
    /// <param name="content">The content box on the plane it is coming from.</param>
    /// <param name="container">The container's new bounds.</param>
    /// <param name="options">Where the padding comes from.</param>
    /// <returns>The offset to add to every coordinate being moved.</returns>
    public static (double X, double Y) OffsetInto(Bounds content, Bounds container, EditingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var padding = options.SubProcessPadding;

        return (container.X + padding - content.X, container.Y + padding - content.Y);
    }

    /// <summary>Moves a box by an offset.</summary>
    /// <param name="bounds">The box to move.</param>
    /// <param name="offset">How far.</param>
    /// <returns>The moved box, the same size.</returns>
    public static Bounds Shift(Bounds bounds, (double X, double Y) offset) =>
        bounds with { X = bounds.X + offset.X, Y = bounds.Y + offset.Y };

    /// <summary>Moves a waypoint by an offset.</summary>
    /// <param name="point">The point to move.</param>
    /// <param name="offset">How far.</param>
    /// <returns>The moved point.</returns>
    public static DiPoint Shift(DiPoint point, (double X, double Y) offset) =>
        point with { X = point.X + offset.X, Y = point.Y + offset.Y };
}
