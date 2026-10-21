using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>Controls extension writes and schema-aware child insertion.</summary>
/// <param name="PruneEmpty">Remove empty target nodes along a removed-value path.</param>
/// <param name="ChildOrder">Optional extension-child ordering strategy.</param>
public sealed record BpmnExtensionWriteOptions(bool PruneEmpty = true, IBpmnExtensionChildOrder? ChildOrder = null);

/// <summary>Provides schema order for newly inserted extension elements.</summary>
public interface IBpmnExtensionChildOrder
{
    /// <summary>Returns insertion index for a new child among the supplied siblings.</summary>
    /// <param name="parent">Parent element, or null for the extension root.</param>
    /// <param name="siblings">Current siblings in document order.</param>
    /// <param name="namespaceUri">New child namespace URI.</param>
    /// <param name="localName">New child local name.</param>
    public int InsertionIndex(BpmnExtensionElement? parent, IReadOnlyList<BpmnExtensionElement> siblings, string namespaceUri, string localName);
}

/// <summary>Reads, changes and replaces namespace-addressed extension content.</summary>
public static class BpmnExtensionNavigator
{
    /// <summary>Reserved row metadata identifying the original matching item index; never written to XML.</summary>
    public const string CollectionRowIndexKey = "$index";

    internal static int CollectionRowIndex(IReadOnlyDictionary<string, string?> row) =>
        int.TryParse(row.GetValueOrDefault(CollectionRowIndexKey), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var index) ? index : -1;

    /// <summary>Reads the attribute or text addressed by a path.</summary>
    /// <param name="extras">Element extension data.</param>
    /// <param name="path">Expanded-name path to read.</param>
    /// <returns>Stored value, or null when the target is absent.</returns>
    public static string? Read(BpmnElementExtras extras, BpmnExtensionPath path)
    {
        ArgumentNullException.ThrowIfNull(extras);
        ArgumentNullException.ThrowIfNull(path);
        ValidatePath(path);
        var target = Resolve(extras.Extensions, path.Steps);
        if (target is null) return null;
        if (path.AttributeName is { } attribute) return target.Attributes.GetValueOrDefault(attribute);
        return target.Text;
    }

    /// <summary>Sets or removes an extension attribute or text value.</summary>
    /// <remarks>A null or empty value removes the target; empty nodes are pruned by default.</remarks>
    /// <param name="extras">Element extension data.</param>
    /// <param name="path">Expanded-name path to write.</param>
    /// <param name="value">Value to store, or null/empty to remove it.</param>
    /// <param name="options">Pruning and child ordering options.</param>
    /// <returns>Updated extension data, preserving untouched references.</returns>
    public static BpmnElementExtras Write(
        BpmnElementExtras extras,
        BpmnExtensionPath path,
        string? value,
        BpmnExtensionWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(extras);
        ArgumentNullException.ThrowIfNull(path);
        ValidatePath(path);
        options ??= new BpmnExtensionWriteOptions();
        var remove = string.IsNullOrEmpty(value);
        if (remove)
        {
            var current = Resolve(extras.Extensions, path.Steps);
            if (current is null) return extras;
            if (path.AttributeName is { } name && !current.Attributes.ContainsKey(name)) return extras;
            if (path.AttributeName is null && string.IsNullOrEmpty(current.Text)) return extras;
        }
        var updated = RewritePath(extras.Extensions, path.Steps, 0, null, options,
            element => WriteValue(element, path.AttributeName, value), remove && options.PruneEmpty);
        return ReferenceEquals(updated, extras.Extensions) ? extras : extras with { Extensions = updated };
    }

    /// <summary>Removes the addressed attribute, text, or extension element.</summary>
    /// <param name="extras">Element extension data.</param>
    /// <param name="path">Expanded-name path to remove.</param>
    /// <returns>Updated extension data.</returns>
    public static BpmnElementExtras Remove(BpmnElementExtras extras, BpmnExtensionPath path)
    {
        ArgumentNullException.ThrowIfNull(extras);
        ArgumentNullException.ThrowIfNull(path);
        ValidatePath(path);
        if (path.AttributeName is null && !path.IsText)
        {
            var removed = RemoveElement(extras.Extensions, path.Steps, 0);
            return ReferenceEquals(removed, extras.Extensions) ? extras : extras with { Extensions = removed };
        }
        var existing = Resolve(extras.Extensions, path.Steps);
        if (existing is null) return extras;
        if (path.AttributeName is { } name && !existing.Attributes.ContainsKey(name)) return extras;
        if (path.AttributeName is null && string.IsNullOrEmpty(existing.Text)) return extras;
        var options = new BpmnExtensionWriteOptions(PruneEmpty: true);
        var rewritten = RewritePath(extras.Extensions, path.Steps, 0, null, options,
            element => WriteValue(element, path.AttributeName, null), pruneEmpty: true);
        return ReferenceEquals(rewritten, extras.Extensions) ? extras : extras with { Extensions = rewritten };
    }

    /// <summary>Returns matching item elements in their current sibling order.</summary>
    /// <param name="extras">Element extension data.</param>
    /// <param name="containerPath">Path to collection container.</param>
    /// <param name="itemNamespace">Item namespace URI.</param>
    /// <param name="itemLocalName">Item local name.</param>
    /// <returns>Matching item elements in document order.</returns>
    public static IReadOnlyList<BpmnExtensionElement> ReadCollection(
        BpmnElementExtras extras,
        BpmnExtensionPath containerPath,
        string itemNamespace,
        string itemLocalName)
    {
        ArgumentNullException.ThrowIfNull(extras);
        ArgumentNullException.ThrowIfNull(containerPath);
        ValidatePath(containerPath);
        if (containerPath.AttributeName is not null || containerPath.IsText) throw new ArgumentException("Collection path must address an element.", nameof(containerPath));
        var container = Resolve(extras.Extensions, containerPath.Steps);
        return container?.Children
            .Where(child => child.Namespace == itemNamespace && child.LocalName == itemLocalName)
            .ToArray() ?? [];
    }

    /// <summary>Replaces matching collection rows while retaining unknown item content.</summary>
    /// <param name="extras">Element extension data.</param>
    /// <param name="containerPath">Path to collection container, created when rows are non-empty.</param>
    /// <param name="itemNamespace">Item namespace URI.</param>
    /// <param name="itemLocalName">Item local name.</param>
    /// <param name="rows">Attribute maps; <c>#text</c> supplies item text and <c>$index</c> identifies the
    /// original matching item index. Missing, invalid, out-of-range or duplicated indices create new items.
    /// If no row contains <c>$index</c>, items are reused by position for backward compatibility.</param>
    /// <returns>Updated extension data.</returns>
    public static BpmnElementExtras ReplaceCollection(
        BpmnElementExtras extras,
        BpmnExtensionPath containerPath,
        string itemNamespace,
        string itemLocalName,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows) =>
        ReplaceCollection(extras, containerPath, itemNamespace, itemLocalName, rows, new BpmnExtensionWriteOptions());

    internal static BpmnElementExtras ReplaceCollection(
        BpmnElementExtras extras,
        BpmnExtensionPath containerPath,
        string itemNamespace,
        string itemLocalName,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> rows,
        BpmnExtensionWriteOptions options)
    {
        ArgumentNullException.ThrowIfNull(extras);
        ArgumentNullException.ThrowIfNull(containerPath);
        ArgumentNullException.ThrowIfNull(rows);
        ValidatePath(containerPath);
        if (containerPath.AttributeName is not null || containerPath.IsText) throw new ArgumentException("Collection path must address an element.", nameof(containerPath));
        ArgumentNullException.ThrowIfNull(options);
        var oldContainer = Resolve(extras.Extensions, containerPath.Steps);
        if (rows.Count == 0 && oldContainer is null) return extras;
        if (rows.Count == 0 && oldContainer is not null && Empty(oldContainer)) return Remove(extras, containerPath);

        var rewritten = RewritePath(extras.Extensions, containerPath.Steps, 0, null, options, container =>
        {
            var oldItems = container.Children
                .Where(child => child.Namespace == itemNamespace && child.LocalName == itemLocalName)
                .ToArray();
            var knownAttributes = rows.SelectMany(row => row.Keys)
                .Where(key => key != "#text" && key != CollectionRowIndexKey)
                .ToHashSet(StringComparer.Ordinal);
            var textIsKnown = rows.Any(row => row.ContainsKey("#text"));
            var indexed = rows.Any(row => row.ContainsKey(CollectionRowIndexKey));
            var indexCounts = rows.Select(CollectionRowIndex).GroupBy(index => index)
                .ToDictionary(group => group.Key, group => group.Count());
            var replacements = new List<BpmnExtensionElement>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var originalIndex = indexed ? CollectionRowIndex(row) : i;
                var old = originalIndex >= 0 && originalIndex < oldItems.Length
                    && (!indexed || indexCounts[originalIndex] == 1)
                    ? oldItems[originalIndex]
                    : new BpmnExtensionElement(itemNamespace, itemLocalName,
                        new Dictionary<string, string>(StringComparer.Ordinal), [], null);
                var attributes = new Dictionary<string, string>(old.Attributes, StringComparer.Ordinal);
                foreach (var key in knownAttributes) attributes.Remove(key);
                foreach (var (key, fieldValue) in row)
                {
                    if (key == "#text" || key == CollectionRowIndexKey) continue;
                    if (string.IsNullOrEmpty(fieldValue)) attributes.Remove(key);
                    else attributes[key] = fieldValue;
                }
                var text = textIsKnown ? row.GetValueOrDefault("#text") : old.Text;
                replacements.Add(old with { Attributes = SameAttributes(old.Attributes, attributes) ? old.Attributes : attributes, Text = text });
            }

            if (oldItems.Length == rows.Count
                && !oldItems.Where((item, index) => !SameElement(item, replacements[index])).Any())
            {
                return container;
            }

            var children = new List<BpmnExtensionElement>(container.Children.Count + Math.Max(0, rows.Count - oldItems.Length));
            var itemIndex = 0;
            foreach (var child in container.Children)
            {
                if (child.Namespace == itemNamespace && child.LocalName == itemLocalName)
                {
                    if (itemIndex < replacements.Count) children.Add(replacements[itemIndex]);
                    itemIndex++;
                }
                else children.Add(child);
            }
            while (itemIndex < replacements.Count)
            {
                var newItem = replacements[itemIndex++];
                var at = InsertionIndex(options.ChildOrder, container, children, itemNamespace, itemLocalName);
                children.Insert(at, newItem);
            }
            return container with { Children = children };
        }, pruneEmpty: rows.Count == 0);
        return ReferenceEquals(rewritten, extras.Extensions) ? extras : extras with { Extensions = rewritten };
    }

    /// <summary>Adds a child at the top level or under an already-resolved parent path.</summary>
    internal static BpmnElementExtras AddChild(
        BpmnElementExtras extras,
        BpmnExtensionPath? parentPath,
        BpmnExtensionElement child,
        BpmnExtensionWriteOptions? options = null)
    {
        options ??= new BpmnExtensionWriteOptions();
        if (parentPath is null)
        {
            var children = extras.Extensions.ToList();
            children.Insert(InsertionIndex(options.ChildOrder, null, children, child.Namespace, child.LocalName), child);
            return extras with { Extensions = children };
        }
        if (parentPath.AttributeName is not null || parentPath.IsText) throw new ArgumentException("Parent path must address an element.", nameof(parentPath));
        if (Resolve(extras.Extensions, parentPath.Steps) is null) throw new InvalidOperationException("Extension parent path does not exist.");
        var updated = RewritePath(extras.Extensions, parentPath.Steps, 0, null, options, parent =>
        {
            var children = parent.Children.ToList();
            children.Insert(InsertionIndex(options.ChildOrder, parent, children, child.Namespace, child.LocalName), child);
            return parent with { Children = children };
        }, pruneEmpty: false);
        return ReferenceEquals(updated, extras.Extensions) ? extras : extras with { Extensions = updated };
    }

    internal static BpmnExtensionElement? Resolve(IReadOnlyList<BpmnExtensionElement> roots, IReadOnlyList<BpmnExtensionPathStep> steps)
    {
        IReadOnlyList<BpmnExtensionElement> siblings = roots;
        BpmnExtensionElement? found = null;
        foreach (var step in steps)
        {
            var index = FindIndex(siblings, step);
            if (index < 0) return null;
            found = siblings[index];
            siblings = found.Children;
        }
        return found;
    }

    internal static BpmnElementExtras UpdateElement(BpmnElementExtras extras, BpmnExtensionPath path,
        Func<BpmnExtensionElement, BpmnExtensionElement> update, BpmnExtensionWriteOptions? options = null) =>
        extras with { Extensions = RewritePath(extras.Extensions, path.Steps, 0, null, options ?? new(), update, false) };

    private static IReadOnlyList<BpmnExtensionElement> RewritePath(
        IReadOnlyList<BpmnExtensionElement> siblings,
        IReadOnlyList<BpmnExtensionPathStep> steps,
        int depth,
        BpmnExtensionElement? parent,
        BpmnExtensionWriteOptions options,
        Func<BpmnExtensionElement, BpmnExtensionElement> update,
        bool pruneEmpty)
    {
        if (steps.Count == 0) return siblings;
        var step = steps[depth];
        var index = FindIndex(siblings, step);
        var copy = siblings.ToList();
        if (index < 0)
        {
            if (pruneEmpty) return siblings;
            var created = CreateFor(step);
            index = InsertCreated(copy, created, step, options.ChildOrder, parent);
        }

        var original = copy[index];
        var replacement = depth == steps.Count - 1
            ? update(original)
            : original with
            {
                Children = RewritePath(original.Children, steps, depth + 1, original, options, update, pruneEmpty),
            };
        if (SameElement(original, replacement)) return siblings;
        if (pruneEmpty && Empty(replacement)) copy.RemoveAt(index);
        else copy[index] = replacement;
        return copy;
    }

    private static IReadOnlyList<BpmnExtensionElement> RemoveElement(
        IReadOnlyList<BpmnExtensionElement> siblings,
        IReadOnlyList<BpmnExtensionPathStep> steps,
        int depth)
    {
        if (steps.Count == 0) return siblings;
        var index = FindIndex(siblings, steps[depth]);
        if (index < 0) return siblings;
        var copy = siblings.ToList();
        if (depth == steps.Count - 1) copy.RemoveAt(index);
        else
        {
            var found = copy[index];
            var children = RemoveElement(found.Children, steps, depth + 1);
            if (ReferenceEquals(children, found.Children)) return siblings;
            var replacement = found with { Children = children };
            if (Empty(replacement)) copy.RemoveAt(index);
            else copy[index] = replacement;
        }
        return copy;
    }

    private static BpmnExtensionElement WriteValue(BpmnExtensionElement element, string? attribute, string? value)
    {
        if (attribute is null)
        {
            var text = string.IsNullOrEmpty(value) ? null : value;
            return element.Text == text ? element : element with { Text = text };
        }
        if (element.Attributes.TryGetValue(attribute, out var existing) && existing == value) return element;
        if (string.IsNullOrEmpty(value) && !element.Attributes.ContainsKey(attribute)) return element;
        var attributes = new Dictionary<string, string>(element.Attributes, StringComparer.Ordinal);
        if (string.IsNullOrEmpty(value)) attributes.Remove(attribute);
        else attributes[attribute] = value;
        return element with { Attributes = attributes };
    }

    private static int FindIndex(IReadOnlyList<BpmnExtensionElement> siblings, BpmnExtensionPathStep step)
    {
        var matches = new List<int>();
        for (var i = 0; i < siblings.Count; i++)
        {
            var item = siblings[i];
            if (item.Namespace == step.NamespaceUri && item.LocalName == step.LocalName) matches.Add(i);
        }
        if (step.Selector is null) return matches.Count == 0 ? -1 : matches[0];
        if (step.Selector.Ordinal is { } ordinal) return ordinal < matches.Count ? matches[ordinal] : -1;
        foreach (var index in matches)
        {
            var item = siblings[index];
            if (item.Attributes.TryGetValue(step.Selector.AttributeName!, out var value)
                && value == step.Selector.AttributeValue) return index;
        }
        return -1;
    }

    private static BpmnExtensionElement CreateFor(BpmnExtensionPathStep step)
    {
        IReadOnlyDictionary<string, string> attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (step.Selector is { AttributeName: { } name, AttributeValue: { } value })
            attributes = new Dictionary<string, string>(StringComparer.Ordinal) { [name] = value };
        return new BpmnExtensionElement(step.NamespaceUri, step.LocalName, attributes, [], null);
    }

    private static int InsertCreated(
        List<BpmnExtensionElement> siblings,
        BpmnExtensionElement created,
        BpmnExtensionPathStep step,
        IBpmnExtensionChildOrder? childOrder,
        BpmnExtensionElement? parent)
    {
        if (step.Selector?.Ordinal is { } ordinal)
        {
            var matching = siblings.Select((element, index) => (element, index))
                .Where(pair => pair.element.Namespace == step.NamespaceUri && pair.element.LocalName == step.LocalName)
                .Select(pair => pair.index).ToArray();
            while (matching.Length <= ordinal)
            {
                var at = matching.Length == ordinal ? (matching.Length == 0 ? siblings.Count : matching[^1] + 1) : siblings.Count;
                var filler = CreateFor(step with { Selector = null });
                siblings.Insert(at, filler);
                matching = siblings.Select((element, index) => (element, index))
                    .Where(pair => pair.element.Namespace == step.NamespaceUri && pair.element.LocalName == step.LocalName)
                    .Select(pair => pair.index).ToArray();
            }
            var targetIndex = matching[ordinal];
            siblings[targetIndex] = created;
            return targetIndex;
        }
        var index = InsertionIndex(childOrder, parent, siblings, created.Namespace, created.LocalName);
        siblings.Insert(index, created);
        return index;
    }

    private static int InsertionIndex(
        IBpmnExtensionChildOrder? childOrder,
        BpmnExtensionElement? parent,
        List<BpmnExtensionElement> siblings,
        string namespaceUri,
        string localName)
    {
        var index = childOrder?.InsertionIndex(parent, siblings, namespaceUri, localName) ?? siblings.Count;
        return Math.Clamp(index, 0, siblings.Count);
    }

    private static void ValidatePath(BpmnExtensionPath path)
    {
        if (path.Steps is null || path.Steps.Count == 0) throw new ArgumentException("Extension path must contain at least one element step.", nameof(path));
        if (path.AttributeName is not null && path.IsText) throw new ArgumentException("Path cannot address both an attribute and text.", nameof(path));
    }

    private static bool Empty(BpmnExtensionElement element) =>
        element.Attributes.Count == 0 && element.Children.Count == 0 && string.IsNullOrEmpty(element.Text);

    private static bool SameAttributes(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static bool SameElement(BpmnExtensionElement left, BpmnExtensionElement right) =>
        ReferenceEquals(left, right) || (left.Namespace == right.Namespace && left.LocalName == right.LocalName
            && left.Text == right.Text && SameAttributes(left.Attributes, right.Attributes)
            && left.Children.Count == right.Children.Count
            && !left.Children.Where((child, index) => !SameElement(child, right.Children[index])).Any());
}
