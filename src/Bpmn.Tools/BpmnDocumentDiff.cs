using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools;

/// <summary>An element that was added or removed.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Type">Its type in wire form.</param>
/// <param name="Name">Its name.</param>
public sealed record BpmnDiffElement(string Id, string Type, BpmnUntrustedText? Name);

/// <summary>One property whose value differs.</summary>
/// <param name="Name">The property, or <c>type</c> when the element's type was switched.</param>
/// <param name="Before">The value before.</param>
/// <param name="After">The value after.</param>
public sealed record BpmnPropertyChange(string Name, BpmnUntrustedText? Before, BpmnUntrustedText? After);

/// <summary>An element present in both documents that differs between them.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Type">Its type after.</param>
/// <param name="Properties">The properties whose values differ.</param>
/// <param name="SourceBefore">For a connector whose source moved, the source before.</param>
/// <param name="SourceAfter">For a connector whose source moved, the source after.</param>
/// <param name="TargetBefore">For a connector whose target moved, the target before.</param>
/// <param name="TargetAfter">For a connector whose target moved, the target after.</param>
/// <param name="GeometryChanged">Whether it is drawn somewhere else, or at another size, or along another route.</param>
public sealed record BpmnChangedElement(
    string Id,
    string Type,
    IReadOnlyList<BpmnPropertyChange> Properties,
    string? SourceBefore,
    string? SourceAfter,
    string? TargetBefore,
    string? TargetAfter,
    bool GeometryChanged);

/// <summary>How two documents differ, element by element.</summary>
/// <param name="Added">Elements only the second has.</param>
/// <param name="Removed">Elements only the first has.</param>
/// <param name="Changed">Elements both have that differ.</param>
/// <param name="Truncated">Whether there were more differences than the limit lists.</param>
public sealed record BpmnDocumentDiffResult(
    IReadOnlyList<BpmnDiffElement> Added,
    IReadOnlyList<BpmnDiffElement> Removed,
    IReadOnlyList<BpmnChangedElement> Changed,
    bool Truncated);

/// <summary>A minimal structural diff of two documents, matching elements by id.</summary>
/// <remarks>
/// Properties are compared generically, through the same descriptors the property panel shows, so every
/// standard property counts without a table of its own. It does not detect an element that was deleted
/// and recreated under another id, nor a renamed id, and it compares two documents, not a history.
/// </remarks>
public static class BpmnDocumentDiff
{
    /// <summary>Compares two documents.</summary>
    /// <param name="before">The earlier document.</param>
    /// <param name="after">The later document.</param>
    /// <param name="limits">How many differences to list and how much text to quote; the defaults when null.</param>
    /// <returns>The differences, in document order.</returns>
    public static BpmnDocumentDiffResult Compare(BpmnDocument before, BpmnDocument after, BpmnToolLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        limits ??= BpmnToolLimits.Default;

        var beforeElements = ElementsOf(before);
        var afterElements = ElementsOf(after);
        var beforeGeometry = GeometryOf(before);
        var afterGeometry = GeometryOf(after);

        var added = new List<BpmnDiffElement>();
        var removed = new List<BpmnDiffElement>();
        var changed = new List<BpmnChangedElement>();
        var count = 0;
        var truncated = false;

        bool Room()
        {
            if (count < limits.MaxElements)
            {
                count++;
                return true;
            }

            truncated = true;
            return false;
        }

        foreach (var (id, element) in beforeElements)
        {
            if (!afterElements.ContainsKey(id) && Room()) removed.Add(new BpmnDiffElement(id, element.Type, Text(element.Name, limits)));
        }

        foreach (var (id, element) in afterElements)
        {
            if (!beforeElements.TryGetValue(id, out var earlier))
            {
                if (Room()) added.Add(new BpmnDiffElement(id, element.Type, Text(element.Name, limits)));
                continue;
            }

            var properties = Properties(before, after, id, earlier, element, limits);
            var sourceMoved = earlier.Source != element.Source;
            var targetMoved = earlier.Target != element.Target;
            var geometryChanged = !SameGeometry(beforeGeometry.GetValueOrDefault(id), afterGeometry.GetValueOrDefault(id));

            if ((properties.Count > 0 || sourceMoved || targetMoved || geometryChanged) && Room())
            {
                changed.Add(new BpmnChangedElement(
                    id,
                    element.Type,
                    properties,
                    sourceMoved ? earlier.Source : null,
                    sourceMoved ? element.Source : null,
                    targetMoved ? earlier.Target : null,
                    targetMoved ? element.Target : null,
                    geometryChanged));
            }
        }

        return new BpmnDocumentDiffResult(added, removed, changed, truncated);
    }

    private static List<BpmnPropertyChange> Properties(
        BpmnDocument before,
        BpmnDocument after,
        string id,
        Element earlier,
        Element later,
        BpmnToolLimits limits)
    {
        var changes = new List<BpmnPropertyChange>();
        if (earlier.Type != later.Type) changes.Add(new BpmnPropertyChange("type", Text(earlier.Type, limits), Text(later.Type, limits)));

        var old = ValuesOf(before, id);
        var now = ValuesOf(after, id);
        foreach (var name in old.Keys.Concat(now.Keys).Distinct(StringComparer.Ordinal))
        {
            var previous = old.GetValueOrDefault(name);
            var current = now.GetValueOrDefault(name);
            if (!string.Equals(previous, current, StringComparison.Ordinal))
            {
                changes.Add(new BpmnPropertyChange(name, Text(previous, limits), Text(current, limits)));
            }
        }

        return changes;
    }

    private static Dictionary<string, string?> ValuesOf(BpmnDocument document, string id) =>
        BpmnElementInspector.PropertiesOf(document, id)
            .GroupBy(property => property.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);

    // Every element the tools describe, in document order, keyed by id.
    private static Dictionary<string, Element> ElementsOf(BpmnDocument document)
    {
        var elements = new Dictionary<string, Element>(StringComparer.Ordinal);
        void Add(string id, string type, string? name, string? source = null, string? target = null) =>
            elements.TryAdd(id, new Element(type, name, source, target));

        foreach (var process in document.Processes) Add(process.Id, "process", process.Name);
        foreach (var pool in document.Collaboration?.Participants ?? []) Add(pool.Id, "participant", pool.Name);
        foreach (var lane in document.Lanes) Add(lane.Id, "lane", lane.Name);
        foreach (var node in document.FlowNodes) Add(node.Id, BpmnElementInspector.WireNameOf(node.Type), node.Name);
        foreach (var data in document.DataObjectReferences) Add(data.Id, "dataObjectReference", data.Name);
        foreach (var store in document.DataStoreReferences) Add(store.Id, "dataStoreReference", store.Name);
        foreach (var note in document.TextAnnotations) Add(note.Id, "textAnnotation", note.Text);
        foreach (var group in document.Groups) Add(group.Id, "group", group.Name);
        foreach (var flow in document.SequenceFlows) Add(flow.Id, "sequenceFlow", flow.Name, flow.SourceRef, flow.TargetRef);
        foreach (var flow in document.MessageFlows) Add(flow.Id, "messageFlow", flow.Name, flow.SourceRef, flow.TargetRef);
        foreach (var association in document.Associations) Add(association.Id, "association", null, association.SourceRef, association.TargetRef);
        foreach (var association in document.DataAssociations)
        {
            Add(association.Id, "dataAssociation", null, string.Join(',', association.SourceRefs), association.TargetRef);
        }

        return elements;
    }

    // Every shape's bounds and every edge's route, per element, across all diagrams.
    private static Dictionary<string, List<double>> GeometryOf(BpmnDocument document)
    {
        var geometry = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (var plane in document.Diagrams.Select(diagram => diagram.Plane))
        {
            foreach (var shape in plane.Shapes)
            {
                Bucket(geometry, shape.BpmnElement).AddRange([shape.Bounds.X, shape.Bounds.Y, shape.Bounds.Width, shape.Bounds.Height]);
            }

            foreach (var edge in plane.Edges)
            {
                Bucket(geometry, edge.BpmnElement).AddRange(edge.Waypoints.SelectMany(point => new[] { point.X, point.Y }));
            }
        }

        return geometry;
    }

    private static List<double> Bucket(Dictionary<string, List<double>> geometry, string id)
    {
        if (!geometry.TryGetValue(id, out var values)) geometry[id] = values = [];
        return values;
    }

    private static bool SameGeometry(List<double>? before, List<double>? after) =>
        (before ?? []).SequenceEqual(after ?? []);

    private static BpmnUntrustedText? Text(string? text, BpmnToolLimits limits) => BpmnUntrustedText.From(text, limits.MaxTextLength);

    private sealed record Element(string Type, string? Name, string? Source, string? Target);
}
