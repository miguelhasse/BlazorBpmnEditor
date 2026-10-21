using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

public static partial class BpmnDocumentEditor
{
    private static BpmnEditResult ApplyCreatePool(BpmnDocument document, CreatePoolCommand command, EditingOptions options)
    {
        if (Invalid(command.Bounds) is { } bad)
            return BpmnEditResult.Reject(document, bad);

        var diagram = command.DiagramId is null ? document.Diagrams.Count > 0 ? document.Diagrams[0] : null
            : document.Diagrams.FirstOrDefault(candidate => candidate.Id == command.DiagramId);

        if (command.DiagramId is not null && diagram is null)
            return PoolCreationRejected(document, command.Id, $"No diagram '{command.DiagramId}' exists.");

        if (document.Find(command.ProcessId) is { } element && element is not BpmnProcess)
            return PoolCreationRejected(document, command.Id, $"'{command.ProcessId}' is not a root Process.");

        if (document.Collaboration?.Participants.Any(pool => pool.ProcessRef == command.ProcessId) == true)
            return PoolCreationRejected(document, command.Id, $"'{command.ProcessId}' already has a Pool.");

        if (diagram?.Plane.BpmnElement is { } owner && owner != command.ProcessId && owner != document.Collaboration?.Id)
            return PoolCreationRejected(document, command.Id, "The Pool must be created on its Process or Collaboration diagram.");

        var collaborationId = document.Collaboration?.Id ?? $"{command.Id}_Collaboration";
        var allocated = new List<string> { command.Id, BpmnId.ForDiagramElement(command.Id) };

        if (document.Find(command.ProcessId) is null)
            allocated.Add(command.ProcessId);

        if (document.Collaboration is null)
            allocated.Add(collaborationId);

        if (diagram is null)
            allocated.AddRange([$"{command.Id}_Diagram", $"{command.Id}_Plane"]);

        if (CreationIdsProblem(document, allocated) is { } collision)
            return BpmnEditResult.Reject(document, collision);

        var prepared = document with
        {
            Processes = document.Processes.Any(process => process.Id == command.ProcessId) ? document.Processes
                : [.. document.Processes, new BpmnProcess(command.ProcessId)],
            Collaboration = document.Collaboration ?? new BpmnCollaboration(collaborationId, null, []),
        };

        if (command.ContentBounds is { } measured && Invalid(measured) is { } invalid)
            return BpmnEditResult.Reject(document, invalid);

        var bounds = AroundProcessContent(prepared, diagram, command.ProcessId, command.Bounds, command.IsHorizontal, false, command.ContentBounds);
        var participant = new BpmnParticipant(command.Id, command.Name, command.ProcessId);
        prepared = prepared with
        {
            Collaboration = prepared.Collaboration! with
            {
                Participants = [.. prepared.Collaboration!.Participants, participant],
            },
            FlowNodes = [.. prepared.FlowNodes.Select(node => RootProcessOf(prepared, node.ContainerId) == command.ProcessId
                ? node with { PoolId = command.Id } : node)],
        };
        var shape = new BpmnShape(BpmnId.ForDiagramElement(command.Id), command.Id, bounds, IsHorizontal: command.IsHorizontal);
        var diagrams = diagram is null
            ? (IReadOnlyList<BpmnDiagram>)[new BpmnDiagram($"{command.Id}_Diagram",
                new BpmnPlane($"{command.Id}_Plane", collaborationId, [shape], []))]
            : [.. prepared.Diagrams.Select(candidate => candidate.Id == diagram.Id ? candidate with
            {
                Plane = candidate.Plane with
                {
                    BpmnElement = collaborationId,
                    Shapes = [shape, .. candidate.Plane.Shapes],
                },
            } : candidate)];

        prepared = prepared with { Diagrams = diagrams };
        return options.PreventCollisions && OverlapsAnotherPool(CreationGeometry(prepared, diagram?.Id), command.Id, bounds) is { } clash
            ? BpmnEditResult.Reject(document, clash)
            : (BpmnEditResult.Applied(prepared) with { CreatedIds = [command.Id] });
    }

    private static BpmnEditResult PoolCreationRejected(BpmnDocument document, string id, string message) =>
        BpmnEditResult.Reject(document, new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, "CT-001", message, id));

    private static BpmnEditDiagnostic? CreationIdsProblem(BpmnDocument document, IEnumerable<string> allocated)
    {
        var used = document.ElementIndex.Keys.Append(document.Definitions.Id)
            .Concat(document.Diagrams.SelectMany(diagram => new[] { diagram.Id, diagram.Plane.Id }
                .Concat(diagram.Plane.Shapes.Select(shape => shape.Id))
                .Concat(diagram.Plane.Shapes.Select(shape => shape.Label?.Id).OfType<string>())
                .Concat(diagram.Plane.Edges.Select(edge => edge.Id))
                .Concat(diagram.Plane.Edges.Select(edge => edge.Label?.Id).OfType<string>())))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var id in allocated)
        {
            if (!used.Add(id))
                return new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, "EDIT-030", $"The id '{id}' is already used.", id);
        }
        return null;
    }

    private static string? RootProcessOf(BpmnDocument document, string? container)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (container is not null && visited.Add(container))
        {
            if (document.Processes.Any(process => process.Id == container)) return container;
            container = document.FindFlowNode(container)?.ContainerId;
        }
        return null;
    }

    private static Bounds AroundProcessContent(
        BpmnDocument document, BpmnDiagram? diagram, string processId, Bounds fallback, bool horizontal, bool lane,
        Bounds? measured = null)
    {
        if (diagram is null)
            return fallback;

        var ids = document.FlowNodes.Where(node => RootProcessOf(document, node.ContainerId) == processId)
            .Select(node => node.Id)
            .Concat(document.TextAnnotations.Where(item => RootProcessOf(document, item.ContainerId) == processId).Select(item => item.Id))
            .Concat(document.Groups.Where(item => RootProcessOf(document, item.ContainerId) == processId).Select(item => item.Id))
            .Concat(document.DataObjectReferences.Where(item => RootProcessOf(document, item.ContainerId) == processId).Select(item => item.Id))
            .Concat(document.DataStoreReferences.Where(item => RootProcessOf(document, item.ContainerId) == processId).Select(item => item.Id))
            .Concat(document.SequenceFlows.Where(item => RootProcessOf(document, item.ContainerId) == processId).Select(item => item.Id))
            .Concat(document.Associations.Where(item => RootProcessOf(document, item.ContainerId) == processId).Select(item => item.Id))
            .Concat(document.DataAssociations.Where(item => RootProcessOf(document, item.ContainerId) == processId).Select(item => item.Id))
            .ToHashSet(StringComparer.Ordinal);

        var boxes = diagram.Plane.Shapes.Where(shape => ids.Contains(shape.BpmnElement))
            .SelectMany(shape => shape.Label?.Bounds is { } label ? new[] { shape.Bounds, label } : [shape.Bounds])
            .Concat(diagram.Plane.Edges.Where(edge => ids.Contains(edge.BpmnElement))
                .SelectMany(edge => edge.Waypoints.Select(point => new Bounds(point.X, point.Y, 0, 0))
                    .Concat(edge.Label?.Bounds is { } label ? [label] : [])))
            .Concat(measured is { } rendered ? [rendered] : []).ToArray();

        if (boxes.Length == 0)
            return fallback;

        var content = Union(boxes);
        const double Padding = 20;
        var header = BpmnLaneLayout.HeaderSize * (lane ? 2 : 1);

        return new Bounds(content.X - Padding - (horizontal ? header : 0),
            content.Y - Padding - (horizontal ? 0 : header),
            Math.Max(fallback.Width, content.Width + (2 * Padding) + (horizontal ? header : 0)),
            Math.Max(fallback.Height, content.Height + (2 * Padding) + (horizontal ? 0 : header)));
    }
}
