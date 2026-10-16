using System.Collections.Frozen;

using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// What a copy put on the clipboard.
/// </summary>
/// <remarks>
/// <para>
/// A detached fragment, not a reference into the document it came from. The document that was
/// copied may be edited, undone or replaced entirely before the paste happens, so the payload has
/// to stand on its own — every element it needs is in here, already closed over its own
/// dependencies.
/// </para>
/// <para>
/// Ids are the originals. They are regenerated at paste, not at copy, because the same payload may
/// be pasted many times and each paste must produce a distinct set (section 2.7: copy/paste
/// duplicates the graph, and each occurrence is unrelated even when visually identical).
/// </para>
/// </remarks>
public sealed record BpmnClipboardPayload
{
    /// <summary>Flow Nodes, including the nested content of any copied embedded Sub-Process.</summary>
    public IReadOnlyList<BpmnFlowNode> FlowNodes { get; init; } = [];

    /// <summary>Sequence Flows with both endpoints inside the fragment.</summary>
    public IReadOnlyList<BpmnSequenceFlow> SequenceFlows { get; init; } = [];

    /// <summary>Associations with both ends inside the fragment.</summary>
    public IReadOnlyList<BpmnAssociation> Associations { get; init; } = [];

    /// <summary>Data Associations with every end inside the fragment.</summary>
    public IReadOnlyList<BpmnDataAssociation> DataAssociations { get; init; } = [];

    /// <summary>Copied Text Annotations.</summary>
    public IReadOnlyList<BpmnTextAnnotation> TextAnnotations { get; init; } = [];

    /// <summary>Copied Groups.</summary>
    public IReadOnlyList<BpmnGroup> Groups { get; init; } = [];

    /// <summary>Copied Data Object References.</summary>
    public IReadOnlyList<BpmnDataObjectReference> DataObjectReferences { get; init; } = [];

    /// <summary>Copied Data Store References.</summary>
    public IReadOnlyList<BpmnDataStoreReference> DataStoreReferences { get; init; } = [];

    /// <summary>
    /// The copied DI, grouped by the plane it came from.
    /// </summary>
    /// <remarks>
    /// Grouped rather than flattened because a collapsed Sub-Process draws its interior on a plane
    /// of its own (section 10.1), and flattening put that interior on whatever plane the paste
    /// landed on — so pasting one collapsed Sub-Process spilled its entire nested graph onto the
    /// top level.
    /// </remarks>
    public IReadOnlyList<BpmnClipboardPlane> Planes { get; init; } = [];

    /// <summary>Every copied shape, whichever plane drew it.</summary>
    public IEnumerable<BpmnShape> Shapes => Planes.SelectMany(plane => plane.Shapes);

    /// <summary>Every copied edge, whichever plane drew it.</summary>
    public IEnumerable<BpmnEdge> Edges => Planes.SelectMany(plane => plane.Edges);

    /// <summary>
    /// The elements the user actually selected, as opposed to what came with them.
    /// </summary>
    /// <remarks>
    /// Paste re-parents these into the target container; everything else keeps its relationship to
    /// them, so a Boundary Event follows its host and a Sub-Process's content stays inside it.
    /// </remarks>
    public IReadOnlyList<string> RootIds { get; init; } = [];

    /// <summary>Whether anything was copied at all.</summary>
    public bool IsEmpty => FlowNodes.Count == 0
        && TextAnnotations.Count == 0
        && Groups.Count == 0
        && DataObjectReferences.Count == 0
        && DataStoreReferences.Count == 0;

    /// <summary>How many elements a paste would create, for a label.</summary>
    public int Count => FlowNodes.Count
        + SequenceFlows.Count
        + Associations.Count
        + DataAssociations.Count
        + TextAnnotations.Count
        + Groups.Count
        + DataObjectReferences.Count
        + DataStoreReferences.Count;

    /// <summary>An empty clipboard.</summary>
    public static BpmnClipboardPayload Empty { get; } = new();
}

/// <summary>
/// The DI one plane contributed to a clipboard fragment.
/// </summary>
/// <param name="OwnerId">
/// The element whose interior this plane draws, or <see langword="null"/> when it is the plane the
/// copied elements were themselves drawn on. A paste puts the null one on the active plane and
/// gives every other its own diagram, so a copied collapsed Sub-Process keeps a plane of its own.
/// </param>
/// <param name="Shapes">The shapes this plane drew.</param>
/// <param name="Edges">The edges this plane drew.</param>
public sealed record BpmnClipboardPlane(
    string? OwnerId,
    IReadOnlyList<BpmnShape> Shapes,
    IReadOnlyList<BpmnEdge> Edges);

/// <summary>
/// Builds a clipboard fragment from a selection.
/// </summary>
/// <remarks>
/// <para>
/// Copy is closure, not filtering. A selection is whatever the user happened to click, and pasting
/// it verbatim would produce elements referring to things that were never copied. So the set is
/// expanded to everything the copied elements cannot exist without — a Boundary Event's host is
/// already there or the Boundary Event is dropped, an embedded Sub-Process brings its entire
/// nested graph (section 2.7) — and narrowed to connectors whose both ends are inside it.
/// </para>
/// <para>
/// A Call Activity is the deliberate exception to deep cloning: it is reusable by reference, so
/// only the shape is copied and <c>calledElementRef</c> is left pointing at the same shared global
/// definition (section 2.7). Deep-cloning what it calls would silently fork a definition that
/// other Call Activities still share.
/// </para>
/// </remarks>
public static class BpmnClipboard
{
    /// <summary>
    /// Copies a selection into a detached fragment.
    /// </summary>
    /// <param name="document">The document to copy from.</param>
    /// <param name="elementIds">What the user selected. Anything unrecognised is ignored.</param>
    /// <returns>The fragment, which may be empty.</returns>
    public static BpmnClipboardPayload Copy(BpmnDocument document, IReadOnlyList<string> elementIds)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(elementIds);

        var roots = elementIds.Where(id => IsCopyable(document, id)).ToHashSet(StringComparer.Ordinal);
        if (roots.Count == 0) return BpmnClipboardPayload.Empty;

        var included = Close(document, roots);

        var planes = new List<BpmnClipboardPlane>();

        foreach (var diagram in document.Diagrams)
        {
            var shapes = diagram.Plane.Shapes.Where(shape => included.Contains(shape.BpmnElement)).ToList();
            var edges = diagram.Plane.Edges.Where(edge => included.Contains(edge.BpmnElement)).ToList();

            if (shapes.Count == 0 && edges.Count == 0) continue;

            /*
             * A plane whose own element is in the fragment is that element's interior and travels
             * with it. Any other plane is just where the copied elements happened to be drawn, so
             * its contribution belongs wherever the paste lands.
             */
            var owner = diagram.Plane.BpmnElement is { } element && included.Contains(element)
                ? element
                : null;

            planes.Add(new BpmnClipboardPlane(owner, shapes, edges));
        }

        return new BpmnClipboardPayload
        {
            FlowNodes = [.. document.FlowNodes.Where(node => included.Contains(node.Id))],
            SequenceFlows = [.. document.SequenceFlows.Where(flow => included.Contains(flow.Id))],
            Associations = [.. document.Associations.Where(association => included.Contains(association.Id))],
            DataAssociations = [.. document.DataAssociations.Where(association => included.Contains(association.Id))],
            TextAnnotations = [.. document.TextAnnotations.Where(annotation => included.Contains(annotation.Id))],
            Groups = [.. document.Groups.Where(group => included.Contains(group.Id))],
            DataObjectReferences = [.. document.DataObjectReferences.Where(reference => included.Contains(reference.Id))],
            DataStoreReferences = [.. document.DataStoreReferences.Where(reference => included.Contains(reference.Id))],
            Planes = planes,
            RootIds = [.. roots],
        };
    }

    /// <summary>
    /// Whether an element can be copied at all.
    /// </summary>
    /// <remarks>
    /// Pools, Lanes and Processes are not. Copying a Pool means copying a Participant and the
    /// Process it references, which is a structural transform rather than a duplication — and a
    /// Lane is not a container at all (section 2.8), so "copy this Lane" has no meaning separate
    /// from copying the nodes it classifies. Both are refused rather than half-done.
    /// </remarks>
    private static bool IsCopyable(BpmnDocument document, string id) => document.Find(id) switch
    {
        BpmnFlowNode => true,
        BpmnTextAnnotation => true,
        BpmnGroup => true,
        BpmnDataObjectReference => true,
        BpmnDataStoreReference => true,
        _ => false,
    };

    /// <summary>
    /// Expands a selection to everything the copied elements cannot exist without.
    /// </summary>
    /// <remarks>
    /// Iterated to a fixed point for the same reason the deletion cascade is: copying a
    /// Sub-Process pulls in a nested Sub-Process, which pulls in its own content in turn.
    /// </remarks>
    private static HashSet<string> Close(BpmnDocument document, HashSet<string> roots)
    {
        var included = new HashSet<string>(roots, StringComparer.Ordinal);
        var added = true;

        while (added)
        {
            added = false;

            foreach (var node in document.FlowNodes)
            {
                if (included.Contains(node.Id)) continue;

                // An embedded Sub-Process owns its content outright, and a Boundary Event is part
                // of its host. A Call Activity owns nothing: its containerId is elsewhere.
                var carried = included.Contains(node.ContainerId)
                    || (node.AttachedToRef is { } host && included.Contains(host));

                if (!carried) continue;

                included.Add(node.Id);
                added = true;
            }
        }

        // Connectors come along only when both ends do. A flow with one end outside the fragment
        // would paste as an edge to an element that does not exist.
        foreach (var flow in document.SequenceFlows)
        {
            if (included.Contains(flow.SourceRef) && included.Contains(flow.TargetRef)) included.Add(flow.Id);
        }

        foreach (var association in document.Associations)
        {
            if (included.Contains(association.SourceRef) && included.Contains(association.TargetRef))
            {
                included.Add(association.Id);
            }
        }

        foreach (var association in document.DataAssociations)
        {
            if (included.Contains(association.TargetRef) && association.SourceRefs.All(included.Contains))
            {
                included.Add(association.Id);
            }
        }

        // Artifacts and data inside a copied container come with it, the same way flow nodes do.
        foreach (var annotation in document.TextAnnotations)
        {
            if (included.Contains(annotation.ContainerId)) included.Add(annotation.Id);
        }

        foreach (var group in document.Groups)
        {
            if (included.Contains(group.ContainerId)) included.Add(group.Id);
        }

        foreach (var reference in document.DataObjectReferences)
        {
            if (included.Contains(reference.ContainerId)) included.Add(reference.Id);
        }

        foreach (var reference in document.DataStoreReferences)
        {
            if (included.Contains(reference.ContainerId)) included.Add(reference.Id);
        }

        return included;
    }

    /// <summary>
    /// The top-left corner of everything in the fragment, so a paste can be positioned relative to it.
    /// </summary>
    /// <param name="payload">The fragment.</param>
    /// <returns>The origin, or <see langword="null"/> when the fragment has no geometry.</returns>
    public static DiPoint? OriginOf(BpmnClipboardPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var shapes = payload.Shapes.ToList();
        if (shapes.Count == 0) return null;

        return new DiPoint(shapes.Min(shape => shape.Bounds.X), shapes.Min(shape => shape.Bounds.Y));
    }

    /// <summary>
    /// Rebuilds a fragment with fresh ids, ready to be added to a document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// References are rewritten only where both ends are inside the fragment. A reference that
    /// points outside it is either kept verbatim — <c>calledElementRef</c>, <c>messageRef</c> and
    /// the rest of the section 1.0 catalog, which the paste does not fork — or cleared, for a
    /// default-flow reference to a Sequence Flow that was not copied. Nothing is rewritten by
    /// pattern-matching strings, which section 6.1 is explicit about.
    /// </para>
    /// <para>
    /// Lane membership is dropped. A pasted node is not in the Lane its original was in unless the
    /// Lane itself was copied, and Lanes are not copyable; leaving the reference would make the
    /// node claim membership of a Lane whose <c>flowNodeRefs</c> never mention it.
    /// </para>
    /// </remarks>
    /// <param name="payload">The fragment to rebuild.</param>
    /// <param name="containerId">The container the roots are pasted into.</param>
    /// <param name="poolId">The Pool the roots are drawn in, when there is one.</param>
    /// <param name="offset">How far to move the geometry from where it was copied.</param>
    /// <param name="mint">Produces a fresh id from a prefix; <see cref="BpmnId.New"/> in practice.</param>
    /// <returns>The rebuilt fragment, whose ids appear nowhere in the source document.</returns>
    public static BpmnClipboardPayload Rebuild(
        BpmnClipboardPayload payload,
        string containerId,
        string? poolId,
        DiPoint offset,
        Func<string, string> mint)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(containerId);
        ArgumentNullException.ThrowIfNull(mint);

        var map = BuildIdMap(payload, mint);
        var roots = payload.RootIds.ToHashSet(StringComparer.Ordinal);

        string Rename(string id) => map.GetValueOrDefault(id, id);
        string? RenameInside(string? id) => id is not null && map.TryGetValue(id, out var renamed) ? renamed : null;
        string? RenameIfIncluded(string? id) => id is null ? null : Rename(id);
        BpmnLoopDataItem? RenameLoopDataItem(BpmnLoopDataItem? item) =>
            item is null ? null : item with { Id = RenameIfIncluded(item.Id) };

        // A root is re-parented into the paste target; anything carried along keeps its
        // relationship to whichever copied element brought it.
        string ContainerFor(string id, string original) =>
            roots.Contains(id) ? containerId : Rename(original);

        return new BpmnClipboardPayload
        {
            FlowNodes =
            [
                .. payload.FlowNodes.Select(node => node with
                {
                    Id = Rename(node.Id),
                    ContainerId = ContainerFor(node.Id, node.ContainerId),
                    PoolId = roots.Contains(node.Id) ? poolId : node.PoolId is null ? null : poolId,
                    LaneId = null,
                    AttachedToRef = node.AttachedToRef is null ? null : RenameInside(node.AttachedToRef),
                    DefaultFlowRef = RenameInside(node.DefaultFlowRef),
                    MultiInstanceLoop = node.MultiInstanceLoop is { } multi
                        ? multi with
                        {
                            LoopDataInputRef = RenameIfIncluded(multi.LoopDataInputRef),
                            LoopDataOutputRef = RenameIfIncluded(multi.LoopDataOutputRef),
                            InputDataItem = RenameLoopDataItem(multi.InputDataItem),
                            OutputDataItem = RenameLoopDataItem(multi.OutputDataItem),
                        }
                        : null,
                }),
            ],
            SequenceFlows =
            [
                .. payload.SequenceFlows.Select(flow => flow with
                {
                    Id = Rename(flow.Id),
                    SourceRef = Rename(flow.SourceRef),
                    TargetRef = Rename(flow.TargetRef),
                    ContainerId = ContainerFor(flow.Id, flow.ContainerId),
                }),
            ],
            Associations =
            [
                .. payload.Associations.Select(association => association with
                {
                    Id = Rename(association.Id),
                    SourceRef = Rename(association.SourceRef),
                    TargetRef = Rename(association.TargetRef),
                    ContainerId = ContainerFor(association.Id, association.ContainerId),
                }),
            ],
            DataAssociations =
            [
                .. payload.DataAssociations.Select(association => association with
                {
                    Id = Rename(association.Id),
                    SourceRefs = [.. association.SourceRefs.Select(Rename)],
                    TargetRef = Rename(association.TargetRef),
                    ContainerId = ContainerFor(association.Id, association.ContainerId),
                    OwnerId = association.OwnerId is null ? null : Rename(association.OwnerId),
                }),
            ],
            TextAnnotations =
            [
                .. payload.TextAnnotations.Select(annotation => annotation with
                {
                    Id = Rename(annotation.Id),
                    ContainerId = ContainerFor(annotation.Id, annotation.ContainerId),
                }),
            ],
            Groups =
            [
                .. payload.Groups.Select(group => group with
                {
                    Id = Rename(group.Id),
                    ContainerId = ContainerFor(group.Id, group.ContainerId),
                }),
            ],
            DataObjectReferences =
            [
                .. payload.DataObjectReferences.Select(reference => reference with
                {
                    Id = Rename(reference.Id),
                    ContainerId = ContainerFor(reference.Id, reference.ContainerId),
                }),
            ],
            DataStoreReferences =
            [
                .. payload.DataStoreReferences.Select(reference => reference with
                {
                    Id = Rename(reference.Id),
                    ContainerId = ContainerFor(reference.Id, reference.ContainerId),
                }),
            ],
            Planes =
            [
                .. payload.Planes.Select(plane => new BpmnClipboardPlane(
                    plane.OwnerId is null ? null : Rename(plane.OwnerId),
                    [
                        .. plane.Shapes.Select(shape => shape with
                        {
                            Id = BpmnId.ForDiagramElement(Rename(shape.BpmnElement)),
                            BpmnElement = Rename(shape.BpmnElement),

                            /*
                             * Only the plane the paste lands on is offset. A nested plane has its
                             * own coordinate space, and shifting it would move the interior
                             * relative to a Sub-Process that has not moved at all.
                             */
                            Bounds = plane.OwnerId is null ? Shift(shape.Bounds, offset) : shape.Bounds,
                            Label = plane.OwnerId is null ? ShiftLabel(shape.Label, offset) : shape.Label,
                        }),
                    ],
                    [
                        .. plane.Edges.Select(edge => edge with
                        {
                            Id = BpmnId.ForDiagramElement(Rename(edge.BpmnElement)),
                            BpmnElement = Rename(edge.BpmnElement),
                            Waypoints = plane.OwnerId is null
                                ? [.. edge.Waypoints.Select(point => Shift(point, offset))]
                                : edge.Waypoints,
                            Label = plane.OwnerId is null ? ShiftLabel(edge.Label, offset) : edge.Label,
                        }),
                    ])),
            ],
            RootIds = [.. payload.RootIds.Select(Rename)],
        };
    }

    /// <summary>
    /// A fresh id for every element in the fragment, keyed by its original.
    /// </summary>
    /// <remarks>
    /// Built in one pass before anything is rewritten, because a Sequence Flow's endpoints have to
    /// resolve to the new ids of nodes that may be rewritten after it.
    /// </remarks>
    private static FrozenDictionary<string, string> BuildIdMap(
        BpmnClipboardPayload payload,
        Func<string, string> mint)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        void Add(string id, string prefix) => map[id] = mint(prefix);

        foreach (var node in payload.FlowNodes)
        {
            Add(node.Id, PrefixOf(node.Type));
            if (node.MultiInstanceLoop?.InputDataItem?.Id is { } inputId) Add(inputId, "DataInput");
            if (node.MultiInstanceLoop?.OutputDataItem?.Id is { } outputId) Add(outputId, "DataOutput");
        }
        foreach (var flow in payload.SequenceFlows) Add(flow.Id, "Flow");
        foreach (var association in payload.Associations) Add(association.Id, "Association");
        foreach (var association in payload.DataAssociations) Add(association.Id, "DataAssociation");
        foreach (var annotation in payload.TextAnnotations) Add(annotation.Id, "TextAnnotation");
        foreach (var group in payload.Groups) Add(group.Id, "Group");
        foreach (var reference in payload.DataObjectReferences) Add(reference.Id, "DataObjectReference");
        foreach (var reference in payload.DataStoreReferences) Add(reference.Id, "DataStoreReference");

        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>The wire name of a type, which is what a generated id is prefixed with.</summary>
    private static string PrefixOf(BpmnFlowNodeType type) =>
        System.Text.Json.JsonSerializer.Serialize(type).Trim('"');

    private static Bounds Shift(Bounds bounds, DiPoint offset) =>
        bounds with { X = bounds.X + offset.X, Y = bounds.Y + offset.Y };

    private static DiPoint Shift(DiPoint point, DiPoint offset) =>
        new(point.X + offset.X, point.Y + offset.Y);

    private static BpmnLabel? ShiftLabel(BpmnLabel? label, DiPoint offset) => label switch
    {
        null => null,
        { Bounds: null } => label,
        { Bounds: { } bounds } => label with { Bounds = Shift(bounds, offset) },
    };
}
