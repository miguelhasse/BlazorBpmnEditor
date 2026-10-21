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

    internal static BpmnAssociation? UniqueConnection(BpmnDocument document, string annotationId)
    {
        using var connections = Connections(document, annotationId).GetEnumerator();
        if (!connections.MoveNext()) return null;
        var connection = connections.Current;
        return !connections.MoveNext() && IsTarget(document, OtherEnd(connection, annotationId)) ? connection : null;
    }
}
