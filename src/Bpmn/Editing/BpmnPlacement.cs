using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>Which side of the anchor a new element goes on.</summary>
public enum BpmnPlacementDirection
{
    /// <summary>After the anchor in reading order, the usual direction of flow.</summary>
    Right,

    /// <summary>Under the anchor.</summary>
    Below,

    /// <summary>Before the anchor.</summary>
    Left,

    /// <summary>Over the anchor.</summary>
    Above,
}

/// <summary>Where a new element would go: its bounds and the container and plane it belongs to.</summary>
/// <param name="Bounds">The free bounds found, or null when placement failed.</param>
/// <param name="ContainerId">The Process or expanded Sub-Process that will own the element.</param>
/// <param name="PoolId">The Pool the element sits in, when there is one.</param>
/// <param name="DiagramId">The diagram whose plane the bounds are on.</param>
/// <param name="Problem">Why no place was found, when <see cref="Bounds"/> is null.</param>
public sealed record BpmnPlacementResult(
    Bounds? Bounds,
    string? ContainerId,
    string? PoolId,
    string? DiagramId,
    BpmnEditDiagnostic? Problem = null)
{
    /// <summary>Whether a place was found.</summary>
    public bool Succeeded => Bounds is not null;
}

/// <summary>
/// Finds room for a new Flow Node next to an existing one or inside a container, clear of its peers.
/// </summary>
/// <remarks>
/// <para>
/// For callers that cannot point, such as agent tools: they name an anchor and a direction, and this
/// supplies the coordinates a <see cref="CreateElementCommand"/> needs. Agents are poor at coordinates,
/// and a model that guessed them would mostly produce overlaps the editor then refuses.
/// </para>
/// <para>
/// Free space is judged by the same rule the editor enforces on creation (DI-011): the new bounds must
/// not overlap a peer Flow Node in the same container, with the configured clearance. Boundary Events
/// and containers are exempt, as they are there. The search starts at the preferred spot and widens in
/// steps of the clearance plus a margin, preferring positions nearest it, so a returned place passes the
/// command's own check. A Pool or expanded Sub-Process too small for the result grows to fit when
/// <see cref="EditingOptions.AutoExpandContainers"/> is on, as it does for a drag.
/// </para>
/// </remarks>
public static class BpmnPlacement
{
    /// <summary>The space left between an anchor and the element placed next to it.</summary>
    public const double Gap = 50d;

    private const double StepMargin = 20d;
    private const int SearchRadius = 12;
    private const double PoolHeader = 30d;
    private const double OpenPlaneOrigin = 100d;

    /// <summary>The size a new element of a type is created at, matching the canvas's own defaults.</summary>
    /// <param name="type">The Flow Node type.</param>
    /// <returns>The width and height.</returns>
    public static (double Width, double Height) DefaultSize(BpmnFlowNodeType type) => type switch
    {
        BpmnFlowNodeType.StartEvent
            or BpmnFlowNodeType.EndEvent
            or BpmnFlowNodeType.IntermediateCatchEvent
            or BpmnFlowNodeType.IntermediateThrowEvent
            or BpmnFlowNodeType.BoundaryEvent => (36d, 36d),
        BpmnFlowNodeType.ExclusiveGateway
            or BpmnFlowNodeType.InclusiveGateway
            or BpmnFlowNodeType.ParallelGateway
            or BpmnFlowNodeType.ComplexGateway
            or BpmnFlowNodeType.EventBasedGateway => (50d, 50d),
        BpmnFlowNodeType.SubProcess
            or BpmnFlowNodeType.EventSubProcess
            or BpmnFlowNodeType.Transaction
            or BpmnFlowNodeType.AdHocSubProcess => (350d, 200d),
        _ => (100d, 80d),
    };

    /// <summary>Finds room for a new element beside an existing one, in the anchor's container.</summary>
    /// <param name="document">The document.</param>
    /// <param name="anchorId">The Flow Node to place beside. A Boundary Event places beside its host's container.</param>
    /// <param name="type">The type of the new element, which decides its size.</param>
    /// <param name="direction">Which side of the anchor.</param>
    /// <param name="options">Collision clearance and padding; the defaults when null.</param>
    /// <param name="diagramId">The diagram to place on; the first one drawing the anchor when null.</param>
    /// <returns>The place found, or the reason none was.</returns>
    public static BpmnPlacementResult NextTo(
        BpmnDocument document,
        string anchorId,
        BpmnFlowNodeType type,
        BpmnPlacementDirection direction = BpmnPlacementDirection.Right,
        EditingOptions? options = null,
        string? diagramId = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(anchorId);

        options ??= EditingOptions.Default;
        if (document.FindFlowNode(anchorId) is not { } anchor)
        {
            return Failed(diagramId, "PL-001", $"'{anchorId}' is not a Flow Node in this document.", anchorId);
        }

        if (ShapeOf(document, anchorId, diagramId) is not var (diagram, shape))
        {
            return Failed(diagramId, "PL-001", $"'{anchorId}' is not drawn on the diagram.", anchorId);
        }

        var containerId = anchor.ContainerId;
        var poolId = anchor.PoolId;
        if (anchor.Type == BpmnFlowNodeType.BoundaryEvent && anchor.AttachedToRef is { } hostId && document.FindFlowNode(hostId) is { } host)
        {
            containerId = host.ContainerId;
            poolId = host.PoolId;
        }

        var (width, height) = DefaultSize(type);
        var a = shape.Bounds;
        var (x, y) = direction switch
        {
            BpmnPlacementDirection.Below => (a.X + (a.Width - width) / 2, a.Y + a.Height + Gap),
            BpmnPlacementDirection.Left => (a.X - Gap - width, a.Y + (a.Height - height) / 2),
            BpmnPlacementDirection.Above => (a.X + (a.Width - width) / 2, a.Y - Gap - height),
            _ => (a.X + a.Width + Gap, a.Y + (a.Height - height) / 2),
        };

        return Search(document, diagram, containerId, poolId, new Bounds(x, y, width, height), direction, region: null, options);
    }

    /// <summary>Finds room for a new element inside a container, clear of what is already there.</summary>
    /// <param name="document">The document.</param>
    /// <param name="containerId">
    /// A Process (drawn in a Pool or on its own plane) or an expanded Sub-Process. A collapsed Sub-Process
    /// is not somewhere to create into: nothing inside it is drawn.
    /// </param>
    /// <param name="type">The type of the new element, which decides its size.</param>
    /// <param name="options">Collision clearance and padding; the defaults when null.</param>
    /// <param name="diagramId">The diagram to place on; the first suitable one when null.</param>
    /// <returns>The place found, or the reason none was.</returns>
    public static BpmnPlacementResult Inside(
        BpmnDocument document,
        string containerId,
        BpmnFlowNodeType type,
        EditingOptions? options = null,
        string? diagramId = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(containerId);

        options ??= EditingOptions.Default;
        var (width, height) = DefaultSize(type);

        if (document.FindFlowNode(containerId) is { } owner)
        {
            if (!owner.FlowElementsOwned)
            {
                return Failed(diagramId, "PL-002", $"'{containerId}' cannot contain Flow Nodes.", containerId);
            }

            if (ShapeOf(document, containerId, diagramId) is not var (diagram, shape) || shape.IsExpanded != true)
            {
                return Failed(diagramId, "PL-002", $"'{containerId}' is not drawn expanded, so nothing inside it is visible.", containerId);
            }

            var padding = options.SubProcessPadding;
            var region = Deflate(shape.Bounds, padding, padding + StepMargin, padding, padding);
            return InRegion(document, diagram, containerId, owner.PoolId, region, width, height, options);
        }

        if (document.Processes.All(process => process.Id != containerId))
        {
            return Failed(diagramId, "PL-002", $"'{containerId}' is neither a Process nor a Sub-Process.", containerId);
        }

        if (document.Collaboration?.Participants.FirstOrDefault(participant => participant.ProcessRef == containerId) is { } pool
            && ShapeOf(document, pool.Id, diagramId) is var (poolDiagram, poolShape))
        {
            var padding = options.SubProcessPadding;
            var region = poolShape.IsHorizontal == false
                ? Deflate(poolShape.Bounds, padding, PoolHeader + padding, padding, padding)
                : Deflate(poolShape.Bounds, PoolHeader + padding, padding, padding, padding);
            return InRegion(document, poolDiagram, containerId, pool.Id, region, width, height, options);
        }

        // A Process on its own plane has no edge: continue the existing content to the right.
        var plane = PlaneFor(document, containerId, diagramId);
        if (plane is null) return Failed(diagramId, "PL-002", $"'{containerId}' is not drawn on any diagram.", containerId);

        var peers = Peers(document, plane, containerId).ToList();
        var start = peers.Count == 0
            ? new Bounds(OpenPlaneOrigin, OpenPlaneOrigin, width, height)
            : RightOf(peers.MaxBy(bounds => bounds.X + bounds.Width), width, height);

        return Search(document, plane, containerId, poolId: null, start, BpmnPlacementDirection.Right, region: null, options);
    }

    private static BpmnPlacementResult InRegion(
        BpmnDocument document,
        BpmnDiagram diagram,
        string containerId,
        string? poolId,
        Bounds region,
        double width,
        double height,
        EditingOptions options)
    {
        // Reading order inside the container: left to right, then the next row down.
        var start = new Bounds(region.X, region.Y, width, height);
        var inside = Search(document, diagram, containerId, poolId, start, BpmnPlacementDirection.Right, region, options);
        if (inside.Succeeded) return inside;

        // Full: go past the right-hand edge of what is there, and let the container grow to fit.
        var peers = Peers(document, diagram, containerId).ToList();
        var beyond = peers.Count == 0 ? start : RightOf(peers.MaxBy(bounds => bounds.X + bounds.Width), width, height);
        return Search(document, diagram, containerId, poolId, beyond, BpmnPlacementDirection.Right, region: null, options);
    }

    private static BpmnPlacementResult Search(
        BpmnDocument document,
        BpmnDiagram diagram,
        string containerId,
        string? poolId,
        Bounds preferred,
        BpmnPlacementDirection direction,
        Bounds? region,
        EditingOptions options)
    {
        var clearance = options.CollisionClearance;
        var step = Math.Max(clearance, 0d) + StepMargin;
        var peers = Peers(document, diagram, containerId).Concat(Obstacles(document, diagram, containerId, poolId)).ToList();

        foreach (var (along, across) in Offsets(region is null))
        {
            var (dx, dy) = direction switch
            {
                BpmnPlacementDirection.Below => (across * step, along * step),
                BpmnPlacementDirection.Left => (-along * step, across * step),
                BpmnPlacementDirection.Above => (across * step, -along * step),
                _ => (along * step, across * step),
            };
            var candidate = preferred with { X = preferred.X + dx, Y = preferred.Y + dy };

            if (region is { } r && !Contains(r, candidate)) continue;
            if (peers.Any(peer => BpmnDocumentEditor.Overlaps(candidate, peer, clearance))) continue;

            return new BpmnPlacementResult(candidate, containerId, poolId, diagram.Id);
        }

        return Failed(diagram.Id, "DI-011", "There is no clear space near the requested position.", containerId);
    }

    // Offsets nearest the preferred spot first. Beside an anchor, only moves away from it ("along" never
    // goes backwards) so the result stays on the requested side; inside a region, a row-major scan.
    private static IEnumerable<(int Along, int Across)> Offsets(bool besideAnchor)
    {
        if (!besideAnchor)
        {
            for (var row = 0; row <= SearchRadius * 2; row++)
            {
                for (var column = 0; column <= SearchRadius * 2; column++) yield return (column, row);
            }

            yield break;
        }

        var offsets = new List<(int Along, int Across)>();
        for (var along = 0; along <= SearchRadius; along++)
        {
            for (var across = -SearchRadius; across <= SearchRadius; across++) offsets.Add((along, across));
        }

        foreach (var offset in offsets
            .OrderBy(offset => (offset.Along * offset.Along) + (offset.Across * offset.Across))
            .ThenBy(offset => Math.Abs(offset.Across))
            .ThenBy(offset => offset.Across))
        {
            yield return offset;
        }
    }

    private static IEnumerable<Bounds> Peers(BpmnDocument document, BpmnDiagram diagram, string containerId)
    {
        foreach (var shape in diagram.Plane.Shapes)
        {
            if (document.FindFlowNode(shape.BpmnElement) is { } node
                && node.ContainerId == containerId
                && BpmnDocumentEditor.ParticipatesInCollisions(node))
            {
                yield return shape.Bounds;
            }
        }
    }

    // Where a new element must not land although the editor would allow it: on a Sub-Process (the editor
    // exempts containers, because a node inside one overlaps it by design) or in another Pool. The
    // container being filled and its ancestors are not obstacles; the new element belongs inside them.
    private static IEnumerable<Bounds> Obstacles(BpmnDocument document, BpmnDiagram diagram, string containerId, string? poolId)
    {
        var enclosing = new HashSet<string>(StringComparer.Ordinal) { containerId };
        for (var owner = document.FindFlowNode(containerId); owner is not null; owner = document.FindFlowNode(owner.ContainerId))
        {
            enclosing.Add(owner.Id);
        }

        var otherPools = document.Collaboration?.Participants
            .Where(participant => participant.Id != poolId)
            .Select(participant => participant.Id)
            .ToHashSet(StringComparer.Ordinal) ?? [];

        foreach (var shape in diagram.Plane.Shapes)
        {
            if (enclosing.Contains(shape.BpmnElement)) continue;

            if (otherPools.Contains(shape.BpmnElement)
                || (document.FindFlowNode(shape.BpmnElement) is { FlowElementsOwned: true }))
            {
                yield return shape.Bounds;
            }
        }
    }

    private static (BpmnDiagram Diagram, BpmnShape Shape)? ShapeOf(BpmnDocument document, string elementId, string? diagramId)
    {
        foreach (var diagram in document.Diagrams.Where(diagram => diagramId is null || diagram.Id == diagramId))
        {
            if (diagram.Plane.Shapes.FirstOrDefault(shape => shape.BpmnElement == elementId) is { } shape) return (diagram, shape);
        }

        return null;
    }

    private static BpmnDiagram? PlaneFor(BpmnDocument document, string processId, string? diagramId) =>
        document.Diagrams.FirstOrDefault(diagram =>
            (diagramId is null || diagram.Id == diagramId)
            && (diagram.Plane.BpmnElement == processId
                || diagram.Plane.Shapes.Any(shape => document.FindFlowNode(shape.BpmnElement)?.ContainerId == processId)));

    private static Bounds RightOf(Bounds anchor, double width, double height) =>
        new(anchor.X + anchor.Width + Gap, anchor.Y + (anchor.Height - height) / 2, width, height);

    private static Bounds Deflate(Bounds bounds, double left, double top, double right, double bottom) =>
        new(bounds.X + left, bounds.Y + top, Math.Max(0d, bounds.Width - left - right), Math.Max(0d, bounds.Height - top - bottom));

    private static bool Contains(Bounds outer, Bounds inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y
        && inner.X + inner.Width <= outer.X + outer.Width
        && inner.Y + inner.Height <= outer.Y + outer.Height;

    private static BpmnPlacementResult Failed(string? diagramId, string code, string message, string elementId) =>
        new(null, null, null, diagramId, new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, code, message, elementId));
}
