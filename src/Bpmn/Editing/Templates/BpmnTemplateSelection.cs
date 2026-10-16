using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>Shared panel and validation compatibility selection.</summary>
public static class BpmnTemplateSelection
{
    /// <summary>Gets latest compatible, nondeprecated templates matching the snapshot's BPMN wire type.</summary>
    public static IReadOnlyList<BpmnElementTemplate> Applicable(BpmnElementSnapshot element, BpmnTemplateOptions options) =>
        options.Registry.ApplicableTo(element.ElementType,
            element.EventDefinitions.Count > 0 ? "bpmn:" + element.EventDefinitions[0].Type + "EventDefinition" : null,
            options.Engines.Count == 0 ? null : options.Engines, isA: BpmnTypeNames.IsA);

    internal static bool MatchesType(BpmnElementTemplate template, string wireType) =>
        template.AppliesTo.Any(type => BpmnTypeNames.IsA(wireType, type));

    /// <summary>Gets a newer compatible version of the applied template.</summary>
    public static BpmnElementTemplate? Upgrade(BpmnElementSnapshot element, BpmnElementTemplate applied, BpmnTemplateOptions options) =>
        Applicable(element, options).FirstOrDefault(template => template.Id == applied.Id && template.Version is { } version && version > (applied.Version ?? -1));
}
