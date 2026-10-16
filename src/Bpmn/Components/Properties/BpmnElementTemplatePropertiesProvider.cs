using System.Globalization;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>Renders applied-template groups before schema groups.</summary>
public sealed class BpmnElementTemplatePropertiesProvider : IBpmnPropertiesProvider
{
    /// <inheritdoc />
    public int Priority => 900;
    /// <inheritdoc />
    public IReadOnlyList<BpmnPropertyGroup> GetGroups(BpmnElementSnapshot element, IReadOnlyList<BpmnPropertyGroup> groups) =>
        [.. groups, .. element.ExtensionGroups.Where(group => group.Id.StartsWith("template:", StringComparison.Ordinal))];

    internal static BpmnElementSnapshot Build(BpmnDocument document, BpmnElementSnapshot snapshot,
        BpmnTemplateOptions? options, IReadOnlyList<BpmnPropertyDescriptor> originalRaw)
    {
        if (options is null) return snapshot;
        var resolved = BpmnAppliedTemplate.Resolve(document, snapshot.ElementId, options.Registry);
        if (resolved.Id is null) return snapshot;
        snapshot = snapshot with { TemplateId = resolved.Id, TemplateVersion = resolved.Version };
        if (resolved.Template is not { } template)
            return snapshot with
            {
                ExtensionGroups = [new("template:missing", "Template", [
                new("template-id", "Id", BpmnPropertyEntryKind.ReadOnly, resolved.Id),
                new("template-version", "Version", BpmnPropertyEntryKind.ReadOnly, resolved.Version?.ToString(CultureInfo.InvariantCulture)),
                new("template-notice", "Notice", BpmnPropertyEntryKind.ReadOnly, "Template not found")]) { Namespace = "template" }, .. snapshot.ExtensionGroups]
            };

        template = BpmnTemplateScopes.Flatten(template);
        var values = BpmnTemplateValues.Read(document, snapshot.ElementId, template);
        var active = BpmnTemplateValues.ActiveProperties(template, values);
        var entries = template.Properties.Select((property, index) => (Property: property, Key: BpmnTemplateValues.PropertyKey(property, index)))
            .Where(item => active.Contains(item.Key) && item.Property.Type != BpmnTemplatePropertyType.Hidden)
            .Select(item => (Property: item.Property, Entry: Entry(template, item.Property, item.Key, values[item.Key]))).ToArray();
        var groups = template.Groups.Select(group => new BpmnPropertyGroup("template:" + group.Id, group.Label,
            entries.Where(item => item.Property.Group == group.Id).Select(item => item.Entry).ToArray())
        { Namespace = "template", Tooltip = group.Tooltip, OpenByDefault = group.OpenByDefault ?? true }).ToList();
        var custom = entries.Where(item => item.Property.Group is null || !template.Groups.Any(group => group.Id == item.Property.Group))
            .Select(item => item.Entry).ToArray();
        if (custom.Length > 0) groups.Add(new("template:custom", "Custom properties", custom) { Namespace = "template" });

        var extras = BpmnDocumentEditor.TemplateExtras(document, snapshot.ElementId);
        var claims = new HashSet<string>(StringComparer.Ordinal);
        var claimedRows = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in template.Properties)
        {
            if (property.Binding is not { } binding) continue;
            if (binding.Type.StartsWith("camunda:", StringComparison.Ordinal) || binding.Get("__scopeType") == "camunda:Connector")
            {
                try
                {
                    var path = Camunda7TemplateBindingHandler.Path(extras, binding) with { AttributeName = null, IsText = false };
                    if (RawPath(extras, path) is { } raw) claimedRows.Add(raw);
                }
                catch (InvalidOperationException) { /* Leave unknown content available in the raw fallback. */ }
                continue;
            }
            if (!binding.Type.StartsWith("zeebe:", StringComparison.Ordinal)) continue;
            try
            {
                var path = ZeebeTemplateBindingHandler.Path(extras, binding);
                if (RawPath(extras, path) is not { } raw) continue;
                if (binding.Type == "zeebe:userTask") claimedRows.Add(raw);
                else
                {
                    claims.Add(raw);
                    if (path.Steps[^1].Selector?.AttributeName is { } keyAttribute && RawPath(extras, path with { AttributeName = keyAttribute }) is { } keyRaw)
                        claims.Add(keyRaw);
                    if (binding.Type is "zeebe:executionListener" or "zeebe:taskListener")
                        foreach (var attribute in new[] { "eventType", "type", "retries" })
                            if (binding.Get(attribute) is not null && RawPath(extras, path with { AttributeName = attribute }) is { } listenerRaw) claims.Add(listenerRaw);
                }
            }
            catch (InvalidOperationException) { /* An unsupported binding remains available as opaque content. */ }
        }
        bool Claimed(string raw) => claims.Contains(raw) || claimedRows.Any(row => raw == row || raw.StartsWith(row + "@", StringComparison.Ordinal) || raw.StartsWith(row + "#", StringComparison.Ordinal) || raw.StartsWith(row + "/", StringComparison.Ordinal));
        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in snapshot.ExtensionGroups)
        {
            var remaining = new List<BpmnPropertyEntry>();
            foreach (var entry in group.Entries)
            {
                var binding = entry.Binding;
                if (binding?.Type == "foreign-attribute" && template.Properties.Any(p => p.Binding?.Type == "property" && p.Binding.Get("__scopeType") is null &&
                    p.Binding.Name == "camunda:" + binding.Parameters["name"])) continue;
                if (binding?.Type == "camunda-collection")
                {
                    var parent = binding.Parameters.GetValueOrDefault("container");
                    var parentPath = parent is null ? null : BpmnExtensionPath.Parse(parent);
                    var local = binding.Parameters["itemLocalName"];
                    var children = (parentPath is null ? extras.Extensions : BpmnExtensionNavigator.Resolve(extras.Extensions, parentPath.Steps)?.Children ?? [])
                        .Where(e => e.Namespace == Camunda7TemplateBindingHandler.Uri && e.LocalName == local).ToArray();
                    var rowPaths = children.Select((_, i) => new BpmnExtensionPath([.. parentPath?.Steps ?? [], new(Camunda7TemplateBindingHandler.Uri, local, new(Ordinal: i))])).ToArray();
                    var hasClaims = rowPaths.Any(p => RawPath(extras, p) is { } raw && Claimed(raw));
                    if (hasClaims) continue;
                    foreach (var row in rowPaths)
                        foreach (var column in entry.Columns ?? [])
                            if (RawPath(extras, row with { AttributeName = column.Name == "#text" ? null : column.Name, IsText = column.Name == "#text" }) is { } raw)
                                covered.Add(raw);
                }
                if (binding?.Type == "extension" && binding.Parameters.TryGetValue("path", out var pathText))
                {
                    var raw = RawPath(extras, BpmnExtensionPath.Parse(pathText));
                    if (raw is not null && Claimed(raw)) continue;
                    if (raw is not null) covered.Add(raw);
                }
                if (binding?.Type == "extension-collection" && binding.Parameters.TryGetValue("container", out var container))
                {
                    var uri = binding.Parameters["itemNamespace"];
                    var local = binding.Parameters["itemLocalName"];
                    var containerPath = BpmnExtensionPath.Parse(container);
                    var children = BpmnExtensionNavigator.ReadCollection(extras, containerPath, uri, local);
                    var hasClaims = children.Select((_, index) => RawPath(extras, containerPath with { Steps = [.. containerPath.Steps, new(uri, local, new(Ordinal: index))] }))
                        .Any(raw => raw is not null && (Claimed(raw) || claims.Any(claim => claim.StartsWith(raw + "@", StringComparison.Ordinal))));
                    for (var index = 0; index < children.Count; index++)
                    {
                        foreach (var column in entry.Columns ?? [])
                        {
                            var path = containerPath with { Steps = [.. containerPath.Steps, new(uri, local, new(Ordinal: index))], AttributeName = column.Name };
                            var raw = RawPath(extras, path);
                            if (raw is null || Claimed(raw)) continue;
                            covered.Add(raw);
                            if (hasClaims) remaining.Add(new(entry.Id + ":" + index + ":" + column.Name, $"{entry.Label} {index + 1}: {column.Label}", BpmnPropertyEntryKind.Text,
                                children[index].Attributes.GetValueOrDefault(column.Name))
                            { Binding = new("extension", new Dictionary<string, string> { ["path"] = path.ToString() }) });
                        }
                    }
                    // A partially claimed collection becomes scalar entries: replacing its rows must never delete template-owned rows.
                    if (hasClaims) continue;
                }
                remaining.Add(entry);
            }
            if (remaining.Count > 0) groups.Add(group with { Entries = remaining });
        }
        var properties = snapshot.Properties;
        if (options.HonorEntriesVisible)
        {
            var boundNames = template.Properties.Where(property => property.Binding?.Type == "property").Select(property => property.Binding!.Name).ToHashSet(StringComparer.Ordinal);
            properties = properties.Where(property => !boundNames.Contains(property.Name) ||
                (template.EntriesVisible.Entries?.GetValueOrDefault(property.Name) ?? template.EntriesVisible.Default ?? true)).ToArray();
        }
        return snapshot with
        {
            Properties = properties,
            ExtensionGroups = groups.Where(group => group.Entries.Count > 0).Select(group => group with { Entries = group.Entries.Select(entry => entry with { GroupId = group.Id }).ToArray() }).ToArray(),
            ExtensionProperties = originalRaw.Where(property => !Claimed(property.Name["extension:".Length..]) && !covered.Contains(property.Name["extension:".Length..])).ToArray()
        };
    }

    private static BpmnPropertyEntry Entry(BpmnElementTemplate template, BpmnTemplateProperty property, string key, string? value) =>
        new("template:" + key, property.Label ?? property.Id ?? key, !property.Editable ? BpmnPropertyEntryKind.ReadOnly : property.Type switch
        {
            BpmnTemplatePropertyType.Text => BpmnPropertyEntryKind.TextArea,
            BpmnTemplatePropertyType.Number => BpmnPropertyEntryKind.Number,
            BpmnTemplatePropertyType.Boolean => BpmnPropertyEntryKind.Checkbox,
            BpmnTemplatePropertyType.Dropdown => BpmnPropertyEntryKind.Select,
            _ => BpmnPropertyEntryKind.Text
        }, DisplayValue(property, value))
        {
            Binding = new("template", new Dictionary<string, string> { ["templateId"] = template.Id, ["version"] = template.Version?.ToString(CultureInfo.InvariantCulture) ?? "", ["propertyKey"] = key }),
            Description = property.Description,
            Tooltip = property.Tooltip,
            Placeholder = property.Placeholder,
            Feel = (BpmnFeelMode)property.Feel,
            ExpressionLanguage = property.Feel is BpmnTemplateFeel.Optional or BpmnTemplateFeel.Required ? BpmnExpressionLanguage.Feel
                : JuelBinding(property.Binding) ? BpmnExpressionLanguage.Juel : BpmnExpressionLanguage.None,
            IsReadOnly = !property.Editable,
            Choices = property.Choices.Select(choice => new BpmnPropertyChoice(choice.Value, choice.Name)).ToArray(),
            Constraints = property.Constraints is { } constraints ? new(constraints.NotEmpty == true, constraints.MinLength, constraints.MaxLength, constraints.Pattern?.Value, constraints.Pattern?.Message) : null,
            Error = BpmnTemplatePropertyValidation.Errors(property, value) is { Count: > 0 } errors ? string.Join(" ", errors) : null
        };

    private static bool JuelBinding(BpmnTemplateBinding? binding) => binding?.Type switch
    {
        "camunda:inputParameter" => binding.Get("scriptFormat") is null,
        "camunda:field" or "camunda:in" => binding.Get("expression") is "true" or "True",
        "camunda:in:businessKey" or "camunda:errorEventDefinition" => true,
        "camunda:executionListener" => binding.Get("scriptFormat") is null
            && binding.Get("implementationType") is "expression" or "delegateExpression",
        "property" => binding.Name is "conditionExpression" or "calledElement" or "camunda:delegateExpression"
            or "camunda:expression" or "camunda:topic" or "camunda:assignee" or "camunda:candidateUsers"
            or "camunda:candidateGroups" or "camunda:dueDate" or "camunda:followUpDate" or "camunda:priority"
            or "camunda:jobPriority" or "camunda:taskPriority" or "camunda:formKey" or "camunda:errorMessage"
            or "camunda:calledElementVersion" or "camunda:calledElementVersionTag" or "camunda:calledElementTenantId",
        _ => false,
    };

    private static string? DisplayValue(BpmnTemplateProperty property, string? value)
    {
        if (value?.StartsWith('=') == true &&
            (property.Type == BpmnTemplatePropertyType.Boolean && bool.TryParse(value[1..], out _) ||
             property.Type == BpmnTemplatePropertyType.Number && decimal.TryParse(value[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out _))) return value[1..];
        return value;
    }

    private static string? RawPath(BpmnElementExtras extras, BpmnExtensionPath path)
    {
        IReadOnlyList<BpmnExtensionElement> children = extras.Extensions;
        var indices = new List<string>();
        foreach (var step in path.Steps)
        {
            var candidates = children.Select((element, index) => (element, index)).Where(item => item.element.Namespace == step.NamespaceUri && item.element.LocalName == step.LocalName).ToArray();
            var selector = step.Selector;
            var candidate = selector?.AttributeName is { } attribute ? candidates.FirstOrDefault(item => item.element.Attributes.GetValueOrDefault(attribute) == selector.AttributeValue)
                : candidates.ElementAtOrDefault(selector?.Ordinal ?? 0);
            if (candidate.element is null) return null;
            indices.Add(candidate.index.ToString(CultureInfo.InvariantCulture));
            children = candidate.element.Children;
        }
        return string.Join('/', indices) + (path.AttributeName is { } name ? "@" + name : path.IsText ? "#text" : "");
    }
}
