using System.Globalization;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>Applies active defaults and markers in one undo step. Registry resolves old bindings; an unknown old template retains its data.</summary>
public sealed record ApplyElementTemplateCommand(string ElementId, BpmnElementTemplate Template, BpmnElementTemplateRegistry? Registry = null) : BpmnEditCommand
{
    /// <summary>Binding handlers used by this command.</summary>
    public BpmnTemplateBindingHandlers Handlers { get; init; } = BpmnTemplateBindingHandlers.Default;
    /// <inheritdoc />
    public override string Label => "Apply element template";
}
/// <summary>Edits an active, editable property and reconciles conditions atomically. PropertyKey is its id, zero-based #n, or a scope-prefixed key documented by BpmnTemplateScopes.</summary>
public sealed record SetTemplatePropertyCommand(string ElementId, BpmnElementTemplate Template, string PropertyKey, string? Value) : BpmnEditCommand
{
    /// <summary>Binding handlers used by this command.</summary>
    public BpmnTemplateBindingHandlers Handlers { get; init; } = BpmnTemplateBindingHandlers.Default;
    /// <inheritdoc />
    public override string Label => "Edit template property";
}
/// <summary>Removes template identity only, retaining configured bindings.</summary>
public sealed record UnlinkElementTemplateCommand(string ElementId) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Unlink element template";
}
/// <summary>Removes template bindings and markers, preserving other extension content.</summary>
public sealed record RemoveElementTemplateCommand(string ElementId, BpmnElementTemplate Template) : BpmnEditCommand
{
    /// <summary>Binding handlers used by this command.</summary>
    public BpmnTemplateBindingHandlers Handlers { get; init; } = BpmnTemplateBindingHandlers.Default;
    /// <inheritdoc />
    public override string Label => "Remove element template";
}
/// <summary>Upgrades a template, retaining editable non-hidden values with matching bindings.</summary>
public sealed record UpgradeElementTemplateCommand(string ElementId, BpmnElementTemplate OldTemplate, BpmnElementTemplate NewTemplate) : BpmnEditCommand
{
    /// <summary>Binding handlers used by this command.</summary>
    public BpmnTemplateBindingHandlers Handlers { get; init; } = BpmnTemplateBindingHandlers.Default;
    /// <inheritdoc />
    public override string Label => "Upgrade element template";
}

public static partial class BpmnDocumentEditor
{
    private static BpmnEditResult ApplyTemplateCommand(BpmnDocument document, BpmnEditCommand command, EditingOptions options)
    {
        var id = command switch
        {
            ApplyElementTemplateCommand c => c.ElementId,
            SetTemplatePropertyCommand c => c.ElementId,
            UnlinkElementTemplateCommand c => c.ElementId,
            RemoveElementTemplateCommand c => c.ElementId,
            UpgradeElementTemplateCommand c => c.ElementId,
            _ => string.Empty
        };
        try
        {
            var updated = command switch
            {
                ApplyElementTemplateCommand c => ChangeTemplate(document, c.ElementId,
                    c.Registry is null ? null : BpmnAppliedTemplate.Resolve(document, c.ElementId, c.Registry).Template, c.Template, c.Handlers, options, false),
                UpgradeElementTemplateCommand c => ChangeTemplate(document, c.ElementId, c.OldTemplate, c.NewTemplate, c.Handlers, options, true),
                SetTemplatePropertyCommand c => SetTemplateProperty(document, c, options),
                UnlinkElementTemplateCommand c => TemplateMarkers(document, c.ElementId, null),
                RemoveElementTemplateCommand c => RemoveTemplate(document, c.ElementId, c.Template, c.Handlers, options),
                _ => throw new InvalidOperationException("Unknown template command.")
            };
            return BpmnEditResult.Applied(updated);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException)
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, "EDIT-110", $"Template edit refused (EXT-004): {exception.Message}", id));
        }
    }

    private static BpmnDocument ChangeTemplate(BpmnDocument document, string id, BpmnElementTemplate? oldTemplate,
        BpmnElementTemplate template, BpmnTemplateBindingHandlers handlers, EditingOptions options, bool upgrade)
    {
        template = BpmnTemplateScopes.Flatten(template);
        oldTemplate = oldTemplate is null ? null : BpmnTemplateScopes.Flatten(oldTemplate);
        var element = document.Find(id) ?? throw new InvalidOperationException("Template target missing.");
        var wireType = BpmnElementInspector.Describe(document, id)?.ElementType ?? throw new InvalidOperationException("Template target unsupported.");
        if (!BpmnTemplateSelection.MatchesType(template, wireType)) throw new InvalidOperationException("Template appliesTo does not match target.");
        foreach (var property in template.Properties.Concat(oldTemplate?.Properties ?? []))
            if (property.Binding is { } binding) handlers.Resolve(binding);
        var oldValues = oldTemplate is null ? null : BpmnTemplateValues.Read(document, id, oldTemplate, handlers);
        var oldActive = oldTemplate is null ? null : TemplateActive(oldTemplate, oldValues!);
        var values = template.Properties.Select((property, index) => (Key: BpmnTemplateValues.PropertyKey(property, index), Property: property))
            .ToDictionary(p => p.Key, p => upgrade && p.Property.Editable && p.Property.Type != BpmnTemplatePropertyType.Hidden &&
                oldTemplate?.Properties.Select((old, index) => (old, index)).Any(x =>
                    oldActive!.Contains(BpmnTemplateValues.PropertyKey(x.old, x.index)) && SameBinding(x.old.Binding, p.Property.Binding)) == true && p.Property.Binding is { } binding
                ? handlers.Resolve(binding).Read(document, id, binding) : BpmnTemplateValues.DefaultValue(p.Property), StringComparer.Ordinal);
        if (template.ElementType is { } typeOverride)
        {
            var node = element as BpmnFlowNode ?? throw new InvalidOperationException("elementType switching needs a flow node.");
            var local = typeOverride.Value.Split(':').Last();
            if (!Enum.TryParse<BpmnFlowNodeType>(local, true, out var type)) throw new InvalidOperationException($"Unsupported elementType '{typeOverride.Value}'.");
            var eventType = node.EventDefinition;
            if (typeOverride.EventDefinition is { } eventName && !Enum.TryParse(eventName.Split(':').Last().Replace("EventDefinition", string.Empty, StringComparison.Ordinal), true, out eventType))
                throw new InvalidOperationException($"Unsupported event definition '{eventName}'.");
            if (type != node.Type || eventType != node.EventDefinition)
                document = TemplateStep(document, new SwitchElementTypeCommand(id, type, eventType), options);
        }
        var active = ReconcileTemplateDefaults(template, values);
        if (oldTemplate is not null)
            foreach (var old in oldTemplate.Properties.OrderByDescending(p => p.Binding?.Get("__scopeType") is not null))
                if (old.Binding is { } oldBinding && (!upgrade || !template.Properties.Select((p, i) => (p, i)).Any(x => active.Contains(BpmnTemplateValues.PropertyKey(x.p, x.i)) && SameBinding(x.p.Binding, oldBinding))))
                    document = handlers.Resolve(oldBinding).Remove(document, id, oldBinding, options);
        foreach (var (property, index) in template.Properties.Select((property, index) => (property, index))
            .OrderBy(item => item.property.Binding?.Type == "property" && item.property.Binding.Get("__scopeType") is null ? 0 : 1))
        {
            var key = BpmnTemplateValues.PropertyKey(property, index);
            if (active.Contains(key) && property.Binding is { } binding)
                document = handlers.Resolve(binding).Write(document, id, binding, values[key], property, options);
        }
        return TemplateMarkers(document, id, template);
    }

    private static BpmnDocument SetTemplateProperty(BpmnDocument document, SetTemplatePropertyCommand command, EditingOptions options)
    {
        var template = BpmnTemplateScopes.Flatten(command.Template);
        var values = new Dictionary<string, string?>(BpmnTemplateValues.Read(document, command.ElementId, template, command.Handlers), StringComparer.Ordinal);
        var indexed = template.Properties.Select((property, index) => (Property: property, Key: BpmnTemplateValues.PropertyKey(property, index))).ToArray();
        var property = indexed.FirstOrDefault(p => p.Key == command.PropertyKey).Property ?? throw new InvalidOperationException("Unknown property key.");
        var active = TemplateActive(template, values);
        if (!active.Contains(command.PropertyKey) || !property.Editable || property.Type == BpmnTemplatePropertyType.Hidden) throw new InvalidOperationException("Template property is not editable or active.");
        values[command.PropertyKey] = BpmnTemplateValues.Normalize(property, command.Value);
        if (property.Binding is { } binding) document = command.Handlers.Resolve(binding).Write(document, command.ElementId, binding, values[command.PropertyKey], property, options);
        // Newly active defaults may activate another condition. Bound passes prevent malformed condition oscillations.
        for (var pass = 0; pass <= template.Properties.Count; pass++)
        {
            var next = TemplateActive(template, values);
            if (next.SetEquals(active)) return document;
            foreach (var (Property, Key) in indexed.Where(item => active.Contains(item.Key) && !next.Contains(item.Key)))
            {
                values[Key] = null;
                if (Property.Binding is { } removed && !indexed.Any(other => next.Contains(other.Key) && SameBinding(other.Property.Binding, removed)))
                    document = command.Handlers.Resolve(removed).Remove(document, command.ElementId, removed, options);
            }
            foreach (var (Property, Key) in indexed.Where(item => !active.Contains(item.Key) && next.Contains(item.Key)))
            {
                values[Key] = BpmnTemplateValues.DefaultValue(Property);
                if (Property.Binding is { } added) document = command.Handlers.Resolve(added).Write(document, command.ElementId, added, values[Key], Property, options);
            }
            active = next;
        }
        throw new InvalidOperationException("Template conditions did not converge.");
    }

    private static IReadOnlySet<string> ReconcileTemplateDefaults(BpmnElementTemplate template, Dictionary<string, string?> values)
    {
        var defaults = new Dictionary<string, string?>(values, StringComparer.Ordinal);
        IReadOnlySet<string> active = defaults.Keys.ToHashSet(StringComparer.Ordinal);
        // Conditions must see only values that will be written, not inactive defaults.
        for (var pass = 0; pass <= template.Properties.Count; pass++)
        {
            var next = TemplateActive(template, values);
            if (next.SetEquals(active)) return active;
            foreach (var (key, value) in defaults)
                values[key] = next.Contains(key) ? value : null;
            active = next;
        }
        throw new InvalidOperationException("Template conditions did not converge.");
    }

    private static IReadOnlySet<string> TemplateActive(BpmnElementTemplate template, IReadOnlyDictionary<string, string?> values) =>
        BpmnTemplateValues.ActiveProperties(template, values);

    private static bool SameBinding(BpmnTemplateBinding? left, BpmnTemplateBinding? right)
    {
        if (left is null || right is null) return false;
        static BpmnTemplateBinding Canonical(BpmnTemplateBinding binding) => binding.Type == "zeebe:taskDefinition:type"
            ? new("zeebe:taskDefinition", new Dictionary<string, string>(binding.Parameters, StringComparer.Ordinal) { ["property"] = "type" }) : binding;
        left = Canonical(left); right = Canonical(right);
        return left.Type == right.Type && left.Parameters.Count == right.Parameters.Count && left.Parameters.All(pair => right.Get(pair.Key) == pair.Value);
    }
    private static BpmnDocument RemoveTemplate(BpmnDocument document, string id, BpmnElementTemplate template, BpmnTemplateBindingHandlers handlers, EditingOptions options)
    {
        foreach (var property in BpmnTemplateScopes.Flatten(template).Properties.OrderByDescending(p => p.Binding?.Get("__scopeType") is not null))
            if (property.Binding is { } binding) document = handlers.Resolve(binding).Remove(document, id, binding, options);
        return TemplateMarkers(document, id, null);
    }
    private static BpmnDocument TemplateMarkers(BpmnDocument document, string id, BpmnElementTemplate? template)
    {
        var extras = TemplateExtras(document, id);
        var attributes = new Dictionary<string, string>(extras.ForeignAttributes, StringComparer.Ordinal);
        foreach (var uri in new[] { BpmnTemplateMarkers.ZeebeNamespaceUri, BpmnTemplateMarkers.CamundaNamespaceUri })
            foreach (var local in new[] { BpmnTemplateMarkers.ModelerTemplate, BpmnTemplateMarkers.ModelerTemplateVersion, BpmnTemplateMarkers.ModelerTemplateIcon })
                attributes.Remove($"{{{uri}}}{local}");
        if (template is not null)
        {
            var uri = template.Flavor == BpmnTemplateFlavor.Camunda7 ? BpmnTemplateMarkers.CamundaNamespaceUri : BpmnTemplateMarkers.ZeebeNamespaceUri;
            attributes[$"{{{uri}}}{BpmnTemplateMarkers.ModelerTemplate}"] = template.Id;
            if (template.Version is { } version) attributes[$"{{{uri}}}{BpmnTemplateMarkers.ModelerTemplateVersion}"] = version.ToString(CultureInfo.InvariantCulture);
            if (template.Icon is { } icon) attributes[$"{{{uri}}}{BpmnTemplateMarkers.ModelerTemplateIcon}"] = icon;
        }
        return TemplateReplaceExtras(document, id, extras with { ForeignAttributes = attributes });
    }
}
