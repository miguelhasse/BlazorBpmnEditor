using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>Composes property groups from a snapshot, in descending priority order.</summary>
public interface IBpmnPropertiesProvider
{
    /// <summary>Providers with greater priority run first.</summary>
    public int Priority => 1000;

    /// <summary>Returns the groups passed to the next provider.</summary>
    public IReadOnlyList<BpmnPropertyGroup> GetGroups(BpmnElementSnapshot element, IReadOnlyList<BpmnPropertyGroup> groups);
}

/// <summary>Wraps standard property descriptors without changing their command routing.</summary>
public sealed class BpmnGeneralPropertiesProvider : IBpmnPropertiesProvider
{
    /// <inheritdoc />
    public int Priority => 1000;

    /// <inheritdoc />
    public IReadOnlyList<BpmnPropertyGroup> GetGroups(BpmnElementSnapshot element, IReadOnlyList<BpmnPropertyGroup> groups) =>
        element.Properties.Count == 0 ? groups :
        [.. groups, new("general", "General", element.Properties.Select(property => FromDescriptor(property) with { GroupId = "general" }).ToList()) { HasBuiltInLabels = true }];

    /// <summary>Maps a descriptor to an editable property entry.</summary>
    /// <param name="property">The descriptor.</param>
    /// <returns>The corresponding entry.</returns>
    public static BpmnPropertyEntry FromDescriptor(BpmnPropertyDescriptor property) =>
        new(property.Name, property.Label, property.Kind switch
        {
            BpmnPropertyKind.MultilineText => BpmnPropertyEntryKind.TextArea,
            BpmnPropertyKind.Boolean => BpmnPropertyEntryKind.Checkbox,
            BpmnPropertyKind.Choice => BpmnPropertyEntryKind.Select,
            _ => BpmnPropertyEntryKind.Text,
        }, property.Value)
        {
            ExpressionLanguage = IsExpression(property.Name)
                ? property.Value?.StartsWith('=') == true ? BpmnExpressionLanguage.Feel : BpmnExpressionLanguage.Juel
                : BpmnExpressionLanguage.None,
            Choices = property.Choices,
            IsReadOnly = property.IsReadOnly,
        };
    private static bool IsExpression(string name) => name is "conditionExpression" or "multiInstance.completionCondition"
        or "multiInstance.loopCardinality" or "standardLoop.loopCondition" or "activationCondition" or "calledElementRef"
        || name.EndsWith(".timeDate", StringComparison.Ordinal) || name.EndsWith(".timeDuration", StringComparison.Ordinal)
        || name.EndsWith(".timeCycle", StringComparison.Ordinal);
}

/// <summary>Wraps opaque extension paths as a visibly separate legacy fallback.</summary>
public sealed class BpmnRawExtensionPropertiesProvider : IBpmnPropertiesProvider
{
    /// <inheritdoc />
    public int Priority => 100;

    /// <inheritdoc />
    public IReadOnlyList<BpmnPropertyGroup> GetGroups(BpmnElementSnapshot element, IReadOnlyList<BpmnPropertyGroup> groups) =>
        element.ExtensionProperties.Count == 0 ? groups :
        [.. groups, new("extensions", "Extensions", element.ExtensionProperties.Select(property =>
            BpmnGeneralPropertiesProvider.FromDescriptor(property) with { GroupId = "extensions" }).ToList()) { Namespace = "*", HasBuiltInLabels = true }];
}
