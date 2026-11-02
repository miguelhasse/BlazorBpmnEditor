using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

// One selection read only. Session documents/DI collections are immutable; host callbacks may replace the document.
internal sealed class BpmnInspectionGeometry
{
    private BpmnDocument? _document;
    private Dictionary<string, BpmnShape>? _shapes;
    private Dictionary<string, BpmnEdge>? _edges;
    private string? _diagramId;
    private Dictionary<string, BpmnShape>? _activeShapes;
    private Dictionary<string, BpmnEdge>? _activeEdges;

    public (BpmnShape? Shape, BpmnEdge? Edge) Global(BpmnDocument document, string elementId)
    {
        EnsureDocument(document);
        if (_shapes is null)
        {
            _shapes = new(StringComparer.Ordinal);
            _edges = new(StringComparer.Ordinal);
            foreach (var diagram in document.Diagrams)
            {
                foreach (var shape in diagram.Plane.Shapes)
                    if (shape.BpmnElement is { } shapeId) _shapes.TryAdd(shapeId, shape);
                foreach (var edge in diagram.Plane.Edges)
                    if (edge.BpmnElement is { } edgeId) _edges.TryAdd(edgeId, edge);
            }
        }
        return (_shapes.GetValueOrDefault(elementId), _edges!.GetValueOrDefault(elementId));
    }

    public (BpmnShape? Shape, BpmnEdge? Edge) Active(BpmnDocument document, string diagramId, string elementId)
    {
        EnsureDocument(document);
        if (_activeShapes is null || _diagramId != diagramId)
        {
            _diagramId = diagramId;
            _activeShapes = new(StringComparer.Ordinal);
            _activeEdges = new(StringComparer.Ordinal);
            var plane = document.Diagrams.FirstOrDefault(diagram => diagram.Id == diagramId)?.Plane;
            if (plane is not null)
            {
                foreach (var shape in plane.Shapes)
                    if (shape.BpmnElement is { } shapeId) _activeShapes.TryAdd(shapeId, shape);
                foreach (var edge in plane.Edges)
                    if (edge.BpmnElement is { } edgeId) _activeEdges.TryAdd(edgeId, edge);
            }
        }
        return (_activeShapes.GetValueOrDefault(elementId), _activeEdges!.GetValueOrDefault(elementId));
    }

    private void EnsureDocument(BpmnDocument document)
    {
        if (ReferenceEquals(_document, document)) return;
        _document = document;
        _shapes = null;
        _edges = null;
        _diagramId = null;
        _activeShapes = null;
        _activeEdges = null;
    }
}
