using System.Text.Json;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

internal sealed class FeelSharpJsonContext
{
    internal static Dictionary<string, object?> Read(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Sample context must be a JSON object.");
        }
        return Object(document.RootElement);
    }

    private static Dictionary<string, object?> Object(JsonElement value)
    {
        var context = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!context.TryAdd(property.Name, Convert(property.Value)))
            {
                throw new JsonException($"Duplicate sample property: {property.Name}.");
            }
        }
        return context;
    }

    private static object? Convert(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => Object(value),
        JsonValueKind.Array => value.EnumerateArray().Select(Convert).ToArray(),
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.TryGetDecimal(out var number) ? number : throw new JsonException("Sample number exceeds decimal precision/range."),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new JsonException("Unsupported sample value."),
    };
}
