using System.Collections.Frozen;

using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// Expanding and collapsing a Sub-Process (specification section 1.2.1).
/// </summary>
/// <remarks>
/// <para>
/// A Sub-Process is drawn one of two ways and the difference is entirely in the DI. Collapsed, it
/// is an opaque leaf and its children are drawn on a <c>BPMNPlane</c> of its own; expanded, its
/// children are drawn on the plane it is on. So expanding and collapsing move DI between planes,
/// and change nothing semantic at all — not one <c>flowElements</c> list, id, container, Sequence
/// Flow or Boundary Event attachment is touched by either command.
/// </para>
/// <para>
/// The translation between planes is rigid. A plane's origin is arbitrary — nothing in BPMNDI ties
/// one plane's coordinates to another's — so moving a block of content between them preserves
/// every distance within it, which is the part of "never changes child BPMNDI coordinates"
/// (section 1.2.1) that can survive contact with per-Sub-Process planes.
/// </para>
/// <para>
/// Neither command recurses. A descendant that was expanded when this Sub-Process was collapsed
/// already has <em>its</em> children inline on the same plane, and a collapsed descendant keeps its
/// own diagram; moving the block verbatim therefore restores the exact nested expansion state that
/// was in force, without this code needing to know what it was.
/// </para>
/// </remarks>
public static partial class BpmnDocumentEditor
{
    // --- expanding ----------------------------------------------------------------

    private static BpmnEditResult ApplyExpand(
        BpmnDocument document,
        ExpandSubProcessCommand command,
        EditingOptions options)
    {
        if (Resolve(document, command.ElementId, command.HostDiagramId) is not { } target)
        {
            return BpmnEditResult.Reject(document, Unresolvable(document, command.ElementId, command.HostDiagramId));
        }

        var (host, shape) = target;

        if (shape.IsExpanded == true)
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-092",
                "This Sub-Process is already expanded.",
                command.ElementId));
        }

        if (command.Bounds is { } requested && Invalid(requested) is { } bad)
        {
            return BpmnEditResult.Reject(document, bad with { ElementId = command.ElementId });
        }

        // The plane the content lives on while collapsed, if the document has one. A Sub-Process
        // whose content was never drawn simply has none, which is a legal file and expands to an
        // empty container of the minimum size.
        var source = document.Diagrams.FirstOrDefault(diagram => diagram.Plane.BpmnElement == command.ElementId);
        var content = source is null
            ? null
            : BpmnSubProcessLayout.ContentBox(source.Plane.Shapes, source.Plane.Edges);

        var bounds = command.Bounds ?? BpmnSubProcessLayout.Resized(
            shape.Bounds,
            BpmnSubProcessLayout.ExpandedSize(content, options));

        /*
         * CT-007 in the other direction: the host plane must not already be drawing something from
         * inside this Sub-Process, or expanding would give that element two shapes on one plane and
         * D022 would report it. Reachable only from a hand-edited file, which is exactly when a
         * check is worth having.
         */
        if (AlreadyDrawn(document, host, command.ElementId) is { } clash)
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "CT-007",
                $"'{clash}' is inside this Sub-Process and is already drawn on '{host.Id}'.",
                command.ElementId));
        }

        var offset = content is { } box
            ? BpmnSubProcessLayout.OffsetInto(box, bounds, options)
            : (X: 0d, Y: 0d);

        (IReadOnlyList<BpmnShape> Shapes, IReadOnlyList<BpmnEdge> Edges) arriving = source is null
            ? (Array.Empty<BpmnShape>(), Array.Empty<BpmnEdge>())
            : ([.. source.Plane.Shapes.Select(child => Shift(child, offset))],
               [.. source.Plane.Edges.Select(edge => Shift(edge, offset))]);

        var opened = Redraw(
            document,
            host,
            shape,
            bounds,
            isExpanded: true,
            added: arriving,
            removed: (Array.Empty<BpmnShape>(), Array.Empty<BpmnEdge>()));

        // The source diagram has done its job: its content is on the host plane now, and leaving it
        // would draw everything twice — which is CT-009.
        if (source is not null)
        {
            opened = opened with { Diagrams = [.. opened.Diagrams.Where(diagram => diagram.Id != source.Id)] };
            opened = WithLabelStyles(opened, host.Id, source.LabelStyles);
        }

        var notes = new List<BpmnEditDiagnostic>();

        if (source is null && document.FlowNodes.Any(node => node.ContainerId == command.ElementId))
        {
            /*
             * Reported rather than invented. The elements exist and are still owned by this
             * Sub-Process; what is missing is any statement of where they go, and sections 10.6 and 11
             * are clear that the editor reports rather than guesses at geometry the author never
             * supplied.
             */
            notes.Add(new BpmnEditDiagnostic(
                BpmnEditSeverity.Info,
                "EDIT-094",
                "This Sub-Process holds content that no diagram draws, so expanding shows an empty container.",
                command.ElementId));
        }

        // Everything the container now draws, plus the container and its own events: none of it
        // may be shoved aside by the room being made for it.
        var arrived = arriving.Shapes.Select(child => child.BpmnElement)
            .Concat(arriving.Edges.Select(edge => edge.BpmnElement))
            .Concat(document.FlowNodes.Where(node => node.AttachedToRef == command.ElementId).Select(node => node.Id))
            .ToHashSet(StringComparer.Ordinal);

        var roomy = MakeRoom(opened, host, command.ElementId, arrived, Opening.Between(shape.Bounds, bounds), options);

        return BpmnEditResult.Applied(Reattach(roomy, host.Id, command.ElementId, shape.Bounds, bounds))
            with
        { Diagnostics = notes };
    }

    // --- collapsing ---------------------------------------------------------------

    private static BpmnEditResult ApplyCollapse(
        BpmnDocument document,
        CollapseSubProcessCommand command,
        EditingOptions options)
    {
        if (Resolve(document, command.ElementId, command.HostDiagramId) is not { } target)
        {
            return BpmnEditResult.Reject(document, Unresolvable(document, command.ElementId, command.HostDiagramId));
        }

        var (host, shape) = target;

        if (shape.IsExpanded != true)
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-092",
                "This Sub-Process is already collapsed.",
                command.ElementId));
        }

        if (command.Bounds is { } requested && Invalid(requested) is { } bad)
        {
            return BpmnEditResult.Reject(document, bad with { ElementId = command.ElementId });
        }

        var bounds = command.Bounds ?? BpmnSubProcessLayout.CollapsedBounds(shape.Bounds, options);

        // Everything this Sub-Process holds, however deeply, decided by containment rather than by
        // which shapes happen to sit inside its rectangle.
        var inside = Inside(document, command.ElementId);

        (IReadOnlyList<BpmnShape> Shapes, IReadOnlyList<BpmnEdge> Edges) leaving = (
            [.. host.Plane.Shapes.Where(child => inside.Contains(child.BpmnElement))],
            [.. host.Plane.Edges.Where(edge => inside.Contains(edge.BpmnElement))]);

        var diagramId = command.ContentDiagramId ?? $"{command.ElementId}_diagram";
        var planeId = command.ContentPlaneId ?? $"{command.ElementId}_plane";

        var existing = document.Diagrams.FirstOrDefault(diagram => diagram.Plane.BpmnElement == command.ElementId);

        if (existing is null && DiagramIdTaken(document, diagramId, planeId) is { } taken)
        {
            return BpmnEditResult.Reject(document, taken with { ElementId = command.ElementId });
        }

        var closed = Redraw(
            document,
            host,
            shape,
            bounds,
            isExpanded: false,
            added: (Array.Empty<BpmnShape>(), Array.Empty<BpmnEdge>()),
            removed: leaving);

        /*
         * The content keeps its coordinates. The destination plane's origin is arbitrary, so the
         * identity translation is both the cheapest choice and the one that makes the next
         * expansion's arithmetic exact — nothing is rounded on the way out and back.
         */
        closed = existing is null
            ? closed with
            {
                Diagrams =
                [
                    .. closed.Diagrams,
                    new BpmnDiagram(diagramId, new BpmnPlane(planeId, command.ElementId, leaving.Shapes, leaving.Edges))
                    {
                        LabelStyles = StylesFor(host, leaving),
                    },
                ],
            }
            : MergeInto(closed, existing.Id, leaving, StylesFor(host, leaving));

        /*
         * The same arithmetic backwards. Everything that moved out of the way when this was
         * expanded lay clear of the collapsed box on one side or another, and shrinking gives it
         * the negative of the distance it was given — so it lands exactly where it started.
         */
        var left = document.FlowNodes.Where(node => node.AttachedToRef == command.ElementId)
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        var tidied = MakeRoom(closed, host, command.ElementId, left, Opening.Between(shape.Bounds, bounds), options);

        return BpmnEditResult.Applied(Reattach(tidied, host.Id, command.ElementId, shape.Bounds, bounds));
    }

    // --- making room --------------------------------------------------------------

    /// <summary>
    /// How far a container's sides moved, and the box they moved from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Expanding opens the diagram up around the container rather than dropping a larger rectangle
    /// on top of its neighbours: whatever lay clear of the collapsed box on a given side moves out
    /// by exactly as much as that side grew. Collapsing runs the same arithmetic with the boxes the
    /// other way round, so every distance is negative and everything comes back to where it was.
    /// </para>
    /// <para>
    /// That symmetry is the point. It is why collapse needs no memory of what expanding displaced,
    /// and why a diagram survives being opened and closed after a reload, when a session memo would
    /// be gone. What it cannot restore is something the user moved in between, which is right: the
    /// later position is the one they chose.
    /// </para>
    /// <para>
    /// Anything straddling an edge of the old box stays put. It was already overlapping the
    /// collapsed container, and there is no side to move it to that is not equally wrong.
    /// </para>
    /// </remarks>
    private readonly record struct Opening(Bounds From, double Left, double Right, double Top, double Bottom)
    {
        public static Opening Between(Bounds before, Bounds after) => new(
            before,
            before.X - after.X,
            after.X + after.Width - (before.X + before.Width),
            before.Y - after.Y,
            after.Y + after.Height - (before.Y + before.Height));

        public bool IsNothing => Left == 0d && Right == 0d && Top == 0d && Bottom == 0d;

        /// <summary>Whether a box wraps the one that grew, and so should grow rather than move.</summary>
        public bool Encloses(Bounds box) =>
            box.X <= From.X && box.Y <= From.Y
            && box.X + box.Width >= From.X + From.Width
            && box.Y + box.Height >= From.Y + From.Height;

        public (double X, double Y) For(Bounds box) => (
            box.X + box.Width <= From.X ? -Left : box.X >= From.X + From.Width ? Right : 0d,
            box.Y + box.Height <= From.Y ? -Top : box.Y >= From.Y + From.Height ? Bottom : 0d);

        public (double X, double Y) For(DiPoint point) => (
            point.X <= From.X ? -Left : point.X >= From.X + From.Width ? Right : 0d,
            point.Y <= From.Y ? -Top : point.Y >= From.Y + From.Height ? Bottom : 0d);
    }

    /// <summary>
    /// Moves the container's neighbours aside, and grows whatever holds them all.
    /// </summary>
    /// <param name="document">The document as the container's own change left it.</param>
    /// <param name="host">The plane being redrawn.</param>
    /// <param name="elementId">The Sub-Process that changed size.</param>
    /// <param name="settled">Shapes this must not move: the container, its content, its events.</param>
    /// <param name="opening">How far each side moved.</param>
    /// <param name="options">Where the margin and the lane tiling rules come from.</param>
    /// <returns>The document with room made, or the same document when nothing had to move.</returns>
    private static BpmnDocument MakeRoom(
        BpmnDocument document,
        BpmnDiagram host,
        string elementId,
        HashSet<string> settled,
        Opening opening,
        EditingOptions options)
    {
        if (opening.IsNothing) return document;

        var plane = document.Diagrams.FirstOrDefault(diagram => diagram.Id == host.Id)?.Plane;
        if (plane is null) return document;

        var moves = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);

        foreach (var shape in plane.Shapes)
        {
            if (shape.BpmnElement == elementId || settled.Contains(shape.BpmnElement)) continue;

            // A Pool, a Lane or an outer Sub-Process wraps this one; it has to grow to keep holding
            // it, which ExpandContainers does below, and moving it would take its whole contents.
            if (opening.Encloses(shape.Bounds)) continue;

            var (dx, dy) = opening.For(shape.Bounds);
            if (dx == 0d && dy == 0d) continue;

            moves[shape.Id] = (dx, dy);
        }

        // Bounds for the container-growth pass, which thinks in rectangles rather than offsets.
        var requested = moves.ToDictionary(
            entry => entry.Key,
            entry => BpmnSubProcessLayout.Shift(plane.Shapes.First(shape => shape.Id == entry.Key).Bounds, entry.Value),
            StringComparer.Ordinal);

        var shapes = plane.Shapes.ToFrozenDictionary(shape => shape.Id, StringComparer.Ordinal);

        if (options.AutoExpandContainers)
        {
            // The container's own new bounds have to be in the request too, or the Pool around it
            // grows to fit the neighbours and not the thing that pushed them.
            var container = plane.Shapes.FirstOrDefault(shape => shape.BpmnElement == elementId);
            if (container is not null) requested[container.Id] = container.Bounds;

            ExpandContainers(document, shapes, requested, options);
        }

        if (requested.Count == 0) return document;

        return document with
        {
            Diagrams =
            [
                .. document.Diagrams.Select(diagram => diagram.Id != host.Id ? diagram : diagram with
                {
                    Plane = diagram.Plane with
                    {
                        Shapes =
                        [
                            .. diagram.Plane.Shapes.Select(shape => moves.TryGetValue(shape.Id, out var offset)
                                // Through Shift rather than by writing Bounds, so the shape's own
                                // BPMNLabel travels with it: a label has bounds of its own, and one
                                // left behind sits over whatever the shape used to cover.
                                ? Shift(shape, offset)
                                : requested.TryGetValue(shape.Id, out var grown) ? shape with { Bounds = grown } : shape),
                        ],
                        Edges = [.. diagram.Plane.Edges.Select(edge => settled.Contains(edge.BpmnElement) ? edge : Reopen(edge, opening))],
                    },
                }),
            ],
        };
    }

    /// <summary>
    /// A route, after the opening moved its ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A route wholly on one side of the opening moves with everything else on that side, rigidly,
    /// and keeps exactly the shape its author gave it.
    /// </para>
    /// <para>
    /// One that spans the opening cannot. Its two ends move by different amounts, so the bends
    /// between them no longer describe anything — they were drawn for a distance that has changed.
    /// Shifting each point by its own side leaves a diagonal through the gap, which is the one
    /// thing this editor's connectors are never supposed to be, so the interior is replaced with a
    /// single square elbow. The ends are kept, because they are where the route meets its shapes.
    /// </para>
    /// </remarks>
    private static BpmnEdge Reopen(BpmnEdge edge, Opening opening)
    {
        if (edge.Waypoints.Count < 2) return edge;

        var startsAt = opening.For(edge.Waypoints[0]);
        var endsAt = opening.For(edge.Waypoints[^1]);

        if (startsAt == endsAt)
        {
            return startsAt == (0d, 0d)
                ? edge
                : edge with { Waypoints = [.. edge.Waypoints.Select(point => BpmnSubProcessLayout.Shift(point, startsAt))] };
        }

        var from = BpmnSubProcessLayout.Shift(edge.Waypoints[0], startsAt);
        var to = BpmnSubProcessLayout.Shift(edge.Waypoints[^1], endsAt);

        // The elbow turns on the longer axis, which is the one the eye reads the route as following.
        var route = Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y)
            ? new[] { from, new DiPoint((from.X + to.X) / 2d, from.Y), new DiPoint((from.X + to.X) / 2d, to.Y), to }
            : [from, new DiPoint(from.X, (from.Y + to.Y) / 2d), new DiPoint(to.X, (from.Y + to.Y) / 2d), to];

        return edge with { Waypoints = route };
    }

    // --- shared -------------------------------------------------------------------

    /// <summary>The diagram and shape a command is about, when exactly one plane answers.</summary>
    /// <remarks>
    /// The type check belongs here rather than in the callers because it is part of the same
    /// question: "which shape should I change" has no answer for a Task, and answering it anyway
    /// is how a Task came to be expandable.
    /// </remarks>
    private static (BpmnDiagram Diagram, BpmnShape Shape)? Resolve(
        BpmnDocument document,
        string elementId,
        string? diagramId)
    {
        if (document.FindFlowNode(elementId) is not { FlowElementsOwned: true }) return null;

        var candidates = document.Diagrams
            .Where(diagram => diagramId is null || diagram.Id == diagramId)
            .SelectMany(diagram => diagram.Plane.Shapes.Select(shape => (Diagram: diagram, Shape: shape)))
            .Where(pair => pair.Shape.BpmnElement == elementId)
            .ToList();

        return candidates.Count == 1 ? candidates[0] : null;
    }

    /// <summary>Why <see cref="Resolve"/> could not answer, which is three different problems.</summary>
    private static BpmnEditDiagnostic Unresolvable(BpmnDocument document, string elementId, string? diagramId)
    {
        if (document.FindFlowNode(elementId) is not { } node)
        {
            return new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-090",
                $"'{elementId}' is not a Flow Node in this document.",
                elementId);
        }

        if (!node.FlowElementsOwned)
        {
            // A Call Activity is the case worth naming: it looks like a container and behaves like
            // one in every other respect, but its content belongs to a global Process it merely
            // references, so there is nothing local to draw (section 1.2.1).
            var why = node.Type == BpmnFlowNodeType.CallActivity
                ? "A Call Activity has no local content to expand; it references a global Process."
                : $"'{elementId}' is not an embedded Sub-Process.";

            return new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, "EDIT-091", why, elementId);
        }

        var drawn = document.Diagrams.Count(diagram => diagram.Plane.Shapes.Any(shape => shape.BpmnElement == elementId));

        return drawn switch
        {
            0 => new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-090",
                $"No plane draws '{elementId}'.",
                elementId),
            _ when diagramId is null => new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-093",
                $"'{elementId}' is drawn on {drawn} planes; say which one.",
                elementId),
            _ => new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-090",
                $"Plane '{diagramId}' does not draw '{elementId}'.",
                elementId),
        };
    }

    /// <summary>Rewrites one diagram: the container's shape, plus content arriving or leaving.</summary>
    private static BpmnDocument Redraw(
        BpmnDocument document,
        BpmnDiagram host,
        BpmnShape shape,
        Bounds bounds,
        bool isExpanded,
        (IReadOnlyList<BpmnShape> Shapes, IReadOnlyList<BpmnEdge> Edges) added,
        (IReadOnlyList<BpmnShape> Shapes, IReadOnlyList<BpmnEdge> Edges) removed)
    {
        var goneShapes = removed.Shapes.Select(child => child.Id).ToHashSet(StringComparer.Ordinal);
        var goneEdges = removed.Edges.Select(edge => edge.Id).ToHashSet(StringComparer.Ordinal);

        /*
         * Arriving content is appended, in the order the source plane declared it. The renderer
         * creates cells in plane order and JointJS z-order follows insertion, so a parent has to
         * come before its children — which source order already guarantees, because that plane was
         * drawn the same way.
         */
        var plane = host.Plane with
        {
            Shapes =
            [
                .. host.Plane.Shapes
                    .Where(child => !goneShapes.Contains(child.Id))
                    .Select(child => child.Id == shape.Id ? child with { Bounds = bounds, IsExpanded = isExpanded } : child),
                .. added.Shapes,
            ],
            Edges = [.. host.Plane.Edges.Where(edge => !goneEdges.Contains(edge.Id)), .. added.Edges],
        };

        return document with
        {
            Diagrams = [.. document.Diagrams.Select(diagram => diagram.Id == host.Id ? diagram with { Plane = plane } : diagram)],
        };
    }

    /// <summary>
    /// Puts the Sub-Process's own Boundary Events and external connectors back on its new outline.
    /// </summary>
    /// <remarks>
    /// These are not its content and do not travel with it — a Boundary Event attached to the
    /// Sub-Process belongs to the parent process, and an incoming Sequence Flow is the parent's too
    /// — but both are drawn against an outline that just changed size, so DI-010 and the endpoint
    /// rules would be broken if they were left alone (section 1.2.1's "external connectors remain
    /// attached to the Sub-Process boundary in both states").
    /// </remarks>
    private static BpmnDocument Reattach(
        BpmnDocument document,
        string diagramId,
        string elementId,
        Bounds before,
        Bounds after)
    {
        if (before == after) return document;

        var attached = document.FlowNodes
            .Where(node => node.AttachedToRef == elementId)
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        var touching = document.SequenceFlows
            .Where(flow => flow.SourceRef == elementId || flow.TargetRef == elementId)
            .Select(flow => flow.Id)
            .Concat(document.MessageFlows
                .Where(flow => flow.SourceRef == elementId || flow.TargetRef == elementId)
                .Select(flow => flow.Id))
            .ToHashSet(StringComparer.Ordinal);

        return document with
        {
            Diagrams =
            [
                .. document.Diagrams.Select(diagram => diagram.Id != diagramId ? diagram : diagram with
                {
                    Plane = diagram.Plane with
                    {
                        Shapes =
                        [
                            .. diagram.Plane.Shapes.Select(shape => attached.Contains(shape.BpmnElement)
                                ? shape with { Bounds = BpmnBoundaryPlacement.Rehost(before, after, shape.Bounds) }
                                : shape),
                        ],
                        Edges =
                        [
                            .. diagram.Plane.Edges.Select(edge => touching.Contains(edge.BpmnElement)
                                ? edge with { Waypoints = Retouch(edge.Waypoints, after) }
                                : edge),
                        ],
                    },
                }),
            ],
        };
    }

    /// <summary>
    /// Moves whichever end of a route lies against the changed shape back onto its outline.
    /// </summary>
    /// <remarks>
    /// Only the terminal point. The bends between are the user's and sections 10.6 and 11 say the editor
    /// keeps what the file says — re-routing the whole connector because its endpoint moved would
    /// discard geometry nobody asked it to touch.
    /// </remarks>
    private static IReadOnlyList<DiPoint> Retouch(IReadOnlyList<DiPoint> waypoints, Bounds host)
    {
        if (waypoints.Count < 2) return waypoints;

        var first = waypoints[0];
        var last = waypoints[^1];

        // Whichever end is nearer the shape is the one attached to it. A connector between two
        // elements has one end on each, so this cannot pick both.
        var startIsHost = Distance(first, host) <= Distance(last, host);
        var route = waypoints.ToArray();

        if (startIsHost) route[0] = Snap(waypoints[1], host);
        else route[^1] = Snap(waypoints[^2], host);

        return route;
    }

    private static DiPoint Snap(DiPoint towards, Bounds host)
    {
        var centre = new DiPoint(host.X + (host.Width / 2d), host.Y + (host.Height / 2d));
        var box = BpmnBoundaryPlacement.OnOutline(host, new Bounds(towards.X - 0.5, towards.Y - 0.5, 1, 1));

        return host.Width <= 0 || host.Height <= 0 ? centre : new DiPoint(box.X + 0.5, box.Y + 0.5);
    }

    private static double Distance(DiPoint point, Bounds host)
    {
        var dx = Math.Max(host.X - point.X, Math.Max(0d, point.X - (host.X + host.Width)));
        var dy = Math.Max(host.Y - point.Y, Math.Max(0d, point.Y - (host.Y + host.Height)));

        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>Everything a Sub-Process holds, however deeply nested.</summary>
    private static HashSet<string> Inside(BpmnDocument document, string containerId)
    {
        var held = Contained(document).ToLookup(entry => entry.Container, entry => entry.Element, StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>([containerId]);

        while (pending.Count > 0)
        {
            foreach (var child in held[pending.Dequeue()])
            {
                if (found.Add(child)) pending.Enqueue(child);
            }
        }

        /*
         * Boundary Events attached to things inside travel too. They are owned by the same
         * container as their host, so they are already in the set — except when the host is the
         * Sub-Process itself, whose events belong to the parent and must stay behind. That case is
         * excluded by construction: the walk starts inside, never at the Sub-Process's own owner.
         */
        return found;
    }

    private static IEnumerable<(string Container, string Element)> Contained(BpmnDocument document)
    {
        foreach (var node in document.FlowNodes) yield return (node.ContainerId, node.Id);
        foreach (var flow in document.SequenceFlows) yield return (flow.ContainerId, flow.Id);
        foreach (var association in document.Associations) yield return (association.ContainerId, association.Id);
        foreach (var annotation in document.TextAnnotations) yield return (annotation.ContainerId, annotation.Id);
        foreach (var group in document.Groups) yield return (group.ContainerId, group.Id);
        foreach (var reference in document.DataObjectReferences) yield return (reference.ContainerId, reference.Id);
        foreach (var reference in document.DataStoreReferences) yield return (reference.ContainerId, reference.Id);
    }

    /// <summary>Something inside this Sub-Process that the host plane already draws, if any.</summary>
    private static string? AlreadyDrawn(BpmnDocument document, BpmnDiagram host, string elementId)
    {
        var inside = Inside(document, elementId);

        return host.Plane.Shapes.Select(shape => shape.BpmnElement)
            .Concat(host.Plane.Edges.Select(edge => edge.BpmnElement))
            .FirstOrDefault(inside.Contains);
    }

    /// <summary>
    /// The label styles the moving content references.
    /// </summary>
    /// <remarks>
    /// Copied to the destination and left behind as well. A declared style nothing references is
    /// legal and harmless; a referenced style that is not declared is D022, and leaving the host's
    /// copy in place makes the reverse command a no-op rather than a second bookkeeping problem.
    /// </remarks>
    private static IReadOnlyList<BpmnLabelStyle> StylesFor(
        BpmnDiagram host,
        (IReadOnlyList<BpmnShape> Shapes, IReadOnlyList<BpmnEdge> Edges) moving)
    {
        var referenced = moving.Shapes.Select(shape => shape.Label?.StyleId)
            .Concat(moving.Edges.Select(edge => edge.Label?.StyleId))
            .Where(style => style is not null)
            .ToHashSet(StringComparer.Ordinal);

        return referenced.Count == 0
            ? []
            : [.. host.LabelStyles.Where(style => referenced.Contains(style.Id))];
    }

    private static BpmnDocument WithLabelStyles(BpmnDocument document, string diagramId, IReadOnlyList<BpmnLabelStyle> styles)
    {
        if (styles.Count == 0) return document;

        return document with
        {
            Diagrams =
            [
                .. document.Diagrams.Select(diagram =>
                {
                    if (diagram.Id != diagramId) return diagram;

                    var known = diagram.LabelStyles.Select(style => style.Id).ToHashSet(StringComparer.Ordinal);

                    // The host's own entry wins a clash: it is the one its existing labels resolve
                    // against, and replacing it would restyle content this command never touched.
                    return diagram with
                    {
                        LabelStyles = [.. diagram.LabelStyles, .. styles.Where(style => !known.Contains(style.Id))],
                    };
                }),
            ],
        };
    }

    private static BpmnDocument MergeInto(
        BpmnDocument document,
        string diagramId,
        (IReadOnlyList<BpmnShape> Shapes, IReadOnlyList<BpmnEdge> Edges) arriving,
        IReadOnlyList<BpmnLabelStyle> styles)
    {
        var merged = document with
        {
            Diagrams =
            [
                .. document.Diagrams.Select(diagram => diagram.Id != diagramId ? diagram : diagram with
                {
                    Plane = diagram.Plane with
                    {
                        Shapes = [.. diagram.Plane.Shapes, .. arriving.Shapes],
                        Edges = [.. diagram.Plane.Edges, .. arriving.Edges],
                    },
                }),
            ],
        };

        return WithLabelStyles(merged, diagramId, styles);
    }

    private static BpmnEditDiagnostic? DiagramIdTaken(BpmnDocument document, string diagramId, string planeId)
    {
        // DI ids share the document's id space but are not in its element index, so this looks at
        // the diagrams themselves rather than asking Find.
        var used = document.Diagrams
            .SelectMany(diagram => new[] { diagram.Id, diagram.Plane.Id })
            .ToHashSet(StringComparer.Ordinal);

        var clash = used.Contains(diagramId) ? diagramId : used.Contains(planeId) ? planeId : null;

        return clash is null
            ? null
            : new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, "EDIT-030", $"'{clash}' is already the id of a diagram or plane.");
    }

    private static BpmnShape Shift(BpmnShape shape, (double X, double Y) offset) => shape with
    {
        Bounds = BpmnSubProcessLayout.Shift(shape.Bounds, offset),
        Label = shape.Label is { Bounds: { } box } label
            ? label with { Bounds = BpmnSubProcessLayout.Shift(box, offset) }
            : shape.Label,
    };

    private static BpmnEdge Shift(BpmnEdge edge, (double X, double Y) offset) => edge with
    {
        Waypoints = [.. edge.Waypoints.Select(point => BpmnSubProcessLayout.Shift(point, offset))],
        Label = edge.Label is { Bounds: { } box } label
            ? label with { Bounds = BpmnSubProcessLayout.Shift(box, offset) }
            : edge.Label,
    };
}
