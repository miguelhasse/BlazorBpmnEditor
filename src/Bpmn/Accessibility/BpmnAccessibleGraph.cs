using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Accessibility;

/// <summary>One stop on the diagram, as something that cannot see it needs to hear about it.</summary>
/// <param name="ElementId">The element this stands for.</param>
/// <param name="TypeName">Its BPMN type, in wire spelling.</param>
/// <param name="Label">Its name, or empty when it has none. Many BPMN elements legitimately do not.</param>
/// <param name="ContainerId">The Process, Pool or Sub-Process that owns it.</param>
/// <param name="LaneId">The Lane it is assigned to, when it is in one.</param>
/// <param name="Depth">How deeply nested it is, which becomes the tree item's level.</param>
/// <param name="Outgoing">Elements a Sequence Flow leads to, in document order.</param>
/// <param name="Incoming">Elements a Sequence Flow arrives from, in document order.</param>
public sealed record BpmnGraphNode(
    string ElementId,
    string TypeName,
    string Label,
    string? ContainerId,
    string? LaneId,
    int Depth,
    IReadOnlyList<string> Outgoing,
    IReadOnlyList<string> Incoming)
{
    /// <summary>
    /// What an artifact or data element is associated with, by name.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Outgoing"/> and <see cref="Incoming"/> on purpose. Those are
    /// Sequence Flow, which is what the arrow keys walk; an Association is not control flow and
    /// treating it as such would send a reader pressing Right from a Task into a comment about it.
    /// Names rather than ids, because an id is not something a reader can do anything with.
    /// </remarks>
    public IReadOnlyList<string> Attached { get; init; } = [];

    internal IReadOnlyList<string?> AttachedTypes { get; init; } = [];

    /// <summary>
    /// Whether a Sub-Process is drawn expanded, or <see langword="null"/> for anything that cannot be.
    /// </summary>
    /// <remarks>
    /// Tri-state because <c>aria-expanded</c> is. On a Task the attribute must be absent rather
    /// than <c>false</c>: present-and-false announces "collapsed", which would tell a reader that
    /// every Task in the diagram is hiding something.
    /// </remarks>
    public bool? IsExpanded { get; init; }

    /// <summary>Whether two nodes say the same thing, comparing the lists by their contents (record equality compares them by reference).</summary>
    internal static bool SameContent(BpmnGraphNode first, BpmnGraphNode second) =>
        ReferenceEquals(first, second)
        || (first.ElementId == second.ElementId && first.TypeName == second.TypeName && first.Label == second.Label
            && first.ContainerId == second.ContainerId && first.LaneId == second.LaneId && first.Depth == second.Depth
            && first.IsExpanded == second.IsExpanded
            && first.Outgoing.SequenceEqual(second.Outgoing) && first.Incoming.SequenceEqual(second.Incoming)
            && first.Attached.SequenceEqual(second.Attached) && first.AttachedTypes.SequenceEqual(second.AttachedTypes));

    /// <summary>Whether this is a container rather than something a flow passes through.</summary>
    private bool IsContainer => TypeName is "pool" or "lane";

    /// <summary>Whether this is an artifact or data element: drawn, named, but never in the flow.</summary>
    private bool IsArtifact =>
        TypeName is "textAnnotation" or "group" or "dataObjectReference" or "dataStoreReference";

    /// <summary>
    /// What a reader hears when they arrive here.
    /// </summary>
    /// <remarks>
    /// The type first, because many BPMN elements have no name and the type is what says what the
    /// thing is. The flow counts after, because on a diagram nobody can see they are the only
    /// indication that there is somewhere further to go — a Gateway announcing three outgoing
    /// flows is telling the reader that Down will find two more branches.
    /// </remarks>
    public string AccessibleName
    {
        get
        {
            var type = Humanise(TypeName);
            var named = Label.Length == 0 ? type : $"{type} {Label}";

            /*
             * No flow counts for anything that cannot carry a Sequence Flow — a Pool, a Lane, an
             * artifact. "No incoming, no outgoing" would not be telling a reader that this one
             * happens to be unconnected; it would be saying something true of every Pool and every
             * Text Annotation ever drawn.
             */
            /*
             * A Sub-Process says which state it is in, because on a diagram nobody can see, that
             * is the difference between a leaf and a container with a whole process inside it.
             * aria-expanded carries it as well, and this repeats it for the same reason the flow
             * counts are spoken: the reader needs it in the name they hear, not only in the
             * attribute their software may or may not announce.
             */
            if (IsExpanded is { } expanded) return $"{named}, {(expanded ? "expanded" : "collapsed")}.";

            if (IsContainer) return $"{named}.";

            if (IsArtifact)
            {
                // What it is attached to is the only thing that makes an artifact mean anything.
                // A Data Object nobody uses and one every Task reads are different facts, and
                // neither is visible to a reader who is being read to.
                return Attached.Count == 0
                    ? $"{named}. Not associated with anything."
                    : $"{named}. Associated with {string.Join(", ", Attached)}.";
            }

            return $"{named}. {Count(Incoming.Count, "incoming")}, {Count(Outgoing.Count, "outgoing")}.";
        }
    }

    private static string Count(int value, string what) => value switch
    {
        0 => $"no {what}",
        1 => $"1 {what}",
        _ => $"{value} {what}",
    };

    /// <summary><c>intermediateCatchEvent</c> to <c>Intermediate catch event</c>.</summary>
    internal static string Humanise(string wireName)
    {
        if (wireName.Length == 0) return "Element";

        var spaced = new System.Text.StringBuilder(wireName.Length + 8);
        spaced.Append(char.ToUpperInvariant(wireName[0]));

        foreach (var character in wireName.AsSpan(1))
        {
            if (char.IsUpper(character)) spaced.Append(' ').Append(char.ToLowerInvariant(character));
            else spaced.Append(character);
        }

        return spaced.ToString();
    }
}

/// <summary>
/// The diagram as a navigable graph, for the parallel accessible tree (implementation plan M7).
/// </summary>
/// <remarks>
/// <para>
/// Built from the document rather than from the SVG, and that is the whole point. A screen reader
/// walking the drawn shapes gets them in paint order, gets nothing for anything the paper has
/// culled, and gets no way to ask what a shape is connected to — which is most of what a BPMN
/// diagram means. This produces the same set of elements in a stated order, with the flows between
/// them, from the model that is true whether or not anything has been drawn.
/// </para>
/// <para>
/// Only the active plane. A document may hold several, and a reader is looking at one of them; the
/// diagram picker is how they reach the others, exactly as for someone using a mouse.
/// </para>
/// </remarks>
public static class BpmnAccessibleGraph
{
    /// <summary>The navigable nodes of one diagram, in reading order.</summary>
    /// <param name="document">The document to read.</param>
    /// <param name="diagramId">The plane being shown, or <see langword="null"/> for the first.</param>
    /// <returns>Every drawn element that is a stop, outermost first, each container's own contents after it.</returns>
    public static IReadOnlyList<BpmnGraphNode> For(BpmnDocument document, string? diagramId)
    {
        ArgumentNullException.ThrowIfNull(document);

        var diagram = diagramId is null
            ? document.Diagrams.Count > 0 ? document.Diagrams[0] : null
            : document.Diagrams.FirstOrDefault(candidate => candidate.Id == diagramId);

        if (diagram is null) return [];

        var drawn = diagram.Plane.Shapes
            .Select(shape => shape.BpmnElement)
            .ToHashSet(StringComparer.Ordinal);

        var nodes = document.FlowNodes
            .Where(node => drawn.Contains(node.Id))
            .ToList();

        /*
         * Pools and Lanes are stops too.
         *
         * They hold no Sequence Flow and are never walked along one, but on a Collaboration they
         * are what says whose process this is — and a reader offered the tasks and never told
         * which Pool they belong to has been given the diagram with its structure removed. They
         * come before their contents, which is where a tree puts a parent.
         */
        var swimlanes = Swimlanes(document, drawn);

        var flows = document.SequenceFlows
            .Where(flow => drawn.Contains(flow.SourceRef) && drawn.Contains(flow.TargetRef))
            .ToList();

        var outgoing = flows
            .GroupBy(flow => flow.SourceRef, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(flow => flow.TargetRef).ToList(), StringComparer.Ordinal);

        var incoming = flows
            .GroupBy(flow => flow.TargetRef, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(flow => flow.SourceRef).ToList(), StringComparer.Ordinal);

        var depths = DepthsOf(nodes);

        /*
         * Document order, with nesting expressed as a level rather than as a sort key. Ordering by
         * depth would list every container first and every container's contents afterwards, which
         * reads as two unrelated lists; document order puts a Sub-Process immediately before the
         * things inside it, which is the order a tree is walked in.
         */
        var laneOf = document.Lanes
            .SelectMany(lane => lane.FlowNodeRefs.Select(node => (node, lane.Id)))
            .ToLookup(pair => pair.node, pair => pair.Id, StringComparer.Ordinal);

        var expansion = diagram.Plane.Shapes.ToDictionary(
            shape => shape.BpmnElement,
            shape => shape.IsExpanded,
            StringComparer.Ordinal);

        var flowNodes = nodes
            .Select(node => new BpmnGraphNode(
                    node.Id,
                    BpmnElementInspector.WireNameOf(node.Type),
                    node.Name ?? string.Empty,
                    node.ContainerId,
                    node.LaneId,
                    depths[node.Id],
                    outgoing.TryGetValue(node.Id, out var to) ? to : [],
                    incoming.TryGetValue(node.Id, out var from) ? from : [])
            {
                // Only a Sub-Process has a state to be in. A Call Activity looks like a container
                // and never expands — its content belongs to a global Process — so saying it is
                // collapsed would promise something that will never open.
                IsExpanded = node.FlowElementsOwned
                    ? expansion.GetValueOrDefault(node.Id) == true
                    : null,
            })
            .ToList();

        /*
         * Artifacts and data elements are stops as well.
         *
         * They carry no Sequence Flow and the arrow keys never walk into one, but they are drawn,
         * they are named, and a diagram where a Data Object feeds three Tasks says something a list
         * of only the Tasks does not. They are placed after the flow nodes of the container that
         * holds them, which is where they sit in the document.
         */
        var stops = flowNodes.Concat(Artifacts(document, drawn, depths)).ToList();

        return swimlanes.Count == 0 ? stops : Interleave(swimlanes, stops, laneOf);
    }

    /// <summary>The artifacts and data elements drawn on this plane.</summary>
    private static IEnumerable<BpmnGraphNode> Artifacts(
        BpmnDocument document,
        HashSet<string> drawn,
        Dictionary<string, int> depths)
    {
        var attachments = Attachments(document);

        foreach (var reference in document.DataObjectReferences)
        {
            if (drawn.Contains(reference.Id))
            {
                yield return Artifact(reference.Id, "dataObjectReference", reference.Name, reference.ContainerId, depths, attachments);
            }
        }

        foreach (var reference in document.DataStoreReferences)
        {
            if (drawn.Contains(reference.Id))
            {
                yield return Artifact(reference.Id, "dataStoreReference", reference.Name, reference.ContainerId, depths, attachments);
            }
        }

        foreach (var annotation in document.TextAnnotations)
        {
            if (drawn.Contains(annotation.Id))
            {
                // The text is the annotation: it has no name, and reading out its id instead would
                // tell a reader nothing about what the note says.
                yield return Artifact(annotation.Id, "textAnnotation", annotation.Text, annotation.ContainerId, depths, attachments);
            }
        }

        foreach (var group in document.Groups)
        {
            if (drawn.Contains(group.Id))
            {
                yield return Artifact(group.Id, "group", group.Name, group.ContainerId, depths, attachments);
            }
        }
    }

    private static BpmnGraphNode Artifact(
        string id,
        string typeName,
        string? label,
        string containerId,
        Dictionary<string, int> depths,
        ILookup<string, (string Name, string? Type)> attachments) => new(
            id,
            typeName,
            Collapse(label),
            containerId,
            null,
            // A level below the container when that container is itself drawn, which is a
            // Sub-Process; otherwise it sits directly on the plane.
            depths.TryGetValue(containerId, out var depth) ? depth + 1 : 1,
            [],
            [])
        {
            Attached = [.. attachments[id].Select(attachment => attachment.Name)],
            AttachedTypes = [.. attachments[id].Select(attachment => attachment.Type)],
        };

    /// <summary>
    /// What each artifact is associated with, by the other end's readable name.
    /// </summary>
    /// <remarks>
    /// Both kinds of association, because both are what an artifact is for: a plain Association ties
    /// a Text Annotation or Group to what it comments on, and a Data Association moves data between
    /// an activity and a Data Object. Neither is control flow, which is why they end up here rather
    /// than in the flow lists the arrow keys walk.
    /// </remarks>
    private static ILookup<string, (string Name, string? Type)> Attachments(BpmnDocument document)
    {
        var names = document.FlowNodes
            .ToDictionary(node => node.Id, node => Collapse(node.Name) is { Length: > 0 } name
                ? (Name: name, Type: (string?)null)
                : (Name: BpmnGraphNode.Humanise(BpmnElementInspector.WireNameOf(node.Type)),
                    Type: (string?)BpmnElementInspector.WireNameOf(node.Type)), StringComparer.Ordinal);

        var pairs = new List<(string Artifact, (string Name, string? Type) Other)>();

        foreach (var association in document.Associations)
        {
            Add(association.SourceRef, association.TargetRef);
            Add(association.TargetRef, association.SourceRef);
        }

        foreach (var association in document.DataAssociations)
        {
            foreach (var source in association.SourceRefs)
            {
                Add(source, association.TargetRef);
                Add(association.TargetRef, source);
            }
        }

        return pairs.ToLookup(pair => pair.Artifact, pair => pair.Other, StringComparer.Ordinal);

        // Recorded only when the other end is a flow node with a name to give: an association
        // between two artifacts has nothing useful to announce in either direction.
        void Add(string artifact, string other)
        {
            if (names.TryGetValue(other, out var name)) pairs.Add((Artifact: artifact, Other: name));
        }
    }

    /// <summary>One line, single-spaced: a label is read aloud, and a note may be wrapped in XML.</summary>
    private static string Collapse(string? text) =>
        text is null ? string.Empty : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The Pools and Lanes drawn on this plane, each Pool followed by its own Lanes.</summary>
    private static List<BpmnGraphNode> Swimlanes(BpmnDocument document, HashSet<string> drawn)
    {
        var lanesOfPool = document.LaneSets
            .GroupBy(set => set.ContainerId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)[.. group.SelectMany(set => set.LaneIds)],
                StringComparer.Ordinal);

        var lanes = document.Lanes.ToDictionary(lane => lane.Id, StringComparer.Ordinal);
        var result = new List<BpmnGraphNode>();
        var placedLanes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var participant in document.Collaboration?.Participants ?? [])
        {
            if (!drawn.Contains(participant.Id)) continue;

            // The Pool carries the Process it references, which is what the nodes inside it name
            // as their container — that is how a node is matched to the Pool that holds it.
            result.Add(new BpmnGraphNode(
                participant.Id,
                "pool",
                participant.Name ?? string.Empty,
                participant.ProcessRef,
                null,
                1,
                [],
                []));

            var owned = participant.ProcessRef is { } process && lanesOfPool.TryGetValue(process, out var ids) ? ids : [];

            foreach (var laneId in owned)
            {
                if (!drawn.Contains(laneId) || !lanes.TryGetValue(laneId, out var lane)) continue;
                // Several Participants may reference one Process. Its drawn Lane is still one
                // semantic stop, placed under the first drawn Participant in document order.
                if (!placedLanes.Add(laneId)) continue;

                result.Add(new BpmnGraphNode(lane.Id, "lane", lane.Name ?? string.Empty, participant.Id, null, 2, [], []));
            }
        }

        return result;
    }

    /// <summary>
    /// Puts each flow node under the Pool or Lane that holds it, and everything else at the end.
    /// </summary>
    /// <remarks>
    /// A tree is only worth walking if a parent is next to its children. Depth is re-stated here
    /// rather than kept from the flat pass, because inside a Pool a node is a level deeper than it
    /// was on a plain Process and a level deeper again when a Lane holds it.
    /// </remarks>
    private static List<BpmnGraphNode> Interleave(
        List<BpmnGraphNode> swimlanes,
        List<BpmnGraphNode> flowNodes,
        ILookup<string, string> laneOf)
    {
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<BpmnGraphNode>(swimlanes.Count + flowNodes.Count);

        foreach (var swimlane in swimlanes)
        {
            result.Add(swimlane);

            var inside = flowNodes.Where(node => swimlane.TypeName == "lane"
                ? laneOf[node.ElementId].Contains(swimlane.ElementId, StringComparer.Ordinal)
                : Owns(swimlane, node) && !laneOf[node.ElementId].Any());

            foreach (var node in inside)
            {
                if (!placed.Add(node.ElementId)) continue;

                result.Add(node with { Depth = node.Depth + swimlane.Depth });
            }
        }

        result.AddRange(flowNodes.Where(node => !placed.Contains(node.ElementId)));

        return result;
    }

    /// <summary>Whether a node belongs to the Process a Pool references.</summary>
    private static bool Owns(BpmnGraphNode pool, BpmnGraphNode node) =>
        pool.ContainerId is not null && string.Equals(node.ContainerId, pool.ContainerId, StringComparison.Ordinal);

    /// <summary>
    /// How deeply each node is nested, counted through owning containers.
    /// </summary>
    /// <remarks>
    /// A Sub-Process's contents sit a level below it, which is what a tree item's <c>aria-level</c>
    /// says and what lets a reader skip a whole branch instead of walking it. Counted by following
    /// container ids until one is not itself a drawn node, so nesting of any depth works without
    /// this knowing what a Sub-Process is.
    /// </remarks>
    private static Dictionary<string, int> DepthsOf(IReadOnlyList<BpmnFlowNode> nodes)
    {
        var owners = nodes.ToDictionary(node => node.Id, node => node.ContainerId, StringComparer.Ordinal);
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var node in nodes) depths[node.Id] = DepthOf(node.Id);

        return depths;

        int DepthOf(string id)
        {
            var depth = 1;
            var seen = new HashSet<string>(StringComparer.Ordinal) { id };
            var current = owners[id];

            // A container that is itself drawn adds a level; anything else is the plane's own root.
            while (current is not null && owners.ContainsKey(current) && seen.Add(current))
            {
                depth++;
                current = owners[current];
            }

            return depth;
        }
    }
}
