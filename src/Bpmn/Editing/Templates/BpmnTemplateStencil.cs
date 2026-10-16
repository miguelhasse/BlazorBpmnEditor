using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>Template palette entries and their atomic creation transaction.</summary>
public static class BpmnTemplateStencil
{
    /// <summary>Groups latest compatible templates by category, preserving container creation rules.</summary>
    public static IReadOnlyList<BpmnStencilGroup> GroupsFor(BpmnTemplateOptions options, IReadOnlyList<BpmnStencilGroup> context)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);
        if (!options.ShowInStencil) return [];
        var rules = context.SelectMany(group => group.Entries).ToArray();
        return options.Registry.LatestCompatible(options.Engines.Count == 0 ? null : options.Engines)
            .Select(template => EntryFor(template, rules))
            .OfType<BpmnStencilEntry>()
            .OrderBy(entry => entry.Template!.Category?.Name ?? "Other templates", StringComparer.Ordinal)
            .ThenBy(entry => entry.Template!.Name, StringComparer.Ordinal)
            .GroupBy(entry => entry.Template!.Category?.Id ?? string.Empty)
            .Select(category => new BpmnStencilGroup(category.First().Template!.Category?.Name ?? "Other templates",
                category.ToArray())).ToArray();
    }

    private static BpmnStencilEntry? EntryFor(BpmnElementTemplate template, IReadOnlyList<BpmnStencilEntry> rules)
    {
        var target = template.ElementType is { } elementType
            ? ConcreteType(elementType.Value)
            : template.AppliesTo.Select(ConcreteType).FirstOrDefault(value => value is not null)
                ?? (template.AppliesTo.Any(name => name is "bpmn:Activity" or "bpmn:FlowNode")
                    ? BpmnFlowNodeType.Task : null);
        if (target is not { } type || !BpmnTemplateSelection.MatchesType(template, type.ToString())) return null;
        var triggerName = template.ElementType?.EventDefinition?.Replace("bpmn:", string.Empty, StringComparison.Ordinal)
            .Replace("EventDefinition", string.Empty, StringComparison.Ordinal);
        var trigger = BpmnEventDefinitionType.None;
        if (triggerName is not null && (!Enum.TryParse(triggerName, out trigger) || !Enum.IsDefined(trigger))) return null;
        var rule = rules.FirstOrDefault(entry => entry.Type == type && entry.EventDefinition == trigger)
            ?? rules.FirstOrDefault(entry => entry.Type == type)
            ?? (type is BpmnFlowNodeType.IntermediateCatchEvent or BpmnFlowNodeType.BoundaryEvent
                ? rules.FirstOrDefault(entry => entry.Kind == BpmnStencilKind.IntermediateBoundaryEvent) : null)
            ?? (type.ToString().EndsWith("Task", StringComparison.Ordinal)
                ? rules.FirstOrDefault(entry => entry.Type == BpmnFlowNodeType.Task) : null);
        return new BpmnStencilEntry(type, template.Name, trigger,
            IsEnabled: rule?.IsEnabled ?? false, DisabledReason: rule?.DisabledReason ?? (rule is null ? "CT-001" : null),
            Kind: type == BpmnFlowNodeType.BoundaryEvent ? BpmnStencilKind.BoundaryEvent : BpmnStencilKind.FlowNode)
        {
            Template = template,
            Description = template.Description
        };
    }

    private static BpmnFlowNodeType? ConcreteType(string? name) =>
        name is not null && name.StartsWith("bpmn:", StringComparison.Ordinal) &&
        Enum.TryParse<BpmnFlowNodeType>(name[5..], out var type) && Enum.IsDefined(type) ? type : null;

    /// <summary>Uses the creation's preallocated id for defaults and markers, in one undo step.</summary>
    public static CompoundCommand CreateCommand(CreateElementCommand create, BpmnElementTemplate template,
        BpmnElementTemplateRegistry? registry = null) =>
        new([create, new ApplyElementTemplateCommand(create.Id, template, registry)], $"Create {template.Name}");
}
