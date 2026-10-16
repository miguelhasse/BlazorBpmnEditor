using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

internal sealed class ReferencedTemplateBindingHandler : IBpmnTemplateBindingHandler
{
    public bool CanHandle(BpmnTemplateBinding binding) => binding.Type is "bpmn:Message#property" or "bpmn:Message#zeebe:subscription#property" or "bpmn:Signal#property" or "bpmn:TimerEventDefinition#property" or "bpmn:ConditionalEventDefinition#property" or "bpmn:ConditionalEventDefinition#zeebe:conditionalFilter#property";

    public string? Read(BpmnDocument document, string elementId, BpmnTemplateBinding binding)
    {
        var node = document.FindFlowNode(elementId) ?? throw new InvalidOperationException("Referenced binding needs a flow node.");
        if (binding.Type.StartsWith("bpmn:Message#", StringComparison.Ordinal) || binding.Type == "bpmn:Signal#property")
        {
            var signal = binding.Type == "bpmn:Signal#property";
            var reference = Reference(node, signal);
            var root = reference is null ? null : document.Find(reference);
            if (binding.Type == "bpmn:Message#zeebe:subscription#property") return root is BpmnMessage message
                ? BpmnExtensionNavigator.Read(message.Extras, ZeebeTemplateBindingHandler.Attribute("subscription", binding.Name ?? binding.Property ?? "correlationKey")) : null;
            return root switch { BpmnMessage message => message.Name, BpmnSignal value => value.Name, _ => null };
        }
        var definition = Definition(node, binding);
        if (binding.Type.EndsWith("#zeebe:conditionalFilter#property", StringComparison.Ordinal))
            return BpmnExtensionNavigator.Read(definition.Extras, ZeebeTemplateBindingHandler.Attribute("conditionalFilter", binding.Name ?? binding.Property ?? throw new InvalidOperationException("Conditional filter binding needs name.")));
        var name = ExpressionName(binding);
        return definition.ExpressionKind == name || definition.Type == BpmnEventDefinitionType.Conditional ? definition.Expression : null;
    }

    public BpmnDocument Write(BpmnDocument document, string elementId, BpmnTemplateBinding binding, string? value, BpmnTemplateProperty property, EditingOptions options) =>
        Change(document, elementId, binding, value, options, false);
    public BpmnDocument Remove(BpmnDocument document, string elementId, BpmnTemplateBinding binding, EditingOptions options) =>
        Change(document, elementId, binding, null, options, true);

    private static BpmnDocument Change(BpmnDocument document, string elementId, BpmnTemplateBinding binding, string? value, EditingOptions options, bool remove)
    {
        var node = document.FindFlowNode(elementId) ?? throw new InvalidOperationException("Referenced binding needs a flow node.");
        if (binding.Type.StartsWith("bpmn:Message#", StringComparison.Ordinal) || binding.Type == "bpmn:Signal#property")
        {
            var signal = binding.Type == "bpmn:Signal#property";
            var reference = Reference(node, signal);
            var root = reference is null ? null : document.Find(reference);
            if (root is null)
            {
                if (remove) return document;
                if (!signal && node.Type is not (BpmnFlowNodeType.ReceiveTask or BpmnFlowNodeType.SendTask) && !node.EventDefinitions.Any(d => d.Type == BpmnEventDefinitionType.Message))
                    throw new InvalidOperationException("Message binding needs a message event or send/receive task.");
                if (signal && !node.EventDefinitions.Any(d => d.Type == BpmnEventDefinitionType.Signal)) throw new InvalidOperationException("Signal binding needs a signal event.");
                reference = (signal ? "Signal_" : "Message_") + Guid.NewGuid().ToString("N");
                document = signal
                    ? document with { Catalog = document.Catalog with { Signals = [.. document.Catalog.Signals, new(reference)] } }
                    : document with { Catalog = document.Catalog with { Messages = [.. document.Catalog.Messages, new(reference)] } };
                if (!signal && node.Type is BpmnFlowNodeType.ReceiveTask or BpmnFlowNodeType.SendTask)
                {
                    node = node with { MessageRef = reference };
                }
                else node = node with { EventDefinitions = node.EventDefinitions.Select(d => d.Type == (signal ? BpmnEventDefinitionType.Signal : BpmnEventDefinitionType.Message) ? d with { Ref = reference } : d).ToArray() };
                document = BpmnDocumentEditor.TemplateReplace(document, node);
                root = document.Find(reference);
            }
            if (binding.Type == "bpmn:Message#zeebe:subscription#property")
            {
                var message = root as BpmnMessage ?? throw new InvalidOperationException("Message reference does not resolve to a message.");
                var path = ZeebeTemplateBindingHandler.Attribute("subscription", binding.Name ?? binding.Property ?? "correlationKey");
                var extras = remove ? BpmnExtensionNavigator.Remove(message.Extras, path) : ZeebeTemplateBindingHandler.Set(message.Extras, path, value ?? string.Empty, options);
                return BpmnDocumentEditor.TemplateReplaceExtras(document, message.Id, extras);
            }
            if ((binding.Name ?? binding.Property) != "name") throw new InvalidOperationException("Referenced root binding supports name only.");
            return BpmnDocumentEditor.TemplateReplace(document, root switch
            {
                BpmnMessage message => message with { Name = value },
                BpmnSignal definition => definition with { Name = value },
                _ => throw new InvalidOperationException("Root reference has wrong type.")
            });
        }
        var old = Definition(node, binding);
        BpmnEventDefinition updated;
        if (binding.Type.EndsWith("#zeebe:conditionalFilter#property", StringComparison.Ordinal))
        {
            var path = ZeebeTemplateBindingHandler.Attribute("conditionalFilter", binding.Name ?? binding.Property ?? throw new InvalidOperationException("Filter binding needs name."));
            updated = old with { Extras = remove ? BpmnExtensionNavigator.Remove(old.Extras, path) : ZeebeTemplateBindingHandler.Set(old.Extras, path, value ?? string.Empty, options) };
        }
        else
        {
            var name = ExpressionName(binding);
            if (remove && old.ExpressionKind is not null && old.ExpressionKind != name) return document;
            updated = old with { Expression = value, ExpressionKind = remove ? null : name };
        }
        return BpmnDocumentEditor.TemplateReplace(document, node with { EventDefinitions = node.EventDefinitions.Select(d => ReferenceEquals(d, old) ? updated : d).ToArray() });
    }
    private static string? Reference(BpmnFlowNode node, bool signal) => !signal && node.Type is BpmnFlowNodeType.ReceiveTask or BpmnFlowNodeType.SendTask
        ? node.MessageRef
        : node.EventDefinitions.FirstOrDefault(d => d.Type == (signal ? BpmnEventDefinitionType.Signal : BpmnEventDefinitionType.Message))?.Ref;
    private static BpmnEventDefinition Definition(BpmnFlowNode node, BpmnTemplateBinding binding) =>
        node.EventDefinitions.FirstOrDefault(d => d.Type == (binding.Type.StartsWith("bpmn:Timer", StringComparison.Ordinal) ? BpmnEventDefinitionType.Timer : BpmnEventDefinitionType.Conditional))
        ?? throw new InvalidOperationException("Binding requires a matching event definition.");
    private static string ExpressionName(BpmnTemplateBinding binding)
    {
        var name = binding.Name ?? binding.Property;
        if (binding.Type == "bpmn:TimerEventDefinition#property" && name is "timeDate" or "timeDuration" or "timeCycle" ||
            binding.Type == "bpmn:ConditionalEventDefinition#property" && name == "condition") return name!;
        throw new InvalidOperationException("Unsupported event expression binding.");
    }
}
