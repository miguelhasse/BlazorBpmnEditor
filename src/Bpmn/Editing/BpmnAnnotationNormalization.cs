using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Geometry;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>Losses requiring explicit consent before applying the editor's annotation policy.</summary>
/// <param name="AnnotationIds">Annotations affected by normalization.</param>
/// <param name="RemovedAssociationIds">Surplus Associations, in original source order.</param>
/// <param name="StraightenedEdgeIds">DI edges whose intermediate bends will be removed.</param>
public sealed record BpmnAnnotationNormalization(
    IReadOnlyList<string> AnnotationIds,
    IReadOnlyList<string> RemovedAssociationIds,
    IReadOnlyList<string> StraightenedEdgeIds)
{
    /// <summary>Whether import needs a separate normalization confirmation.</summary>
    public bool RequiresConfirmation => RemovedAssociationIds.Count > 0 || StraightenedEdgeIds.Count > 0;

    internal static BpmnAnnotationNormalization Inspect(BpmnDocument document)
    {
        var sourceOrder = document.Source?.Clone().Descendants()
            .Where(element => element.Name == BpmnXmlNames.Model + "association")
            .Select(element => (string?)element.Attribute("id"))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Select((id, index) => (id, index))
            .ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
        var removed = new HashSet<string>(StringComparer.Ordinal);
        var affected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var annotation in document.TextAnnotations)
        {
            var connections = BpmnAnnotationAttachments.Connections(document, annotation.Id)
                .OrderBy(association => sourceOrder?.GetValueOrDefault(association.Id, int.MaxValue) ?? 0)
                .ToArray();
            var retained = connections.FirstOrDefault(association =>
                BpmnAnnotationAttachments.IsTarget(document, BpmnAnnotationAttachments.OtherEnd(association, annotation.Id)));
            foreach (var connection in connections)
            {
                if (connection == retained) continue;
                removed.Add(connection.Id);
                affected.Add(annotation.Id);
            }
        }

        var straightened = new List<string>();
        foreach (var edge in document.Diagrams.SelectMany(diagram => diagram.Plane.Edges))
        {
            if (removed.Contains(edge.BpmnElement) || edge.Waypoints.Count <= 2
                || !BpmnAnnotationAttachments.IsAnnotationAssociation(document, edge.BpmnElement)) continue;
            straightened.Add(edge.Id);
            if (document.Find(edge.BpmnElement) is not BpmnAssociation association) continue;
            foreach (var id in (string[])[association.SourceRef, association.TargetRef])
            {
                if (document.Find(id) is BpmnTextAnnotation) affected.Add(id);
            }
        }
        return new BpmnAnnotationNormalization(
            [.. document.TextAnnotations.Where(annotation => affected.Contains(annotation.Id)).Select(annotation => annotation.Id)],
            [.. document.Associations.Where(association => removed.Contains(association.Id))
                .OrderBy(association => sourceOrder?.GetValueOrDefault(association.Id, int.MaxValue) ?? 0)
                .Select(association => association.Id)],
            straightened);
    }

    internal BpmnDocument Apply(BpmnDocument document)
    {
        var removed = RemovedAssociationIds.ToHashSet(StringComparer.Ordinal);
        var straightened = StraightenedEdgeIds.ToHashSet(StringComparer.Ordinal);
        var normalized = document with
        {
            Associations = [.. document.Associations.Where(association => !removed.Contains(association.Id))],
            Diagrams = [.. document.Diagrams.Select(diagram => diagram with
            {
                Plane = diagram.Plane with
                {
                    Edges = [.. diagram.Plane.Edges.Where(edge => !removed.Contains(edge.BpmnElement))
                        .Select(edge => straightened.Contains(edge.Id)
                            ? edge with { Waypoints = [edge.Waypoints[0], edge.Waypoints[^1]] } : edge)],
                },
            })],
        };
        // Endpoint normalization is already the reader's import policy. Do not move notes here.
        return ConnectorEndpoints.Normalize(normalized);
    }
}
