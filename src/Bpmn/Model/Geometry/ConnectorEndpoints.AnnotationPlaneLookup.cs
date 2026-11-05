using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Model.Geometry;

internal static partial class ConnectorEndpoints
{
    private sealed class AnnotationPlaneLookup(BpmnPlane plane)
    {
        private Dictionary<string, BpmnShape>? _shapesById;
        private Dictionary<string, BpmnShape>? _shapesByElement;
        private Dictionary<string, BpmnEdge>? _edgesById;
        private Dictionary<string, BpmnEdge>? _edgesByElement;

        public BpmnShape? ShapeById(string id)
        {
            if (_shapesById is null)
            {
                _shapesById = new(StringComparer.Ordinal);
                foreach (var shape in plane.Shapes) _shapesById.TryAdd(shape.Id, shape);
            }
            return _shapesById.GetValueOrDefault(id);
        }

        public BpmnShape? ShapeByElement(string id)
        {
            if (_shapesByElement is null)
            {
                _shapesByElement = new(StringComparer.Ordinal);
                foreach (var shape in plane.Shapes) _shapesByElement.TryAdd(shape.BpmnElement, shape);
            }
            return _shapesByElement.GetValueOrDefault(id);
        }

        public BpmnEdge? EdgeById(string id)
        {
            if (_edgesById is null)
            {
                _edgesById = new(StringComparer.Ordinal);
                foreach (var edge in plane.Edges) _edgesById.TryAdd(edge.Id, edge);
            }
            return _edgesById.GetValueOrDefault(id);
        }

        public BpmnEdge? EdgeByElement(string id)
        {
            if (_edgesByElement is null)
            {
                _edgesByElement = new(StringComparer.Ordinal);
                foreach (var edge in plane.Edges) _edgesByElement.TryAdd(edge.BpmnElement, edge);
            }
            return _edgesByElement.GetValueOrDefault(id);
        }
    }
}
