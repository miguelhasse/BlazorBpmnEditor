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

/// <summary>Groups standard property descriptors by BPMN concept without changing command routing.</summary>
public sealed class BpmnGeneralPropertiesProvider : IBpmnPropertiesProvider
{
    /// <inheritdoc />
    public int Priority => 1000;

    /// <inheritdoc />
    public IReadOnlyList<BpmnPropertyGroup> GetGroups(BpmnElementSnapshot element, IReadOnlyList<BpmnPropertyGroup> groups)
    {
        if (element.Properties.Count == 0)
        {
            return groups;
        }

        var semanticGroups = element.Properties.GroupBy(property => property.Group).ToDictionary(group => group.Key,
            group => new BpmnPropertyGroup(GroupOf(group.Key).Id, GroupOf(group.Key).Label, group.Select(FromDescriptor).ToList())
            {
                HasBuiltInLabels = true
            });
        var kindsById = semanticGroups.Keys.ToDictionary(kind => GroupOf(kind).Id);

        BpmnPropertyGroup BuildGroup(BpmnPropertyGroupKind kind)
        {
            var group = semanticGroups[kind];
            var children = semanticGroups.Keys.Where(candidate => GroupOf(candidate).ParentId == group.Id)
                .Order().Select(BuildGroup).ToArray();
            return group with { Children = children };
        }

        var roots = semanticGroups.Keys.Where(kind => GroupOf(kind).ParentId is not { } parentId
                || !kindsById.ContainsKey(parentId))
            .Order().Select(BuildGroup);

        return [.. groups, .. roots];
    }

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
            EditorKey = property.Name == "messageRef" && property.Kind == BpmnPropertyKind.Choice && property.Group is BpmnPropertyGroupKind.SendTask
                or BpmnPropertyGroupKind.ReceiveTask or BpmnPropertyGroupKind.MessageFlow ? "bpmn:messageReference" : null,
            ExpressionLanguage = IsExpression(property.Name)
                ? property.Value?.StartsWith('=') == true ? BpmnExpressionLanguage.Feel : BpmnExpressionLanguage.Juel
                : BpmnExpressionLanguage.None,
            Choices = property.Choices,
            IsReadOnly = property.IsReadOnly,
            GroupId = GroupOf(property.Group).Id,
        };

    private static (string Id, string Label, string? ParentId) GroupOf(BpmnPropertyGroupKind group) => group switch
    {
        BpmnPropertyGroupKind.General => ("general", "General", null),
        BpmnPropertyGroupKind.Activity => ("activity", "Activity", null),
        BpmnPropertyGroupKind.Event => ("event", "Event", null),
        BpmnPropertyGroupKind.ScriptTask => ("script-task", "Script Task", null),
        BpmnPropertyGroupKind.CallActivity => ("call-activity", "Call Activity", null),
        BpmnPropertyGroupKind.SendTask => ("send-task", "Send Task", null),
        BpmnPropertyGroupKind.ReceiveTask => ("receive-task", "Receive Task", null),
        BpmnPropertyGroupKind.Gateway => ("gateway", "Gateway", null),
        BpmnPropertyGroupKind.ComplexGateway => ("complex-gateway", "Complex Gateway", null),
        BpmnPropertyGroupKind.SequenceFlow => ("sequence-flow", "Sequence Flow", null),
        BpmnPropertyGroupKind.MessageFlow => ("message-flow", "Message Flow", null),
        BpmnPropertyGroupKind.Association => ("association", "Association", null),
        BpmnPropertyGroupKind.Assignment => ("assignment", "Assignment", null),
        BpmnPropertyGroupKind.TextAnnotation => ("text-annotation", "Text Annotation", null),
        BpmnPropertyGroupKind.Group => ("group", "Group", null),
        BpmnPropertyGroupKind.DataObjectReference => ("data-object-reference", "Data Object Reference", null),
        BpmnPropertyGroupKind.DataStoreReference => ("data-store-reference", "Data Store Reference", null),
        BpmnPropertyGroupKind.DataState => ("data-state", "Data State", null),
        BpmnPropertyGroupKind.Process => ("process", "Process", null),
        BpmnPropertyGroupKind.LoopCharacteristics => ("loop-characteristics", "Loop Characteristics", null),
        BpmnPropertyGroupKind.MultiInstanceInputDataItem =>
            ("multi-instance-input-data-item", "Input Data Item", "loop-characteristics"),
        BpmnPropertyGroupKind.MultiInstanceOutputDataItem =>
            ("multi-instance-output-data-item", "Output Data Item", "loop-characteristics"),
        BpmnPropertyGroupKind.ComplexBehaviorDefinition =>
            ("complex-behavior-definition", "Complex Behavior Definition", "loop-characteristics"),
        _ => throw new ArgumentOutOfRangeException(nameof(group), group, "Unknown BPMN property group."),
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
