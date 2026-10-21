using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Model.Geometry;

internal static partial class ConnectorEndpoints
{
    internal static BpmnDocument FollowAnnotationShapes(BpmnDocument before, BpmnDocument after, ISet<string>? positioned = null)
    {
        var diagrams = after.Diagrams.Select(diagram =>
            {
                var oldPlane = before.Diagrams.FirstOrDefault(candidate => candidate.Id == diagram.Id)?.Plane;
                if (oldPlane is null) return diagram;
                var shapes = diagram.Plane.Shapes.Select(shape =>
                {
                    if (after.Find(shape.BpmnElement) is not BpmnTextAnnotation
                        || BpmnAnnotationAttachments.UniqueConnection(after, shape.BpmnElement) is not { } connection
                        || before.Find(connection.Id) is not BpmnAssociation oldConnection
                        || oldConnection.SourceRef != connection.SourceRef || oldConnection.TargetRef != connection.TargetRef)
                    {
                        return shape;
                    }

                    var oldShape = oldPlane.Shapes.FirstOrDefault(candidate => candidate.Id == shape.Id);
                    if (oldShape is null) return shape;
                    if (oldShape.Bounds != shape.Bounds)
                    {
                        var noteDelta = new DiPoint(shape.Bounds.X - oldShape.Bounds.X, shape.Bounds.Y - oldShape.Bounds.Y);
                        return Equals(shape.Label, oldShape.Label)
                            ? shape with { Label = TranslateAnnotation(oldShape, noteDelta).Label } : shape;
                    }
                    // Explicit movement (including a deliberately unchanged note) wins.
                    if (positioned?.Contains(shape.Id) == true) return shape;
                    var targetId = BpmnAnnotationAttachments.OtherEnd(connection, shape.BpmnElement);
                    var oldTarget = oldPlane.Shapes.FirstOrDefault(candidate => candidate.BpmnElement == targetId);
                    var target = diagram.Plane.Shapes.FirstOrDefault(candidate => candidate.BpmnElement == targetId);
                    if (oldTarget is null || target is null
                        || SizeChanged(oldTarget.Bounds, target.Bounds)) return shape;
                    var delta = new DiPoint(target.Bounds.X - oldTarget.Bounds.X, target.Bounds.Y - oldTarget.Bounds.Y);
                    if (Math.Abs(delta.X) < 0.01 && Math.Abs(delta.Y) < 0.01) return shape;
                    return TranslateAnnotation(shape, delta);
                }).ToArray();

                var edges = diagram.Plane.Edges.Select(edge =>
                {
                    if (after.Find(edge.BpmnElement) is not BpmnAssociation association || edge.Waypoints.Count < 2) return edge;
                    var annotationId = after.Find(association.SourceRef) is BpmnTextAnnotation ? association.SourceRef : association.TargetRef;
                    if (BpmnAnnotationAttachments.UniqueConnection(after, annotationId)?.Id != association.Id) return edge;
                    var oldEdge = oldPlane.Edges.FirstOrDefault(candidate => candidate.Id == edge.Id);
                    if (oldEdge is not { Waypoints.Count: >= 2 }) return edge;
                    var first = FollowTranslation(association.SourceRef, edge.Waypoints[0], oldEdge.Waypoints[0]);
                    var last = FollowTranslation(association.TargetRef, edge.Waypoints[^1], oldEdge.Waypoints[^1]);
                    if (first == edge.Waypoints[0] && last == edge.Waypoints[^1]) return edge;
                    return edge with { Waypoints = [first, last] };
                }).ToArray();

                return shapes.SequenceEqual(diagram.Plane.Shapes) && edges.SequenceEqual(diagram.Plane.Edges) ? diagram : diagram with
                {
                    Plane = diagram.Plane with
                    {
                        Shapes = shapes,
                        Edges = edges,
                    },
                };

                DiPoint FollowTranslation(string id, DiPoint point, DiPoint oldPoint)
                {
                    if (point != oldPoint) return point;
                    var oldShape = oldPlane.Shapes.FirstOrDefault(shape => shape.BpmnElement == id);
                    var shape = shapes.FirstOrDefault(candidate => candidate.BpmnElement == id);
                    if (shape is null || oldShape is null || SizeChanged(oldShape.Bounds, shape.Bounds)) return point;
                    return TranslatePoint(point, new DiPoint(shape.Bounds.X - oldShape.Bounds.X, shape.Bounds.Y - oldShape.Bounds.Y));
                }
            }).ToArray();
        return diagrams.SequenceEqual(after.Diagrams) ? after : after with { Diagrams = diagrams };
    }

    private static BpmnShape TranslateAnnotation(BpmnShape shape, DiPoint delta) => shape with
    {
        Bounds = shape.Bounds with { X = shape.Bounds.X + delta.X, Y = shape.Bounds.Y + delta.Y },
        Label = shape.Label is { Bounds: { } bounds } label ? label with
        {
            Bounds = bounds with { X = bounds.X + delta.X, Y = bounds.Y + delta.Y },
        } : shape.Label,
    };

    private static DiPoint TranslatePoint(DiPoint point, DiPoint delta) => new(point.X + delta.X, point.Y + delta.Y);
}
