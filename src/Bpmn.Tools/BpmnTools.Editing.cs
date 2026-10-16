using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools;

public static partial class BpmnTools
{
    // Extension values are addressed by namespace path and edited through their own commands; the
    // tools edit standard properties only.
    private const string ExtensionPrefix = "extension:";

    /// <summary>
    /// Creates a Flow Node beside an anchor or inside a container, placed clear of its neighbours, and
    /// optionally connects the anchor to it in the same single edit.
    /// </summary>
    /// <param name="host">The document.</param>
    /// <param name="request">What to create and where.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The new element. Refusals: <see cref="BpmnToolCodes.InvalidArgument"/>, placement codes
    /// (<c>PL-001</c>, <c>PL-002</c>, <c>DI-011</c>), <see cref="BpmnToolCodes.IllegalConnection"/>, or the
    /// editor's own. A refusal creates nothing — not the element, and not the connector.
    /// </returns>
    public static async ValueTask<BpmnToolResult<BpmnCreatedElement>> CreateElementAsync(
        IBpmnToolHost host,
        BpmnCreateElementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(request);
        if (await host.GetDocumentAsync(cancellationToken).ConfigureAwait(false) is not { } document) return NoDocument<BpmnCreatedElement>(host);
        if (host.IsReadOnly) return ReadOnly<BpmnCreatedElement>(host);

        if (request.Type is null || !FlowNodeTypes.TryGetValue(request.Type, out var type))
        {
            return Failed<BpmnCreatedElement>(host, BpmnToolCodes.InvalidArgument, "The type is not a BPMN Flow Node type.");
        }

        if (type == BpmnFlowNodeType.BoundaryEvent)
        {
            return Failed<BpmnCreatedElement>(host, BpmnToolCodes.InvalidArgument, "A boundary event is attached to an activity, not placed; this tool cannot create one.");
        }

        var eventDefinition = BpmnEventDefinitionType.None;
        if (request.EventDefinition is not null
            && (!EventDefinitions.TryGetValue(request.EventDefinition, out eventDefinition)
                || BpmnElementInspector.CategoryOf(type) != BpmnElementCategory.Event))
        {
            return Failed<BpmnCreatedElement>(host, BpmnToolCodes.InvalidArgument, "The event definition is unknown, or the type is not an event.");
        }

        if (request.Name?.Length > host.Limits.MaxTextLength) return TooLong<BpmnCreatedElement>(host);
        if (request.ConnectFromAnchor && request.AnchorId is null)
        {
            return Failed<BpmnCreatedElement>(host, BpmnToolCodes.InvalidArgument, "Connecting needs an anchor to connect from.");
        }

        BpmnPlacementResult placed;
        if (request.AnchorId is not null)
        {
            if (!TryDirection(request.Direction, out var direction))
            {
                return Failed<BpmnCreatedElement>(host, BpmnToolCodes.InvalidArgument, "The direction must be 'right', 'below', 'left' or 'above'.");
            }

            // The active diagram is a preference: get_diagram lists every diagram's elements, so the anchor
            // may be drawn only on another one.
            var anchorDiagram = DiagramDrawing(document, request.AnchorId, host.ActiveDiagramId);
            placed = BpmnPlacement.NextTo(document, request.AnchorId, type, direction, host.Editing, anchorDiagram);
        }
        else
        {
            var containerId = request.ContainerId ?? DefaultContainer(document, host.ActiveDiagramId);
            if (containerId is null) return Failed<BpmnCreatedElement>(host, BpmnToolCodes.NotFound, "The document has no process to create into.");

            placed = BpmnPlacement.Inside(document, containerId, type, host.Editing, host.ActiveDiagramId);
        }

        if (!placed.Succeeded)
        {
            var problem = placed.Problem!;
            return Failed<BpmnCreatedElement>(host, problem.Code, "No place could be found for the element.", [ToolDiagnostic(problem, host.Limits)]);
        }

        var elementId = BpmnId.New(BpmnElementInspector.WireNameOf(type));
        var bounds = placed.Bounds!.Value;
        BpmnEditCommand command = new CreateElementCommand(elementId, type, placed.ContainerId!, bounds, request.Name, eventDefinition, PoolId: placed.PoolId)
        {
            DiagramId = placed.DiagramId,
            Origin = host.Origin,
        };

        string? connectorId = null;
        IReadOnlyList<BpmnToolDiagnostic> warnings = [];
        if (request.ConnectFromAnchor)
        {
            // Judge the connector against the document as it would be, so a refusal creates nothing.
            var scratch = BpmnDocumentEditor.Apply(document, command, host.Editing);
            if (!scratch.IsApplied) return Refused<BpmnCreatedElement>(host, scratch);

            var kind = BpmnConnectionRules.InferKind(scratch.Document, request.AnchorId!, elementId) ?? BpmnConnectorKind.SequenceFlow;
            var check = BpmnConnectionRules.Check(scratch.Document, kind, request.AnchorId!, elementId);
            if (!check.IsValid) return IllegalConnection<BpmnCreatedElement>(host, check);

            warnings = [.. check.Violations.Select(violation => ToolDiagnostic(violation, host.Limits))];
            connectorId = BpmnId.New(ConnectorPrefix(kind));
            var connector = new CreateConnectorCommand(
                connectorId,
                kind,
                request.AnchorId!,
                elementId,
                Centres(scratch.Document, request.AnchorId!, elementId, placed.DiagramId) ?? [])
            {
                DiagramId = placed.DiagramId,
                Origin = host.Origin,
            };

            command = new CompoundCommand([command, connector], "Agent: bpmn_create_element") { Origin = host.Origin };
        }

        var result = await host.ApplyAsync(command, cancellationToken).ConfigureAwait(false);
        if (!result.IsApplied) return Refused<BpmnCreatedElement>(host, result);

        return Applied(
            host,
            result,
            new BpmnCreatedElement(elementId, placed.ContainerId!, ToolBounds(bounds)!, connectorId),
            warnings);
    }

    /// <summary>
    /// Connects two elements, refusing a connection the BPMN rules forbid exactly as the canvas refuses it
    /// during a drag.
    /// </summary>
    /// <param name="host">The document.</param>
    /// <param name="request">What to connect.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The new connector, or <see cref="BpmnToolCodes.IllegalConnection"/> with the violated rule ids, or
    /// <see cref="BpmnToolCodes.NotFound"/> when an end is missing or the two are not drawn on one diagram.
    /// </returns>
    public static async ValueTask<BpmnToolResult<BpmnCreatedConnector>> ConnectAsync(
        IBpmnToolHost host,
        BpmnConnectRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(request);
        if (await host.GetDocumentAsync(cancellationToken).ConfigureAwait(false) is not { } document) return NoDocument<BpmnCreatedConnector>(host);
        if (host.IsReadOnly) return ReadOnly<BpmnCreatedConnector>(host);

        if (string.IsNullOrEmpty(request.SourceId) || string.IsNullOrEmpty(request.TargetId)
            || BpmnConnectionRules.EndpointOf(document, request.SourceId) is not { } source
            || BpmnConnectionRules.EndpointOf(document, request.TargetId) is not { } target)
        {
            return Failed<BpmnCreatedConnector>(host, BpmnToolCodes.NotFound, "Both ends must be elements a connector can attach to.");
        }

        BpmnConnectorKind kind;
        if (request.Kind is null) kind = BpmnConnectionRules.InferKind(source, target);
        else if (!ConnectorKinds.TryGetValue(request.Kind, out kind))
        {
            return Failed<BpmnCreatedConnector>(host, BpmnToolCodes.InvalidArgument, "The kind must be 'sequenceFlow', 'messageFlow', 'association' or 'dataAssociation'.");
        }

        if (request.Name?.Length > host.Limits.MaxTextLength) return TooLong<BpmnCreatedConnector>(host);

        var check = BpmnConnectionRules.Check(kind, source, target);
        if (!check.IsValid) return IllegalConnection<BpmnCreatedConnector>(host, check);

        var diagramId = SharedDiagram(document, request.SourceId, request.TargetId, host.ActiveDiagramId);
        if (diagramId is null || Centres(document, request.SourceId, request.TargetId, diagramId) is not { } waypoints)
        {
            return Failed<BpmnCreatedConnector>(host, BpmnToolCodes.NotFound, "The two elements are not drawn on one diagram.");
        }

        var connectorId = BpmnId.New(ConnectorPrefix(kind));
        BpmnEditCommand command = new CreateConnectorCommand(connectorId, kind, request.SourceId, request.TargetId, waypoints)
        {
            DiagramId = diagramId,
            Origin = host.Origin,
        };

        if (request.Name is not null)
        {
            command = new CompoundCommand(
                [command, new UpdatePropertiesCommand(connectorId, "name", request.Name) { Origin = host.Origin }],
                "Agent: bpmn_connect")
            { Origin = host.Origin };
        }

        var result = await host.ApplyAsync(command, cancellationToken).ConfigureAwait(false);
        if (!result.IsApplied) return Refused<BpmnCreatedConnector>(host, result);

        return Applied(
            host,
            result,
            new BpmnCreatedConnector(connectorId, Wire(kind)),
            [.. check.Violations.Select(violation => ToolDiagnostic(violation, host.Limits))]);
    }

    /// <summary>Sets one standard property of an element, such as its name or documentation.</summary>
    /// <param name="host">The document.</param>
    /// <param name="request">Which property, and the value.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result; the editor refuses a property the element does not have (<c>EDIT-070</c>) or a bad value (<c>EDIT-071</c>).</returns>
    public static async ValueTask<BpmnToolResult> SetPropertyAsync(
        IBpmnToolHost host,
        BpmnSetPropertyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(request);
        if (await host.GetDocumentAsync(cancellationToken).ConfigureAwait(false) is not { } document) return NoDocument<object>(host);
        if (host.IsReadOnly) return ReadOnly<object>(host);

        if (string.IsNullOrEmpty(request.ElementId) || document.Find(request.ElementId) is null)
        {
            return Failed<object>(host, BpmnToolCodes.NotFound, "There is no element with that id.");
        }

        if (string.IsNullOrEmpty(request.Name) || request.Name.StartsWith(ExtensionPrefix, StringComparison.Ordinal))
        {
            return Failed<object>(host, BpmnToolCodes.InvalidArgument, "Only standard properties can be set with this tool.");
        }

        if (request.Value?.Length > host.Limits.MaxTextLength) return TooLong<object>(host);

        var result = await host.ApplyAsync(
            new UpdatePropertiesCommand(request.ElementId, request.Name, request.Value) { Origin = host.Origin },
            cancellationToken).ConfigureAwait(false);

        return result.IsApplied ? Applied<object>(host, result, data: null, []) : Refused<object>(host, result);
    }

    /// <summary>
    /// Deletes elements, and everything that cannot outlive them, which the result lists. An adapter that
    /// can ask the user does so before calling this.
    /// </summary>
    /// <param name="host">The document.</param>
    /// <param name="request">What to delete.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Every element that went, the ones asked for and the ones that went with them.</returns>
    public static async ValueTask<BpmnToolResult<BpmnDeletedElements>> DeleteAsync(
        IBpmnToolHost host,
        BpmnDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(request);
        if (await host.GetDocumentAsync(cancellationToken).ConfigureAwait(false) is not { } document) return NoDocument<BpmnDeletedElements>(host);
        if (host.IsReadOnly) return ReadOnly<BpmnDeletedElements>(host);

        if (request.ElementIds is null || request.ElementIds.Count == 0)
        {
            return Failed<BpmnDeletedElements>(host, BpmnToolCodes.InvalidArgument, "Name at least one element to delete.");
        }

        if (request.ElementIds.Count > host.Limits.MaxBatch)
        {
            return Failed<BpmnDeletedElements>(host, BpmnToolCodes.TooLarge, "That is more elements than one call may delete.");
        }

        var result = await host.ApplyAsync(new DeleteElementsCommand(request.ElementIds) { Origin = host.Origin }, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsApplied) return Refused<BpmnDeletedElements>(host, result);

        // The editor reports how many further elements went; an agent needs to know which.
        var remaining = result.Document.ElementIndex;
        var deleted = document.ElementIndex.Keys
            .Where(id => !remaining.ContainsKey(id) && !IsDiagramElement(document, id))
            .Order(StringComparer.Ordinal)
            .ToList();

        return Applied(host, result, new BpmnDeletedElements(deleted), []);
    }

    // --- editing helpers -------------------------------------------------------------------------

    // Shapes and edges go with their elements; they are drawing, not content, and are not listed.
    private static bool IsDiagramElement(BpmnDocument document, string id) =>
        document.Diagrams.Any(diagram => diagram.Id == id || diagram.Plane.Id == id
            || diagram.Plane.Shapes.Any(shape => shape.Id == id)
            || diagram.Plane.Edges.Any(edge => edge.Id == id));

    private static bool TryDirection(string? name, out BpmnPlacementDirection direction)
    {
        direction = name switch
        {
            null or "right" => BpmnPlacementDirection.Right,
            "below" => BpmnPlacementDirection.Below,
            "left" => BpmnPlacementDirection.Left,
            "above" => BpmnPlacementDirection.Above,
            _ => (BpmnPlacementDirection)(-1),
        };

        return Enum.IsDefined(direction);
    }

    private static string ConnectorPrefix(BpmnConnectorKind kind) => kind switch
    {
        BpmnConnectorKind.MessageFlow => "MessageFlow",
        BpmnConnectorKind.Association => "Association",
        BpmnConnectorKind.DataAssociation => "DataAssociation",
        _ => "Flow",
    };

    // A diagram drawing the element, preferring the active one; null when none does.
    private static string? DiagramDrawing(BpmnDocument document, string elementId, string? activeDiagramId) =>
        document.Diagrams
            .OrderBy(diagram => diagram.Id == activeDiagramId ? 0 : 1)
            .FirstOrDefault(diagram => diagram.Plane.Shapes.Any(shape => shape.BpmnElement == elementId))
            ?.Id;

    // A diagram drawing both ends, preferring the active one.
    private static string? SharedDiagram(BpmnDocument document, string sourceId, string targetId, string? activeDiagramId) =>
        document.Diagrams
            .OrderBy(diagram => diagram.Id == activeDiagramId ? 0 : 1)
            .FirstOrDefault(diagram =>
                diagram.Plane.Shapes.Any(shape => shape.BpmnElement == sourceId)
                && diagram.Plane.Shapes.Any(shape => shape.BpmnElement == targetId))
            ?.Id;

    // The two shape centres. The editor projects them onto the shapes' outlines when it applies the
    // command, exactly as it does for a connector drawn on the canvas.
    private static IReadOnlyList<DiPoint>? Centres(BpmnDocument document, string sourceId, string targetId, string? diagramId)
    {
        var plane = document.Diagrams.FirstOrDefault(diagram => diagramId is null || diagram.Id == diagramId)?.Plane;
        var source = plane?.Shapes.FirstOrDefault(shape => shape.BpmnElement == sourceId)?.Bounds;
        var target = plane?.Shapes.FirstOrDefault(shape => shape.BpmnElement == targetId)?.Bounds;
        if (source is not { } s || target is not { } t) return null;

        return [new DiPoint(s.X + (s.Width / 2), s.Y + (s.Height / 2)), new DiPoint(t.X + (t.Width / 2), t.Y + (t.Height / 2))];
    }

    private static BpmnToolResult<TData> Applied<TData>(
        IBpmnToolHost host,
        BpmnEditResult result,
        TData? data,
        IReadOnlyList<BpmnToolDiagnostic> extra) => new()
        {
            Ok = true,
            Applied = true,
            Data = data,
            CreatedIds = result.CreatedIds,
            Diagnostics = [.. result.Diagnostics.Select(finding => ToolDiagnostic(finding, host.Limits)), .. extra],
            Revision = host.Revision,
            IsDirty = DirtyOf(host),
        };

    private static BpmnToolResult<TData> Refused<TData>(IBpmnToolHost host, BpmnEditResult result)
    {
        var code = result.Rejections.FirstOrDefault()?.Code ?? BpmnToolCodes.Refused;
        return Failed<TData>(
            host,
            code,
            "The editor refused the edit; nothing was changed.",
            [.. result.Diagnostics.Select(finding => ToolDiagnostic(finding, host.Limits))]);
    }

    private static BpmnToolResult<TData> IllegalConnection<TData>(IBpmnToolHost host, BpmnConnectionCheck check) =>
        Failed<TData>(
            host,
            BpmnToolCodes.IllegalConnection,
            "BPMN does not allow this connection; nothing was changed.",
            [.. check.Violations.Select(violation => ToolDiagnostic(violation, host.Limits))]);

    private static BpmnToolResult<TData> ReadOnly<TData>(IBpmnToolHost host) =>
        Failed<TData>(host, BpmnToolCodes.ReadOnly, "The document is read-only.");

    private static BpmnToolResult<TData> TooLong<TData>(IBpmnToolHost host) =>
        Failed<TData>(host, BpmnToolCodes.InvalidArgument, "The text is longer than this call accepts.");
}
