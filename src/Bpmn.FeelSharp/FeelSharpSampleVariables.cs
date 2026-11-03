using System.Text.Json;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Bounded, non-executing variable inference from JSON examples or an inline schema subset.</summary>
public sealed class FeelSharpSampleVariables
{
    /// <summary>Infers members from a JSON object. Strings remain strings, including ISO-looking values.</summary>
    public static IReadOnlyList<BpmnFeelVariable> FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Sample context must be a JSON object.");
        }
        var remaining = 1000;
        return ObjectMembers(document.RootElement, ref remaining);
    }

    /// <summary>Infers inline type/properties/items schemas. References and combinators are unsupported.</summary>
    public static IReadOnlyList<BpmnFeelVariable> FromSchema(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var remaining = 1000;
        return SchemaMembers(document.RootElement, ref remaining);
    }

    private static System.Collections.ObjectModel.ReadOnlyCollection<BpmnFeelVariable> ObjectMembers(JsonElement value, ref int remaining)
    {
        var variables = new List<BpmnFeelVariable>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new JsonException($"Duplicate sample property: {property.Name}.");
            }
            Count(ref remaining);
            variables.Add(new(property.Name, Kind(property.Value), "Inferred from sample JSON; runtime availability unknown.")
            {
                Source = "Sample JSON",
                IsInferred = true,
                Entries = Members(property.Value, ref remaining),
            });
        }
        return variables.AsReadOnly();
    }

    private static IReadOnlyList<BpmnFeelVariable> Members(JsonElement value, ref int remaining)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            return ObjectMembers(value, ref remaining);
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            var entries = new Dictionary<string, BpmnFeelVariable>(StringComparer.Ordinal);
            foreach (var item in value.EnumerateArray())
            {
                Count(ref remaining);
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                foreach (var member in ObjectMembers(item, ref remaining))
                {
                    if (entries.TryGetValue(member.Name, out var existing))
                    {
                        entries[member.Name] = Merge(existing, member);
                    }
                    else
                    {
                        entries[member.Name] = member;
                    }
                }
            }
            return entries.Values.OrderBy(variable => variable.Name, StringComparer.Ordinal).ToArray();
        }
        return [];
    }

    private static BpmnFeelVariable Merge(BpmnFeelVariable first, BpmnFeelVariable second)
    {
        if (first.Detail != second.Detail)
        {
            return first with { Detail = "unknown", Entries = [] };
        }
        var entries = first.Entries.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        foreach (var member in second.Entries)
        {
            entries[member.Name] = entries.TryGetValue(member.Name, out var existing) ? Merge(existing, member) : member;
        }
        return first with { Entries = entries.Values.OrderBy(variable => variable.Name, StringComparer.Ordinal).ToArray() };
    }

    private static IReadOnlyList<BpmnFeelVariable> SchemaMembers(JsonElement value, ref int remaining)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Schema nodes must be objects.");
        }
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name is not ("type" or "properties" or "items" or "description" or "title" or "$schema"))
            {
                throw new JsonException($"Unsupported schema keyword: {property.Name}.");
            }
            if (property.Name is "description" or "title" or "$schema" && property.Value.ValueKind != JsonValueKind.String)
            {
                throw new JsonException($"Schema {property.Name} must be a string.");
            }
        }
        if (value.TryGetProperty("type", out var declaredType) && (declaredType.ValueKind != JsonValueKind.String ||
            declaredType.GetString() is not ("object" or "array" or "string" or "number" or "integer" or "boolean" or "null")))
        {
            throw new JsonException("Unsupported schema type.");
        }
        if (value.TryGetProperty("items", out var items))
        {
            return SchemaMembers(items, ref remaining);
        }
        if (!value.TryGetProperty("properties", out var properties))
        {
            return [];
        }
        if (properties.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Schema properties must be an object.");
        }
        var variables = new List<BpmnFeelVariable>();
        foreach (var property in properties.EnumerateObject())
        {
            Count(ref remaining);
            var schema = property.Value;
            var members = SchemaMembers(schema, ref remaining);
            var type = schema.TryGetProperty("type", out var declared) ? declared.GetString() : "unknown";
            if (type is not ("object" or "array" or "string" or "number" or "integer" or "boolean" or "null" or "unknown"))
            {
                throw new JsonException($"Unsupported schema type: {type}.");
            }
            variables.Add(new(property.Name, type, schema.TryGetProperty("description", out var description) ? description.GetString() : null)
            { Source = "Inline schema", IsInferred = true, Entries = members });
        }
        return variables.AsReadOnly();
    }

    private static string Kind(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "context",
        JsonValueKind.Array => "list",
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Null => "null",
        _ => "unknown",
    };

    private static void Count(ref int remaining)
    {
        if (--remaining < 0)
        {
            throw new JsonException("Sample inference exceeds the 1000-node limit.");
        }
    }
}
