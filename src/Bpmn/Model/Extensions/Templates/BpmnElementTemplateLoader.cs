using System.Text;
using System.Text.Json;

namespace Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

/// <summary>Loads Camunda 7 and Camunda 8 element-template JSON without network access.</summary>
public static class BpmnElementTemplateLoader
{
    /// <summary>Maximum accepted JSON input size.</summary>
    public const int MaximumInputBytes = 4 * 1024 * 1024;

    /// <summary>Loads one template object or an array of templates.</summary>
    public static BpmnTemplateLoadResult Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > MaximumInputBytes)
            return Failure("Template JSON exceeds the 4 MB input limit.");
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            return LoadRoot(document.RootElement);
        }
        catch (JsonException ex)
        {
            return Failure($"Invalid template JSON: {ex.Message}");
        }
    }

    /// <summary>Loads one template object or an array of templates from a bounded stream.</summary>
    public static BpmnTemplateLoadResult Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > MaximumInputBytes)
                return Failure("Template JSON exceeds the 4 MB input limit.");
            output.Write(buffer, 0, read);
        }
        return Load(Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length)));
    }

    private static BpmnTemplateLoadResult LoadRoot(JsonElement root)
    {
        var templates = new List<BpmnElementTemplate>();
        var errors = new List<BpmnTemplateLoadError>();
        if (root.ValueKind == JsonValueKind.Object) ParseOne(root, 0, templates, errors);
        else if (root.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in root.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    errors.Add(Error("Each array item must be a template object.", index));
                else ParseOne(item, index, templates, errors);
                index++;
            }
        }
        else return Failure("Template input must be an object or an array of objects.");
        return new(templates, errors);
    }

    private static void ParseOne(JsonElement root, int index, List<BpmnElementTemplate> templates, List<BpmnTemplateLoadError> errors)
    {
        var id = Text(root, "id");
        try
        {
            var flavor = DetectFlavor(root);
            var template = new BpmnElementTemplate
            {
                Schema = Text(root, "$schema"),
                Id = id ?? string.Empty,
                Name = Text(root, "name") ?? string.Empty,
                Version = Int(root, "version"),
                Description = Text(root, "description"),
                Keywords = StringArray(root, "keywords"),
                DocumentationRef = Text(root, "documentationRef"),
                Icon = ReadIcon(root),
                Category = ReadCategory(root),
                Engines = ReadStringDictionary(root, "engines"),
                AppliesTo = StringArray(root, "appliesTo"),
                ElementType = ReadElementType(root),
                Groups = ReadGroups(root),
                Properties = ReadProperties(root, "properties"),
                Scopes = ReadScopes(root),
                EntriesVisible = ReadEntriesVisible(root),
                Deprecated = ReadDeprecated(root),
                Flavor = flavor
            };
            var issues = Validate(template);
            if (issues.Count > 0)
            {
                foreach (var issue in issues) errors.Add(new("EXT-004", issue, index, id));
                return;
            }
            templates.Add(template);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            errors.Add(new("EXT-004", ex.Message, index, id));
        }
    }

    private static List<string> Validate(BpmnElementTemplate template)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(template.Schema)) errors.Add("Required field '$schema' is missing.");
        if (string.IsNullOrWhiteSpace(template.Id)) errors.Add("Required field 'id' is missing.");
        if (string.IsNullOrWhiteSpace(template.Name)) errors.Add("Required field 'name' is missing.");
        if (template.AppliesTo.Count == 0) errors.Add("Required non-empty field 'appliesTo' is missing.");
        if (template.Properties.Count == 0) errors.Add("Required non-empty field 'properties' is missing.");
        if (template.Version < 0) errors.Add("'version' must be a non-negative integer.");

        if (template.Scopes.Select(BpmnTemplateScopes.Prefix).Distinct(StringComparer.Ordinal).Count() != template.Scopes.Count)
            errors.Add("Scope identities must be unique.");
        template = BpmnTemplateScopes.Flatten(template);
        var groupIds = template.Groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        if (groupIds.Count != template.Groups.Count) errors.Add("Group ids must be unique.");
        var propertyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in AllProperties(template))
        {
            if (property.Id is not null && !propertyIds.Add(property.Id)) errors.Add($"Duplicate property id '{property.Id}'.");
            if (property.Group is not null && !groupIds.Contains(property.Group)) errors.Add($"Property '{property.Id ?? property.Label}' references undeclared group '{property.Group}'.");
            if (property.Type == BpmnTemplatePropertyType.Dropdown && property.Choices.Count == 0)
                errors.Add($"Dropdown property '{property.Id ?? property.Label}' must declare choices.");
            if (property.GeneratedValue is { Type: not "uuid" }) errors.Add($"Unsupported generated value type '{property.GeneratedValue.Type}'.");
            if (property.Binding is null)
            {
                errors.Add($"Property '{property.Id ?? property.Label}' is missing binding.");
                continue;
            }
            if (!BpmnTemplateBinding.KnownTypes(template.Flavor).Contains(property.Binding.Type))
                errors.Add($"Unknown {template.Flavor} binding type '{property.Binding.Type}'.");
            else if (!HasRequiredBindingParameters(property.Binding))
                errors.Add($"Binding '{property.Binding.Type}' is missing a required parameter.");
            if (property.Condition is not null)
                foreach (var condition in Conditions(property.Condition))
                    if (!propertyIds.Contains(condition.Property) && !AllProperties(template).Any(p => p.Id == condition.Property))
                        errors.Add($"Condition references unknown property id '{condition.Property}'.");
        }
        return errors;
    }

    private static bool HasRequiredBindingParameters(BpmnTemplateBinding binding) => binding.Type switch
    {
        "zeebe:input" => binding.Get("name") is not null,
        "zeebe:output" => binding.Get("source") is not null,
        "zeebe:taskHeader" => binding.Get("key") is not null,
        "zeebe:taskDefinition" => binding.Get("property") is not null,
        "property" or "camunda:property" or "camunda:inputParameter" or "camunda:field" => binding.Get("name") is not null,
        "camunda:outputParameter" => binding.Get("source") is not null,
        _ => true
    };

    private static IEnumerable<BpmnTemplateProperty> AllProperties(BpmnElementTemplate template) =>
        template.Properties.Concat(template.Scopes.SelectMany(s => s.Properties));
    private static IEnumerable<PropertyCondition> Conditions(BpmnTemplateCondition condition) => condition switch
    {
        PropertyCondition simple => [simple],
        AllMatchCondition all => all.Conditions,
        _ => []
    };

    private static BpmnTemplateFlavor DetectFlavor(JsonElement root)
    {
        var schema = Text(root, "$schema") ?? string.Empty;
        if (schema.Contains("zeebe-element-templates-json-schema", StringComparison.OrdinalIgnoreCase)) return BpmnTemplateFlavor.Zeebe;
        if (schema.Contains("element-templates-json-schema", StringComparison.OrdinalIgnoreCase)) return BpmnTemplateFlavor.Camunda7;
        if (ReadScopes(root).Length > 0) return BpmnTemplateFlavor.Camunda7;
        foreach (var prop in ReadProperties(root, "properties"))
            if (prop.Binding?.Type.StartsWith("camunda:", StringComparison.Ordinal) == true) return BpmnTemplateFlavor.Camunda7;
        return BpmnTemplateFlavor.Zeebe;
    }

    private static BpmnTemplateProperty[] ReadProperties(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array) return [];
        return array.EnumerateArray().Select(ReadProperty).ToArray();
    }
    private static BpmnTemplateProperty ReadProperty(JsonElement e)
    {
        var typeText = Text(e, "type") ?? "String";
        if (!Enum.TryParse<BpmnTemplatePropertyType>(typeText, true, out var type)) throw new FormatException($"Unsupported property type '{typeText}'.");
        var feelText = Text(e, "feel");
        var feel = feelText is null ? BpmnTemplateFeel.None : feelText.ToLowerInvariant() switch
        {
            "optional" => BpmnTemplateFeel.Optional,
            "required" => BpmnTemplateFeel.Required,
            "static" => BpmnTemplateFeel.Static,
            _ => throw new FormatException($"Unsupported feel mode '{feelText}'.")
        };
        return new BpmnTemplateProperty
        {
            Id = Text(e, "id"),
            Label = Text(e, "label"),
            Description = Text(e, "description"),
            Tooltip = Text(e, "tooltip"),
            Type = type,
            Value = Scalar(e, "value"),
            GeneratedValue = ReadGeneratedValue(e),
            Placeholder = Text(e, "placeholder"),
            Choices = ReadChoices(e),
            Feel = feel,
            Optional = Bool(e, "optional") ?? false,
            Editable = Bool(e, "editable") ?? true,
            Group = Text(e, "group"),
            Constraints = ReadConstraints(e),
            Condition = ReadCondition(e),
            Binding = ReadBinding(e)
        };
    }
    private static BpmnTemplateBinding? ReadBinding(JsonElement e)
    {
        if (!e.TryGetProperty("binding", out var binding) || binding.ValueKind != JsonValueKind.Object) return null;
        var type = Text(binding, "type");
        if (string.IsNullOrWhiteSpace(type)) return null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in binding.EnumerateObject())
            if (item.Name != "type") values[item.Name] = item.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                ? item.Value.GetRawText() : ScalarValue(item.Value) ?? string.Empty;
        return new(type, values);
    }
    private static BpmnTemplateCondition? ReadCondition(JsonElement e)
    {
        if (!e.TryGetProperty("condition", out var c) || c.ValueKind != JsonValueKind.Object) return null;
        if (c.TryGetProperty("allMatch", out var all) && all.ValueKind == JsonValueKind.Array)
            return new AllMatchCondition(all.EnumerateArray().Select(ReadSimpleCondition).ToArray());
        return ReadSimpleCondition(c);
    }
    private static PropertyCondition ReadSimpleCondition(JsonElement e)
    {
        var property = Text(e, "property") ?? throw new FormatException("Condition is missing 'property'.");
        if (e.TryGetProperty("equals", out var equals)) return new(property, BpmnTemplateConditionOperator.Equals, ScalarValue(equals));
        if (e.TryGetProperty("oneOf", out var oneOf) && oneOf.ValueKind == JsonValueKind.Array)
            return new(property, BpmnTemplateConditionOperator.OneOf, Values: oneOf.EnumerateArray().Select(ScalarValue).Where(v => v is not null).Cast<string>().ToArray());
        if (e.TryGetProperty("isActive", out var active)) return new(property, BpmnTemplateConditionOperator.IsActive, BooleanValue: active.ValueKind == JsonValueKind.True);
        if (e.TryGetProperty("isEmpty", out var empty)) return new(property, BpmnTemplateConditionOperator.IsEmpty, BooleanValue: empty.ValueKind == JsonValueKind.True);
        throw new FormatException("Condition must specify equals, oneOf, isActive, or isEmpty.");
    }
    private static BpmnTemplateScope[] ReadScopes(JsonElement e)
    {
        if (!e.TryGetProperty("scopes", out var scopes) || scopes.ValueKind != JsonValueKind.Array) return [];
        return scopes.EnumerateArray().Select(scope =>
        {
            var type = Text(scope, "type") ?? throw new FormatException("Scope is missing 'type'.");
            if (!scope.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array)
                throw new FormatException($"Scope '{type}' is missing its properties array.");
            var id = Text(scope, "id");
            if (type == "bpmn:Error" && string.IsNullOrWhiteSpace(id))
                throw new FormatException("A bpmn:Error scope requires an id.");
            if (type is not ("camunda:Connector" or "bpmn:Error"))
                throw new FormatException($"Unsupported Camunda 7 scope type '{type}'.");
            return new BpmnTemplateScope(type, ReadProperties(scope, "properties"), id);
        }).ToArray();
    }
    private static BpmnTemplateGroup[] ReadGroups(JsonElement e)
    {
        if (!e.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array) return [];
        return groups.EnumerateArray().Select(g => new BpmnTemplateGroup(
            Text(g, "id") ?? throw new FormatException("Group is missing 'id'."),
            Text(g, "label") ?? throw new FormatException("Group is missing 'label'."), Text(g, "tooltip"), Bool(g, "openByDefault"))).ToArray();
    }
    private static BpmnTemplateChoice[] ReadChoices(JsonElement e)
    {
        if (!e.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array) return [];
        return choices.EnumerateArray().Select(c => new BpmnTemplateChoice(Text(c, "name") ?? string.Empty, Text(c, "value") ?? string.Empty)).ToArray();
    }
    private static BpmnTemplateGeneratedValue? ReadGeneratedValue(JsonElement e) =>
        e.TryGetProperty("generatedValue", out var value) && value.ValueKind == JsonValueKind.Object ? new(Text(value, "type") ?? string.Empty) : null;
    private static BpmnTemplateConstraints? ReadConstraints(JsonElement e)
    {
        if (!e.TryGetProperty("constraints", out var c) || c.ValueKind != JsonValueKind.Object) return null;
        BpmnTemplatePattern? pattern = null;
        if (c.TryGetProperty("pattern", out var p) && p.ValueKind == JsonValueKind.Object)
            pattern = new(Text(p, "value") ?? string.Empty, Text(p, "message"));
        return new(Bool(c, "notEmpty"), Int(c, "minLength"), Int(c, "maxLength"), pattern);
    }
    private static BpmnTemplateEntriesVisible ReadEntriesVisible(JsonElement e)
    {
        if (!e.TryGetProperty("entriesVisible", out var visible)) return new();
        if (visible.ValueKind is JsonValueKind.True or JsonValueKind.False) return new(visible.GetBoolean());
        if (visible.ValueKind == JsonValueKind.Object)
            return new(null, visible.EnumerateObject().Where(p => p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                .ToDictionary(p => p.Name, p => p.Value.GetBoolean(), StringComparer.Ordinal));
        return new();
    }
    private static BpmnTemplateDeprecated? ReadDeprecated(JsonElement e)
    {
        if (!e.TryGetProperty("deprecated", out var d) || d.ValueKind == JsonValueKind.False || d.ValueKind == JsonValueKind.Null) return null;
        if (d.ValueKind == JsonValueKind.True) return new();
        if (d.ValueKind == JsonValueKind.String) return new(d.GetString());
        if (d.ValueKind == JsonValueKind.Object) return new(Text(d, "message"), Text(d, "documentationRef"));
        return null;
    }
    private static BpmnTemplateCategory? ReadCategory(JsonElement e)
    {
        if (!e.TryGetProperty("category", out var c) || c.ValueKind != JsonValueKind.Object) return null;
        return new(Text(c, "id") ?? string.Empty, Text(c, "name") ?? string.Empty);
    }
    private static BpmnTemplateElementType? ReadElementType(JsonElement e)
    {
        if (!e.TryGetProperty("elementType", out var t) || t.ValueKind != JsonValueKind.Object) return null;
        var value = Text(t, "value");
        return value is null ? null : new(value, Text(t, "eventDefinition"));
    }
    private static string? ReadIcon(JsonElement e)
    {
        if (!e.TryGetProperty("icon", out var icon)) return null;
        return icon.ValueKind == JsonValueKind.Object ? Text(icon, "contents") : ScalarValue(icon);
    }
    private static Dictionary<string, string> ReadStringDictionary(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var obj) || obj.ValueKind != JsonValueKind.Object) return [];
        return obj.EnumerateObject().ToDictionary(p => p.Name, p => ScalarValue(p.Value) ?? string.Empty, StringComparer.Ordinal);
    }
    private static string[] StringArray(JsonElement e, string name) =>
        e.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Select(ScalarValue).Where(s => s is not null).Cast<string>().ToArray() : [];
    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool? Bool(JsonElement e, string name) => e.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
    private static int? Int(JsonElement e, string name) => e.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
    private static string? Scalar(JsonElement e, string name) => e.TryGetProperty(name, out var value) ? ScalarValue(value) : null;
    private static string? ScalarValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
        JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
        _ => null
    };
    private static BpmnTemplateLoadResult Failure(string message) => new([], [Error(message)]);
    private static BpmnTemplateLoadError Error(string message, int? index = null) => new("EXT-004", message, index);
}


