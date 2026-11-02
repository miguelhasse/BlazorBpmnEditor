using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Model.Geometry;

internal static partial class ConnectorEndpoints
{
    /// <summary>
    /// Reconstructs flow attachments from DI, preserving their path-length ratio when a flow changes.
    /// </summary>
    internal static BpmnDocument FollowFlowAttachments(
        BpmnDocument document, BpmnDocument? before = null, ICollection<MovedConnector>? moved = null,
        ISet<string>? positionedAnnotations = null)
    {
        if (document.Associations.Count == 0) return document;

        BpmnDiagram[]? diagrams = null;
        for (var diagramIndex = 0; diagramIndex < document.Diagrams.Count; diagramIndex++)
        {
            var diagram = document.Diagrams[diagramIndex];
            var oldPlane = before?.Diagrams.FirstOrDefault(candidate => candidate.Id == diagram.Id)?.Plane;
            var flows = new Dictionary<string, BpmnEdge>(StringComparer.Ordinal);
            foreach (var edge in diagram.Plane.Edges)
                if (document.Find(edge.BpmnElement) is BpmnSequenceFlow or BpmnMessageFlow)
                    flows.TryAdd(edge.BpmnElement, edge);
            if (flows.Count == 0) continue;
            var shapes = new Dictionary<string, BpmnShape>(StringComparer.Ordinal);
            foreach (var shape in diagram.Plane.Shapes) shapes.TryAdd(shape.BpmnElement, shape);
            var oldEdges = new Dictionary<string, BpmnEdge>(StringComparer.Ordinal);
            var oldShapes = new Dictionary<string, BpmnShape>(StringComparer.Ordinal);
            if (oldPlane is not null)
            {
                foreach (var edge in oldPlane.Edges) oldEdges.TryAdd(edge.BpmnElement, edge);
                foreach (var shape in oldPlane.Shapes) oldShapes.TryAdd(shape.Id, shape);
            }
            Dictionary<string, BpmnShape>? movedAnnotations = null;
            BpmnEdge[]? edges = null;
            for (var edgeIndex = 0; edgeIndex < diagram.Plane.Edges.Count; edgeIndex++)
            {
                var edge = diagram.Plane.Edges[edgeIndex];
                var followed = FollowEdge(edge);
                if (ReferenceEquals(followed, edge)) continue;
                edges ??= [.. diagram.Plane.Edges];
                edges[edgeIndex] = followed;
            }
            if (edges is null && movedAnnotations is null) continue;
            diagrams ??= [.. document.Diagrams];
            diagrams[diagramIndex] = diagram with
            {
                Plane = diagram.Plane with
                {
                    Edges = edges ?? diagram.Plane.Edges,
                    Shapes = movedAnnotations is null ? diagram.Plane.Shapes : [.. diagram.Plane.Shapes.Select(shape =>
                        movedAnnotations.TryGetValue(shape.BpmnElement, out var movedShape) ? movedShape : shape)],
                },
            };

            BpmnEdge FollowEdge(BpmnEdge edge)
            {
                if (document.Find(edge.BpmnElement) is not BpmnAssociation association || edge.Waypoints.Count < 2) return edge;
                var sourceIsFlow = flows.TryGetValue(association.SourceRef, out var sourceFlow);
                var targetIsFlow = flows.TryGetValue(association.TargetRef, out var targetFlow);
                if (sourceIsFlow == targetIsFlow) return edge;

                var flow = sourceIsFlow ? sourceFlow! : targetFlow!;
                var annotationId = sourceIsFlow ? association.TargetRef : association.SourceRef;
                if (document.Find(annotationId) is not BpmnTextAnnotation) return edge;
                var annotationShape = shapes.GetValueOrDefault(annotationId);
                if (annotationShape is null || flow.Waypoints.Count < 2) return edge;

                var flowPoint = sourceIsFlow ? edge.Waypoints[0] : edge.Waypoints[^1];
                var annotationPoint = sourceIsFlow ? edge.Waypoints[^1] : edge.Waypoints[0];
                var oldEdge = oldEdges.GetValueOrDefault(edge.BpmnElement);
                var oldFlow = oldEdges.GetValueOrDefault(flow.BpmnElement);
                var oldAssociation = before?.Find(association.Id) as BpmnAssociation;
                var follows = oldEdge is { Waypoints.Count: >= 2 } && oldFlow is { Waypoints.Count: >= 2 }
                    && oldAssociation?.SourceRef == association.SourceRef && oldAssociation.TargetRef == association.TargetRef
                    && !oldFlow.Waypoints.SequenceEqual(flow.Waypoints);
                var path = RoundedFlowPath.Flatten(flow.Waypoints);
                var oldPath = follows ? RoundedFlowPath.Flatten(oldFlow!.Waypoints) : null;
                var ratio = follows
                    ? ClosestRatio(oldPath!, sourceIsFlow ? oldEdge!.Waypoints[0] : oldEdge!.Waypoints[^1])
                    : ClosestRatio(path, flowPoint);
                var point = PointAtRatio(path, ratio);
                var oldAnnotation = oldShapes.GetValueOrDefault(annotationShape.Id);
                if (follows && oldAnnotation?.Bounds == annotationShape.Bounds
                    && positionedAnnotations?.Contains(annotationShape.Id) != true
                    && BpmnAnnotationAttachments.UniqueConnection(document, annotationId)?.Id == association.Id)
                {
                    var oldPoint = PointAtRatio(oldPath!, ratio);
                    var delta = new DiPoint(point.X - oldPoint.X, point.Y - oldPoint.Y);
                    if (Math.Abs(delta.X) > 0.01 || Math.Abs(delta.Y) > 0.01)
                    {
                        annotationShape = TranslateAnnotation(annotationShape, delta);
                        movedAnnotations ??= new(StringComparer.Ordinal);
                        movedAnnotations[annotationId] = annotationShape;
                        annotationPoint = TranslatePoint(annotationPoint, delta);
                    }
                }
                if (Distance(point, flowPoint) > 0.01) flowPoint = point;
                annotationPoint = BpmnOutline.Normalize(
                    annotationPoint, BpmnOutline.Rectangle(annotationShape.Bounds), flowPoint);
                var first = sourceIsFlow ? flowPoint : annotationPoint;
                var last = sourceIsFlow ? annotationPoint : flowPoint;
                if (first == edge.Waypoints[0] && last == edge.Waypoints[^1]) return edge;

                moved?.Add(new MovedConnector(edge.BpmnElement,
                    Math.Max(Distance(first, edge.Waypoints[0]), Distance(last, edge.Waypoints[^1]))));
                if (follows) return edge with { Waypoints = [first, last] };
                var route = edge.Waypoints.ToArray();
                route[0] = first;
                route[^1] = last;
                return edge with { Waypoints = route };
            }
        }
        return diagrams is null ? document : document with { Diagrams = diagrams };
    }

    private static double ClosestRatio(IReadOnlyList<DiPoint> route, DiPoint point)
    {
        double length = 0;
        double closestLength = 0;
        var best = double.PositiveInfinity;
        for (var index = 1; index < route.Count; index++)
        {
            var start = route[index - 1];
            var end = route[index];
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var squaredLength = (dx * dx) + (dy * dy);
            var fraction = squaredLength == 0 ? 0 : Math.Clamp(
                (((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / squaredLength, 0, 1);
            var distance = Distance(point, new DiPoint(start.X + (fraction * dx), start.Y + (fraction * dy)));
            var segmentLength = Math.Sqrt(squaredLength);
            if (distance < best)
            {
                best = distance;
                closestLength = length + (fraction * segmentLength);
            }
            length += segmentLength;
        }
        return length == 0 ? 0 : closestLength / length;
    }

    private static DiPoint PointAtRatio(IReadOnlyList<DiPoint> route, double ratio)
    {
        double total = 0;
        for (var index = 1; index < route.Count; index++) total += Distance(route[index - 1], route[index]);
        var remaining = total * ratio;
        for (var index = 1; index < route.Count; index++)
        {
            var start = route[index - 1];
            var end = route[index];
            var length = Distance(start, end);
            if (length > 0 && remaining <= length)
            {
                var fraction = remaining / length;
                return new DiPoint(start.X + ((end.X - start.X) * fraction), start.Y + ((end.Y - start.Y) * fraction));
            }
            remaining -= length;
        }
        return route[^1];
    }
}
