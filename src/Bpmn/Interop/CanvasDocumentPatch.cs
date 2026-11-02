using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Interop;

internal sealed record CanvasArrayPatch(
    IReadOnlyList<JsonElement> Upserts,
    IReadOnlyList<string> Removals,
    IReadOnlyList<string>? Order);

internal sealed record CanvasCollectionPatch(string Collection, CanvasArrayPatch Delta);

internal sealed record CanvasPlanePatch(string DiagramId, CanvasArrayPatch? Shapes, CanvasArrayPatch? Edges);

/// <summary>Changes to the canvas projection, based on the last acknowledged resident document.</summary>
internal sealed record CanvasDocumentPatch(
    long BaseRevision,
    long Revision,
    string? DiagramId,
    IReadOnlyList<CanvasCollectionPatch> Collections,
    IReadOnlyList<CanvasPlanePatch> Planes)
{
    internal static CanvasDocumentPatch? Create(
        BpmnDocument before, BpmnDocument after, string? diagramId, long baseRevision, long revision)
    {
        // Containment/root changes retain the authoritative full-load path.
        if (!ReferenceEquals(before.Definitions, after.Definitions)
            || !ReferenceEquals(before.Collaboration, after.Collaboration)
            || before.Diagrams.Count == 0 || before.Diagrams.Count != after.Diagrams.Count)
            return null;

        var collections = new List<CanvasCollectionPatch>();
        var planes = new List<CanvasPlanePatch>();
        var valid = true;
        Add("flowNodes", before.FlowNodes, after.FlowNodes, static item => item.Id);
        Add("sequenceFlows", before.SequenceFlows, after.SequenceFlows, static item => item.Id);
        Add("messageFlows", before.MessageFlows, after.MessageFlows, static item => item.Id);
        Add("associations", before.Associations, after.Associations, static item => item.Id);
        Add("dataAssociations", before.DataAssociations, after.DataAssociations, static item => item.Id);
        Add("textAnnotations", before.TextAnnotations, after.TextAnnotations, static item => item.Id);
        Add("groups", before.Groups, after.Groups, static item => item.Id);
        Add("dataObjectReferences", before.DataObjectReferences, after.DataObjectReferences, static item => item.Id);
        Add("dataStoreReferences", before.DataStoreReferences, after.DataStoreReferences, static item => item.Id);
        Add("lanes", before.Lanes, after.Lanes, static item => item.Id);
        Add("laneSets", before.LaneSets, after.LaneSets, static item => item.Id);

        for (var index = 0; index < before.Diagrams.Count; index++)
        {
            var old = before.Diagrams[index];
            var next = after.Diagrams[index];
            if (ReferenceEquals(old, next)) continue;
            if (old.Id != next.Id || old.Name != next.Name || old.Plane.Id != next.Plane.Id
                || old.Plane.BpmnElement != next.Plane.BpmnElement)
                return null;
            var shapes = Difference(old.Plane.Shapes, next.Plane.Shapes, static item => item.Id, ref valid);
            var edges = Difference(old.Plane.Edges, next.Plane.Edges, static item => item.Id, ref valid);
            if (shapes is not null || edges is not null) planes.Add(new(next.Id, shapes, edges));
        }

        return valid ? new(baseRevision, revision, diagramId, collections, planes) : null;

        void Add<T>(string name, IReadOnlyList<T> old, IReadOnlyList<T> next, Func<T, string> id)
        {
            var delta = Difference(old, next, id, ref valid);
            if (delta is not null) collections.Add(new(name, delta));
        }
    }

    private static CanvasArrayPatch? Difference<T>(
        IReadOnlyList<T> before, IReadOnlyList<T> after, Func<T, string> id, ref bool valid)
    {
        if (ReferenceEquals(before, after)) return null;

        var old = new Dictionary<string, T>(before.Count, StringComparer.Ordinal);
        foreach (var item in before)
            if (!old.TryAdd(id(item), item)) valid = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var upserts = new List<JsonElement>();
        for (var index = 0; index < after.Count; index++)
        {
            var item = after[index];
            var key = id(item);
            if (!seen.Add(key)) valid = false;
            if (old.TryGetValue(key, out var previous))
            {
                if (ReferenceEquals(previous, item)) continue;
                var projected = CanvasDocumentPayload.Project(item);
                if (!JsonElement.DeepEquals(CanvasDocumentPayload.Project(previous), projected)) upserts.Add(projected);
            }
            else upserts.Add(CanvasDocumentPayload.Project(item));
        }
        var removals = old.Keys.Where(key => !seen.Contains(key)).ToArray();
        // Map application retains survivors in place and appends new ids. Only send an order when that differs.
        var reordered = false;
        var position = 0;
        foreach (var item in before)
        {
            if (!seen.Contains(id(item))) continue;
            reordered |= position >= after.Count || id(after[position]) != id(item);
            position++;
        }
        foreach (var item in after)
        {
            if (old.ContainsKey(id(item))) continue;
            reordered |= position >= after.Count || id(after[position]) != id(item);
            position++;
        }
        return upserts.Count == 0 && removals.Length == 0 && !reordered ? null
            : new(upserts, removals, reordered ? after.Select(id).ToArray() : null);
    }
}
