using System.Buffers;
using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

internal static class FeelSharpResultJson
{
    internal static string Serialize(object? value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, MaxDepth = 64 });
        Write(writer, value);
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void Write(Utf8JsonWriter writer, object? value)
    {
        if (value is IEnumerable and not string && writer.CurrentDepth >= 64)
        {
            throw new JsonException("The FEEL result exceeds the maximum JSON depth of 64.");
        }

        switch (value)
        {
            case null:
                writer.WriteNullValue();
                return;
            case IReadOnlyDictionary<string, object?> context:
                writer.WriteStartObject();
                foreach (var pair in context)
                {
                    writer.WritePropertyName(pair.Key);
                    Write(writer, pair.Value);
                }
                writer.WriteEndObject();
                return;
            case IDictionary context:
                writer.WriteStartObject();
                foreach (DictionaryEntry pair in context)
                {
                    if (pair.Key is not string key)
                    {
                        throw new JsonException("FEEL context keys must be strings.");
                    }
                    writer.WritePropertyName(key);
                    Write(writer, pair.Value);
                }
                writer.WriteEndObject();
                return;
            case IEnumerable list when value is not string:
                writer.WriteStartArray();
                foreach (var item in list)
                {
                    Write(writer, item);
                }
                writer.WriteEndArray();
                return;
            default:
                var info = FeelSharpResultJsonContext.Default.GetTypeInfo(value.GetType())
                    ?? throw new JsonException($"FEEL result type '{value.GetType().FullName}' has no JSON contract.");
                JsonSerializer.Serialize(writer, value, info);
                return;
        }
    }
}

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(decimal))]
internal sealed partial class FeelSharpResultJsonContext : JsonSerializerContext;
