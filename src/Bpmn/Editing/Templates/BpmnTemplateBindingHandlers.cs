using System.Globalization;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>Reads and edits one family of template bindings. Throw InvalidOperationException to refuse an unsupported target or value.</summary>
public interface IBpmnTemplateBindingHandler
{
    /// <summary>Whether this handler accepts the binding.</summary>
    public bool CanHandle(BpmnTemplateBinding binding);
    /// <summary>Reads the stored binding value; null means absent.</summary>
    public string? Read(BpmnDocument document, string elementId, BpmnTemplateBinding binding);
    /// <summary>Writes a normalized value, preserving unrelated content.</summary>
    public BpmnDocument Write(BpmnDocument document, string elementId, BpmnTemplateBinding binding, string? value, BpmnTemplateProperty templateProperty, EditingOptions options);
    /// <summary>Removes only the bound value.</summary>
    public BpmnDocument Remove(BpmnDocument document, string elementId, BpmnTemplateBinding binding, EditingOptions options);
}

/// <summary>Ordered binding-handler registry. Custom handlers precede built-in Camunda 7 and 8 handlers.</summary>
public sealed class BpmnTemplateBindingHandlers
{
    private readonly IReadOnlyList<IBpmnTemplateBindingHandler> _handlers;
    /// <summary>Built-in Camunda 7 and 8 handlers.</summary>
    public static BpmnTemplateBindingHandlers Default { get; } = new();
    /// <summary>Creates a registry with optional vendor handlers.</summary>
    public BpmnTemplateBindingHandlers(IEnumerable<IBpmnTemplateBindingHandler>? handlers = null) =>
        _handlers = [.. handlers ?? [], new Camunda7TemplateBindingHandler(), new StandardTemplateBindingHandler(), new ZeebeTemplateBindingHandler(), new ReferencedTemplateBindingHandler()];
    /// <summary>Resolves a handler, or throws for an unknown binding (reported as EDIT-110 by commands).</summary>
    public IBpmnTemplateBindingHandler Resolve(BpmnTemplateBinding binding) =>
        _handlers.FirstOrDefault(handler => handler.CanHandle(binding)) ?? throw new InvalidOperationException($"Unknown template binding '{binding.Type}' (EXT-004).");
}

/// <summary>Stored template values indexed by property id, zero-based #n, or the scope-prefixed keys documented by BpmnTemplateScopes.</summary>
public static class BpmnTemplateValues
{
    /// <summary>Gets the stable command/UI key for a property at its zero-based index.</summary>
    public static string PropertyKey(BpmnTemplateProperty property, int index) => property.Id ?? "#" + index.ToString(CultureInfo.InvariantCulture);
    /// <summary>Reads template values. Unknown bindings throw InvalidOperationException.</summary>
    public static IReadOnlyDictionary<string, string?> Read(BpmnDocument document, string elementId, BpmnElementTemplate template,
        BpmnTemplateBindingHandlers? handlers = null) => BpmnTemplateScopes.Flatten(template).Properties.Select((property, index) =>
            new KeyValuePair<string, string?>(PropertyKey(property, index), property.Binding is { } binding
                ? (handlers ?? BpmnTemplateBindingHandlers.Default).Resolve(binding).Read(document, elementId, binding) : null))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <summary>Evaluates activation using the same stable keys and normalized values as template commands.</summary>
    public static IReadOnlySet<string> ActiveProperties(BpmnElementTemplate template, IReadOnlyDictionary<string, string?> values)
    {
        template = BpmnTemplateScopes.Flatten(template);
        var keyed = template with { Properties = template.Properties.Select((property, index) => property with { Id = PropertyKey(property, index) }).ToArray() };
        return BpmnTemplateConditionEvaluator.ActiveProperties(keyed, property =>
        {
            var value = values.GetValueOrDefault(property.Id!);
            return property.Type is BpmnTemplatePropertyType.Boolean or BpmnTemplatePropertyType.Number && value?.StartsWith('=') == true ? value[1..] : value;
        });
    }

    internal static string? Normalize(BpmnTemplateProperty property, string? value)
    {
        if (value is null || value.StartsWith('=')) return value;
        if (property.Optional && value.Length == 0) return value;
        if (property.Type == BpmnTemplatePropertyType.Boolean && bool.TryParse(value, out var boolean)) value = boolean ? "true" : "false";
        if (property.Type == BpmnTemplatePropertyType.Number && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            value = number.ToString(CultureInfo.InvariantCulture);
        var cast = property.Feel == BpmnTemplateFeel.Required ||
            (property.Type is BpmnTemplatePropertyType.Boolean or BpmnTemplatePropertyType.Number &&
             (property.Feel is BpmnTemplateFeel.Optional or BpmnTemplateFeel.Static || property.Binding?.Type is "zeebe:input" or "zeebe:output"));
        return cast ? "=" + value : value;
    }

    internal static string? DefaultValue(BpmnTemplateProperty property) => Normalize(property,
        property.Value ?? (property.GeneratedValue?.Type == "uuid" ? Guid.NewGuid().ToString() : null));
}

/// <summary>Applied template identity and registry resolution for property-panel callers.</summary>
public static class BpmnAppliedTemplate
{
    /// <summary>Resolves persisted markers without changing missing-template content.</summary>
    public static (BpmnElementTemplate? Template, string? Id, int? Version, bool IsMissing) Resolve(
        BpmnDocument document, string elementId, BpmnElementTemplateRegistry registry)
    {
        var extras = BpmnDocumentEditor.TemplateExtras(document, elementId);
        if (!BpmnTemplateMarkers.TryReadApplied(extras, out var id, out var version, out var flavor)) return (null, null, null, false);
        var template = registry.TryGetExact(id, version);
        if (template is not null && template.Flavor != flavor) template = null;
        return (template, id, version, template is null);
    }
}

public static partial class BpmnDocumentEditor
{
    internal static BpmnElementExtras TemplateExtras(BpmnDocument document, string id) =>
        TryGetCarrier(document, id, out _, out var extras) ? extras : throw new InvalidOperationException($"Element '{id}' does not carry extension data.");

    internal static BpmnDocument TemplateReplaceExtras(BpmnDocument document, string id, BpmnElementExtras updated)
    {
        if (!TryGetCarrier(document, id, out var element, out var original)) throw new InvalidOperationException($"No element '{id}'.");
        return TemplateRequire(ApplyExtras(document, element!, original, updated, id));
    }

    internal static BpmnDocument TemplateReplace(BpmnDocument document, object replacement) => WithElement(document, replacement);
    internal static BpmnDocument TemplateStep(BpmnDocument document, BpmnEditCommand command, EditingOptions options) => TemplateRequire(ApplyCommand(document, command, options));
    internal static BpmnDocument TemplateRequire(BpmnEditResult result) => result.IsApplied ? result.Document :
        throw new InvalidOperationException(string.Join(" ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
}
