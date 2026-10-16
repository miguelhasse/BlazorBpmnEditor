using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>Uses registered moddle child declarations when inserting extension elements.</summary>
public sealed class BpmnSchemaChildOrder(BpmnExtensionRegistry registry) : IBpmnExtensionChildOrder
{
    private static readonly Dictionary<string, int> ZeebeRootOrder = new(StringComparer.Ordinal)
    {
        ["taskDefinition"] = 0,
        ["ioMapping"] = 1,
        ["taskHeaders"] = 2,
        ["properties"] = 3,
    };

    /// <inheritdoc />
    public int InsertionIndex(BpmnExtensionElement? parent, IReadOnlyList<BpmnExtensionElement> siblings, string namespaceUri, string localName)
    {
        ArgumentNullException.ThrowIfNull(siblings);
        var parentType = parent is not null && registry.TryResolveElement(parent.Namespace, parent.LocalName, out var resolved, out _)
            ? resolved : null;
        for (var index = 0; index < siblings.Count; index++)
        {
            var sibling = siblings[index];
            var order = Compare(parentType, namespaceUri, localName, sibling.Namespace, sibling.LocalName);
            if (order < 0) return index;
        }
        return siblings.Count;
    }

    private int Compare(BpmnExtensionType? parent, string leftNamespace, string leftName, string rightNamespace, string rightName)
    {
        if (parent is not null && registry.TryGetSchema(parent.NamespaceUri, out _)
            && leftNamespace == rightNamespace && leftNamespace == parent.NamespaceUri)
        {
            return registry.CompareChildOrder(parent, leftName, rightName);
        }

        if (leftNamespace == rightNamespace && leftNamespace == "http://camunda.org/schema/zeebe/1.0"
            && ZeebeRootOrder.TryGetValue(leftName, out var leftZeebe)
            && ZeebeRootOrder.TryGetValue(rightName, out var rightZeebe))
            return leftZeebe.CompareTo(rightZeebe);

        var leftRank = Rank(leftNamespace, leftName);
        var rightRank = Rank(rightNamespace, rightName);
        if (leftRank is null || rightRank is null) return 0;
        return leftRank.Value.CompareTo(rightRank.Value);
    }

    private (int Schema, int Type)? Rank(string uri, string elementName)
    {
        var schemas = registry.Schemas;
        for (var schemaIndex = 0; schemaIndex < schemas.Count; schemaIndex++)
        {
            var schema = schemas[schemaIndex];
            if (schema.Uri != uri) continue;
            for (var typeIndex = 0; typeIndex < schema.Types.Count; typeIndex++)
                if (schema.Types[typeIndex].ElementName == elementName && schema.Types[typeIndex].Extends.Count == 0)
                    return (schemaIndex, typeIndex);
        }
        return null;
    }
}
