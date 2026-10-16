using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>Regenerates template UUIDs on each paste, retaining identity markers and unrelated Extras.</summary>
public sealed class BpmnTemplatePasteTransform(BpmnElementTemplateRegistry registry) : IBpmnPasteTransform
{
    /// <summary>Binding registry used to write regenerated values.</summary>
    public BpmnTemplateBindingHandlers Handlers { get; init; } = BpmnTemplateBindingHandlers.Default;
    /// <inheritdoc />
    public BpmnDocument Transform(BpmnDocument document, IReadOnlyList<string> pastedIds, EditingOptions options)
    {
        foreach (var id in pastedIds)
        {
            if (BpmnAppliedTemplate.Resolve(document, id, registry).Template is not { } template) continue;
            if (template.Flavor == BpmnTemplateFlavor.Camunda7) document = Camunda7TemplateBindingHandler.CloneScopedErrors(document, id, template);
            var values = BpmnTemplateValues.Read(document, id, template, Handlers);
            var active = BpmnTemplateValues.ActiveProperties(template, values);
            var clonedReferences = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (property, index) in BpmnTemplateScopes.Flatten(template).Properties.Select((property, index) => (property, index)))
            {
                if (property.GeneratedValue?.Type != "uuid"
                    || !active.Contains(BpmnTemplateValues.PropertyKey(property, index))
                    || property.Binding is not { } binding)
                {
                    continue;
                }

                var referenceKind = ReferenceKind(binding);
                if (referenceKind is not null && clonedReferences.Add(referenceKind))
                    document = CloneReferencedRoot(document, id, referenceKind == "signal");

                document = Handlers.Resolve(binding).Write(document, id, binding, BpmnTemplateValues.Normalize(property, Guid.NewGuid().ToString()), property, options);
            }
        }
        return document;
    }

    private static string? ReferenceKind(BpmnTemplateBinding binding) =>
        binding.Type.StartsWith("bpmn:Message#", StringComparison.Ordinal) ? "message"
        : binding.Type == "bpmn:Signal#property" ? "signal"
        : null;

    private static BpmnDocument CloneReferencedRoot(BpmnDocument document, string elementId, bool signal)
    {
        var node = document.FindFlowNode(elementId);
        if (node is null) return document;

        var referenceType = signal ? BpmnEventDefinitionType.Signal : BpmnEventDefinitionType.Message;
        var taskMessage = !signal && node.Type is BpmnFlowNodeType.ReceiveTask or BpmnFlowNodeType.SendTask;
        var reference = taskMessage
            ? node.MessageRef
            : node.EventDefinitions.FirstOrDefault(definition => definition.Type == referenceType)?.Ref;
        if (reference is null) return document;

        var newId = CreateUniqueId(document, reference);
        var definitionIndex = node.EventDefinitions.ToList().FindIndex(candidate => candidate.Type == referenceType);
        var updatedNode = taskMessage
            ? node with { MessageRef = newId }
            : node with
            {
                EventDefinitions = node.EventDefinitions.Select((definition, index) =>
                    index == definitionIndex ? definition with { Ref = newId } : definition).ToArray()
            };

        if (signal)
        {
            var definition = document.Catalog.Signals.FirstOrDefault(candidate => candidate.Id == reference);
            if (definition is null) return document;
            var clone = definition with { Id = newId, Extras = CopyExtras(definition.Extras) };
            document = document with { Catalog = document.Catalog with { Signals = [.. document.Catalog.Signals, clone] } };
        }
        else
        {
            var definition = document.Catalog.Messages.FirstOrDefault(candidate => candidate.Id == reference);
            if (definition is null) return document;
            var clone = definition with { Id = newId, Extras = CopyExtras(definition.Extras) };
            document = document with { Catalog = document.Catalog with { Messages = [.. document.Catalog.Messages, clone] } };
        }

        return document with
        {
            FlowNodes = document.FlowNodes.Select(candidate => candidate.Id == elementId ? updatedNode : candidate).ToArray()
        };
    }

    private static string CreateUniqueId(BpmnDocument document, string sourceId)
    {
        string id;
        do id = sourceId + "_copy_" + Guid.NewGuid().ToString("N");
        while (document.Find(id) is not null);
        return id;
    }

    private static Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.BpmnElementExtras CopyExtras(
        Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.BpmnElementExtras extras) => extras with
        {
            ForeignAttributes = extras.ForeignAttributes.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            Extensions = extras.Extensions.ToArray()
        };
}
