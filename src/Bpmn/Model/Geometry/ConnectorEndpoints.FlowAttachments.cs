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

        var changed = false;
        var diagrams = document.Diagrams.Select(diagram =>
        {
            var oldPlane = before?.Diagrams.FirstOrDefault(candidate => candidate.Id == diagram.Id)?.Plane;
            var flows = diagram.Plane.Edges
                .Where(edge => document.Find(edge.BpmnElement) is BpmnSequenceFlow or BpmnMessageFlow)
                .GroupBy(edge => edge.BpmnElement, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var planeChanged = false;
            var movedAnnotations = new Dictionary<string, BpmnShape>(StringComparer.Ordinal);
            var edges = diagram.Plane.Edges.Select(edge =>
            {
                if (document.Find(edge.BpmnElement) is not BpmnAssociation association || edge.Waypoints.Count < 2) return edge;
                var sourceIsFlow = flows.TryGetValue(association.SourceRef, out var sourceFlow);
                var targetIsFlow = flows.TryGetValue(association.TargetRef, out var targetFlow);
                if (sourceIsFlow == targetIsFlow) return edge;

                var flow = sourceIsFlow ? sourceFlow! : targetFlow!;
                var annotationId = sourceIsFlow ? association.TargetRef : association.SourceRef;
                if (document.Find(annotationId) is not BpmnTextAnnotation) return edge;
                var annotationShape = diagram.Plane.Shapes.FirstOrDefault(shape => shape.BpmnElement == annotationId);
                if (annotationShape is null || flow.Waypoints.Count < 2) return edge;

                var route = edge.Waypoints.ToArray();
                var flowEnd = sourceIsFlow ? 0 : route.Length - 1;
                var annotationEnd = sourceIsFlow ? route.Length - 1 : 0;
                var oldEdge = oldPlane?.Edges.FirstOrDefault(candidate => candidate.BpmnElement == edge.BpmnElement);
                var oldFlow = oldPlane?.Edges.FirstOrDefault(candidate => candidate.BpmnElement == flow.BpmnElement);
                var oldAssociation = before?.Find(association.Id) as BpmnAssociation;
                var follows = oldEdge is { Waypoints.Count: >= 2 } && oldFlow is { Waypoints.Count: >= 2 }
                    && oldAssociation?.SourceRef == association.SourceRef && oldAssociation.TargetRef == association.TargetRef
                    && !oldFlow.Waypoints.SequenceEqual(flow.Waypoints);
                var path = RoundedFlowPath.Flatten(flow.Waypoints);
                var oldPath = follows ? RoundedFlowPath.Flatten(oldFlow!.Waypoints) : null;
                var ratio = follows
                    ? ClosestRatio(oldPath!, sourceIsFlow ? oldEdge!.Waypoints[0] : oldEdge!.Waypoints[^1])
                    : ClosestRatio(path, route[flowEnd]);
                var point = PointAtRatio(path, ratio);
                var oldAnnotation = oldPlane?.Shapes.FirstOrDefault(shape => shape.Id == annotationShape.Id);
                if (follows && oldAnnotation?.Bounds == annotationShape.Bounds
                    && positionedAnnotations?.Contains(annotationShape.Id) != true
                    && BpmnAnnotationAttachments.UniqueConnection(document, annotationId)?.Id == association.Id)
                {
                    var oldPoint = PointAtRatio(oldPath!, ratio);
                    var delta = new DiPoint(point.X - oldPoint.X, point.Y - oldPoint.Y);
                    if (Math.Abs(delta.X) > 0.01 || Math.Abs(delta.Y) > 0.01)
                    {
                        annotationShape = TranslateAnnotation(annotationShape, delta);
                        movedAnnotations[annotationId] = annotationShape;
                        route[annotationEnd] = TranslatePoint(route[annotationEnd], delta);
                        planeChanged = true;
                    }
                }
                if (Distance(point, route[flowEnd]) > 0.01) route[flowEnd] = point;
                route[annotationEnd] = BpmnOutline.Normalize(
                    route[annotationEnd], BpmnOutline.Rectangle(annotationShape.Bounds), route[flowEnd]);
                if (route.SequenceEqual(edge.Waypoints)) return edge;

                moved?.Add(new MovedConnector(edge.BpmnElement,
                    Math.Max(Distance(route[0], edge.Waypoints[0]), Distance(route[^1], edge.Waypoints[^1]))));
                planeChanged = true;
                return edge with { Waypoints = follows ? [route[0], route[^1]] : route };
            }).ToArray();
            changed |= planeChanged;
            return planeChanged ? diagram with
            {
                Plane = diagram.Plane with
                {
                    Edges = edges,
                    Shapes = [.. diagram.Plane.Shapes.Select(shape =>
                        movedAnnotations.TryGetValue(shape.BpmnElement, out var movedShape) ? movedShape : shape)],
                },
            } : diagram;
        }).ToArray();
        return changed ? document with { Diagrams = diagrams } : document;
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
        var total = route.Zip(route.Skip(1), Distance).Sum();
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
