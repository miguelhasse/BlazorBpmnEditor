namespace Hasseware.AspNetCore.Components.Bpmn.Model;

/// <summary>Editor attachment policy, separate from BPMN Association semantics.</summary>
internal static class BpmnAnnotationAttachments
{
    internal static bool IsAnnotationAssociation(BpmnDocument document, string id) =>
        document.Find(id) is BpmnAssociation association
        && (document.Find(association.SourceRef) is BpmnTextAnnotation
            || document.Find(association.TargetRef) is BpmnTextAnnotation);

    internal static IEnumerable<BpmnAssociation> Connections(BpmnDocument document, string annotationId) =>
        document.Associations.Where(association => association.SourceRef == annotationId || association.TargetRef == annotationId);

    internal static string OtherEnd(BpmnAssociation association, string annotationId) =>
        association.SourceRef == annotationId ? association.TargetRef : association.SourceRef;

    internal static bool IsTarget(BpmnDocument document, string id) =>
        document.Find(id) is BpmnFlowNode or BpmnParticipant or BpmnLane
            or BpmnDataObjectReference or BpmnDataStoreReference or BpmnSequenceFlow or BpmnMessageFlow;

    internal static Dictionary<string, List<BpmnAssociation>> ConnectionsByElement(BpmnDocument document)
    {
        var connections = new Dictionary<string, List<BpmnAssociation>>(StringComparer.Ordinal);
        foreach (var association in document.Associations)
        {
            Add(association.SourceRef, association);
            if (association.TargetRef != association.SourceRef) Add(association.TargetRef, association);
        }
        return connections;

        void Add(string id, BpmnAssociation association)
        {
            if (!connections.TryGetValue(id, out var entries))
                connections.Add(id, entries = []);
            entries.Add(association);
        }
    }

    internal static BpmnAssociation? UniqueConnection(BpmnDocument document, string annotationId,
        IReadOnlyDictionary<string, List<BpmnAssociation>>? indexed = null)
    {
        if (indexed is not null)
        {
            return indexed.TryGetValue(annotationId, out var entries) && entries.Count == 1
                && IsTarget(document, OtherEnd(entries[0], annotationId)) ? entries[0] : null;
        }
        using var connections = Connections(document, annotationId).GetEnumerator();
        if (!connections.MoveNext()) return null;
        var connection = connections.Current;
        return !connections.MoveNext() && IsTarget(document, OtherEnd(connection, annotationId)) ? connection : null;
    }
}
