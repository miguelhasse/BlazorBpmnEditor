using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

internal sealed class StandardTemplateBindingHandler : IBpmnTemplateBindingHandler
{
    public bool CanHandle(BpmnTemplateBinding binding) => binding.Type == "property";
    public string? Read(BpmnDocument document, string elementId, BpmnTemplateBinding binding)
    {
        var name = binding.Name ?? throw new InvalidOperationException("Property binding needs name.");
        if (name.Contains(':')) return BpmnDocumentEditor.TemplateExtras(document, elementId).ForeignAttributes.GetValueOrDefault(Expanded(document, name));
        var element = document.Find(elementId) ?? throw new InvalidOperationException("Element missing.");
        var property = BpmnElementInspector.Properties(document, element).FirstOrDefault(p => p.Name == name);
        return property?.Value ?? (property is not null ? null : throw new InvalidOperationException($"Unsupported BPMN property '{name}'."));
    }
    public BpmnDocument Write(BpmnDocument document, string elementId, BpmnTemplateBinding binding, string? value, BpmnTemplateProperty property, EditingOptions options)
    {
        var name = binding.Name ?? throw new InvalidOperationException("Property binding needs name.");
        if (!name.Contains(':')) return BpmnDocumentEditor.TemplateStep(document, new UpdatePropertiesCommand(elementId, name, value), options);
        var key = Expanded(document, name);
        var extras = BpmnDocumentEditor.TemplateExtras(document, elementId);
        var attributes = new Dictionary<string, string>(extras.ForeignAttributes, StringComparer.Ordinal);
        if (value is null) attributes.Remove(key); else attributes[key] = value;
        return BpmnDocumentEditor.TemplateReplaceExtras(document, elementId, extras with { ForeignAttributes = attributes });
    }
    public BpmnDocument Remove(BpmnDocument document, string elementId, BpmnTemplateBinding binding, EditingOptions options) =>
        Write(document, elementId, binding, null, new(), options);
    private static string Expanded(BpmnDocument document, string name)
    {
        var parts = name.Split(':', 2);
        var uri = parts[0] == "zeebe" ? BpmnTemplateMarkers.ZeebeNamespaceUri :
            parts[0] == "camunda" ? BpmnTemplateMarkers.CamundaNamespaceUri :
            document.Definitions.NamespaceDeclarations.GetValueOrDefault(parts[0]) ?? throw new InvalidOperationException($"Unknown namespace '{parts[0]}'.");
        return $"{{{uri}}}{parts[1]}";
    }
}

internal sealed class ZeebeTemplateBindingHandler : IBpmnTemplateBindingHandler
{
    private static readonly Lazy<BpmnSchemaChildOrder> DefaultOrder = new(() => new(new BpmnExtensionRegistry().Register(BpmnExtensionSchemas.Zeebe)));
    internal const string Uri = BpmnTemplateMarkers.ZeebeNamespaceUri;
    private static readonly HashSet<string> SingleTypes = new(StringComparer.Ordinal)
    {
        "taskDefinition", "calledElement", "userTask", "formDefinition", "assignmentDefinition", "taskSchedule", "priorityDefinition",
        "jobPriorityDefinition", "calledDecision", "script", "adHoc", "agentDefinition"
    };
    public bool CanHandle(BpmnTemplateBinding binding) => binding.Type.StartsWith("zeebe:", StringComparison.Ordinal) &&
        (SingleTypes.Contains(binding.Type[6..]) || binding.Type is "zeebe:taskDefinition:type" or "zeebe:input" or "zeebe:output" or "zeebe:taskHeader" or "zeebe:property" or "zeebe:linkedResource" or "zeebe:executionListener" or "zeebe:taskListener");

    public string? Read(BpmnDocument document, string elementId, BpmnTemplateBinding binding)
    {
        var extras = BpmnDocumentEditor.TemplateExtras(document, elementId);
        var path = Path(extras, binding);
        if (binding.Type == "zeebe:userTask") return extras.Extensions.Any(e => e.Namespace == Uri && e.LocalName == "userTask") ? "true" : null;
        return BpmnExtensionNavigator.Read(extras, path);
    }
    public BpmnDocument Write(BpmnDocument document, string elementId, BpmnTemplateBinding binding, string? value, BpmnTemplateProperty property, EditingOptions options)
    {
        if (property.Optional && string.IsNullOrEmpty(value)) return Remove(document, elementId, binding, options);
        var extras = BpmnDocumentEditor.TemplateExtras(document, elementId);
        var path = Path(extras, binding);
        var updated = Set(extras, path, value ?? string.Empty, options);
        if (binding.Type is "zeebe:executionListener" or "zeebe:taskListener")
        {
            foreach (var key in new[] { "eventType", "type", "retries" })
                if (binding.Get(key) is { } keyValue)
                    updated = Set(updated, path with { AttributeName = key }, keyValue, options);
        }
        return BpmnDocumentEditor.TemplateReplaceExtras(document, elementId, updated);
    }
    public BpmnDocument Remove(BpmnDocument document, string elementId, BpmnTemplateBinding binding, EditingOptions options)
    {
        var extras = BpmnDocumentEditor.TemplateExtras(document, elementId);
        var path = Path(extras, binding);
        if (binding.Type is "zeebe:input" or "zeebe:output" or "zeebe:taskHeader" or "zeebe:property" or "zeebe:userTask" or "zeebe:executionListener" or "zeebe:taskListener")
            path = path with { AttributeName = null };
        return BpmnDocumentEditor.TemplateReplaceExtras(document, elementId, BpmnExtensionNavigator.Remove(extras, path));
    }

    internal static BpmnElementExtras Set(BpmnElementExtras extras, BpmnExtensionPath path, string value, EditingOptions options)
    {
        if (value.Length != 0) return BpmnExtensionNavigator.Write(extras, path, value, new(ChildOrder: options.ExtensionChildOrder ?? DefaultOrder.Value));
        // Navigator treats empty as removal. Template non-optional bindings instead retain an empty attribute.
        var token = Guid.NewGuid().ToString();
        var created = BpmnExtensionNavigator.Write(extras, path with { AttributeName = path.AttributeName ?? "__templatePresence" }, token, new(ChildOrder: options.ExtensionChildOrder ?? DefaultOrder.Value));
        BpmnExtensionElement Rewrite(BpmnExtensionElement element)
        {
            var attributes = new Dictionary<string, string>(element.Attributes, StringComparer.Ordinal);
            foreach (var (key, stored) in element.Attributes)
                if (stored == token)
                {
                    if (path.AttributeName is null) attributes.Remove(key); else attributes[key] = string.Empty;
                }
            return element with { Attributes = attributes, Children = element.Children.Select(Rewrite).ToArray() };
        }
        return created with { Extensions = created.Extensions.Select(Rewrite).ToArray() };
    }

    internal static BpmnExtensionPath Attribute(string local, string attribute) => new([new(Uri, local)], attribute);
    internal static BpmnExtensionPath Path(BpmnElementExtras extras, BpmnTemplateBinding binding)
    {
        BpmnExtensionPath Keyed(string parent, string child, string key, string? value, string attribute) => new(
            [new(Uri, parent), new(Uri, child, new(AttributeName: key, AttributeValue: value ?? throw new InvalidOperationException($"Binding '{binding.Type}' needs '{key}'.")))], attribute);
        switch (binding.Type)
        {
            case "zeebe:taskDefinition:type": return Attribute("taskDefinition", "type");
            case "zeebe:input": return Keyed("ioMapping", "input", "target", binding.Name, "source");
            case "zeebe:output": return Keyed("ioMapping", "output", "source", binding.Get("source"), "target");
            case "zeebe:taskHeader": return Keyed("taskHeaders", "header", "key", binding.Key, "value");
            case "zeebe:property": return Keyed("properties", "property", "name", binding.Name, "value");
            case "zeebe:linkedResource": return Keyed("linkedResources", "linkedResource", "linkName", binding.Get("linkName"), binding.Property ?? "resourceId");
            case "zeebe:userTask": return new([new(Uri, "userTask")]);
            case "zeebe:executionListener":
            case "zeebe:taskListener":
                var task = binding.Type == "zeebe:taskListener";
                var parent = task ? "taskListeners" : "executionListeners";
                var child = task ? "taskListener" : "executionListener";
                var rows = extras.Extensions.FirstOrDefault(e => e.Namespace == Uri && e.LocalName == parent)?.Children
                    .Where(e => e.Namespace == Uri && e.LocalName == child).ToArray() ?? [];
                var index = Array.FindIndex(rows, e => e.Attributes.GetValueOrDefault("eventType") == binding.Get("eventType") && (binding.Get("type") is null || e.Attributes.GetValueOrDefault("type") == binding.Get("type")));
                return new([new(Uri, parent), new(Uri, child, new(Ordinal: index < 0 ? rows.Length : index))], binding.Property ?? "type");
            default: return Attribute(binding.Type[6..], binding.Property ?? throw new InvalidOperationException($"Binding '{binding.Type}' needs property."));
        }
    }
}
