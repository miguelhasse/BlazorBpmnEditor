namespace Hasseware.AspNetCore.Components.Bpmn.Accessibility;

/// <summary>Which way a keystroke moves through the diagram.</summary>
public enum BpmnGraphDirection
{
    /// <summary>Along a Sequence Flow, in the direction the arrow points.</summary>
    Forward,

    /// <summary>Back up a Sequence Flow, against the arrow.</summary>
    Back,

    /// <summary>The next branch out of the same source, or the next element in reading order.</summary>
    Next,

    /// <summary>The previous branch, or the previous element in reading order.</summary>
    Previous,

    /// <summary>The first element in reading order.</summary>
    First,

    /// <summary>The last element in reading order.</summary>
    Last,
}

/// <summary>
/// Moving through the diagram with the arrow keys (implementation plan M7).
/// </summary>
/// <remarks>
/// <para>
/// Pure, and deliberately: what the arrow keys should do on a branching graph is a design decision
/// that wants tests with named cases, not a thing to be inferred from watching a browser. The
/// component that binds keys to it holds no rules of its own.
/// </para>
/// <para>
/// The model is: left and right walk the flow, up and down choose between branches. Following a
/// Sequence Flow is what makes this a graph rather than a list, and on a Gateway with three
/// outgoing branches the reader needs to hear all three without leaving the Gateway — so Next and
/// Previous move between the alternatives out of the shared predecessor, and fall back to reading
/// order only when there is no branch to choose from.
/// </para>
/// </remarks>
public static class BpmnGraphNavigation
{
    /// <summary>
    /// Where a keystroke lands, or <see langword="null"/> when there is nowhere to go.
    /// </summary>
    /// <remarks>
    /// Null rather than staying put, so the caller can decide whether that is silence or a bump.
    /// A move that would leave the diagram is not an error.
    /// </remarks>
    /// <param name="nodes">The diagram, in reading order.</param>
    /// <param name="from">The element the reader is on, or <see langword="null"/> when nowhere.</param>
    /// <param name="direction">Which way to go.</param>
    public static string? Move(IReadOnlyList<BpmnGraphNode> nodes, string? from, BpmnGraphDirection direction)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        if (nodes.Count == 0) return null;

        if (direction == BpmnGraphDirection.First) return nodes[0].ElementId;
        if (direction == BpmnGraphDirection.Last) return nodes[^1].ElementId;

        // Nowhere yet: any key lands on the first element rather than doing nothing, so the very
        // first keystroke is never the one that appears broken.
        var index = from is null ? -1 : IndexOf(nodes, from);
        if (index < 0) return nodes[0].ElementId;

        var node = nodes[index];

        return direction switch
        {
            BpmnGraphDirection.Forward => node.Outgoing.Count > 0 ? node.Outgoing[0] : null,
            BpmnGraphDirection.Back => node.Incoming.Count > 0 ? node.Incoming[0] : null,
            BpmnGraphDirection.Next => Sibling(nodes, node, index, +1),
            BpmnGraphDirection.Previous => Sibling(nodes, node, index, -1),
            _ => null,
        };
    }

    /// <summary>
    /// The next or previous alternative to this element.
    /// </summary>
    /// <remarks>
    /// A branch first: if this element was reached along a flow from something with several
    /// outgoing flows, the alternatives are the other targets of that same source, and stepping
    /// between them is how a reader hears what a Gateway decides between. Only when there is no
    /// such fork does this fall back to reading order, which is what makes the tree still walkable
    /// end to end on a diagram that has no branching at all.
    /// </remarks>
    private static string? Sibling(IReadOnlyList<BpmnGraphNode> nodes, BpmnGraphNode node, int index, int step)
    {
        if (node.Incoming.Count > 0 && Find(nodes, node.Incoming[0]) is { } parent && parent.Outgoing.Count > 1)
        {
            var among = parent.Outgoing;
            var position = IndexOf(among, node.ElementId);

            if (position >= 0)
            {
                var next = position + step;
                if (next >= 0 && next < among.Count) return among[next];
            }
        }

        var walked = index + step;
        return walked >= 0 && walked < nodes.Count ? nodes[walked].ElementId : null;
    }

    private static BpmnGraphNode? Find(IReadOnlyList<BpmnGraphNode> nodes, string elementId)
    {
        var index = IndexOf(nodes, elementId);
        return index < 0 ? null : nodes[index];
    }

    private static int IndexOf(IReadOnlyList<BpmnGraphNode> nodes, string elementId)
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            if (string.Equals(nodes[index].ElementId, elementId, StringComparison.Ordinal)) return index;
        }

        return -1;
    }

    private static int IndexOf(IReadOnlyList<string> ids, string elementId)
    {
        for (var index = 0; index < ids.Count; index++)
        {
            if (string.Equals(ids[index], elementId, StringComparison.Ordinal)) return index;
        }

        return -1;
    }
}
