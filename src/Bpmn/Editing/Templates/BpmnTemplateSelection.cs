using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>Shared panel and validation compatibility selection.</summary>
public static class BpmnTemplateSelection
{
    /// <summary>Gets latest compatible, nondeprecated templates matching the snapshot's BPMN wire type.</summary>
    public static IReadOnlyList<BpmnElementTemplate> Applicable(BpmnElementSnapshot element, BpmnTemplateOptions options) =>
        Applicable(element.ElementType, element.EventDefinitions, options);

    internal static bool MatchesType(BpmnElementTemplate template, string wireType) =>
        template.AppliesTo.Any(type => BpmnTypeNames.IsA(wireType, type));

    /// <summary>Gets a newer compatible version of the applied template.</summary>
    public static BpmnElementTemplate? Upgrade(BpmnElementSnapshot element, BpmnElementTemplate applied, BpmnTemplateOptions options) =>
        Newer(Applicable(element, options), applied);

    internal static BpmnElementTemplate? Upgrade(BpmnDocument document, string elementId, BpmnElementTemplate applied, BpmnTemplateOptions options)
    {
        if (document.Find(elementId) is not { } element) return null;
        // Upgrade selection reads only notation identity, not property-panel geometry, diagnostics or localized captions.
        return Newer(Applicable(BpmnElementInspector.TypeNameOf(element),
            element is BpmnFlowNode node ? node.EventDefinitions : [], options), applied);
    }

    private static IReadOnlyList<BpmnElementTemplate> Applicable(
        string elementType, IReadOnlyList<BpmnEventDefinition> definitions, BpmnTemplateOptions options) =>
        options.Registry.ApplicableTo(elementType,
            definitions.Count > 0 ? "bpmn:" + definitions[0].Type + "EventDefinition" : null,
            options.Engines.Count == 0 ? null : options.Engines, isA: BpmnTypeNames.IsA);

    private static BpmnElementTemplate? Newer(IReadOnlyList<BpmnElementTemplate> templates, BpmnElementTemplate applied) =>
        templates.FirstOrDefault(template => template.Id == applied.Id && template.Version is { } version && version > (applied.Version ?? -1));
}
