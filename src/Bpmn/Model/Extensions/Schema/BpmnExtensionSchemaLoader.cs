using System.Text;
using System.Text.Json;

namespace Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;

/// <summary>Loads trusted, local moddle descriptors without reflection or network access.</summary>
public static class BpmnExtensionSchemaLoader
{
    /// <summary>Maximum UTF-8 descriptor size (4 MiB).</summary>
    public const int MaximumInputBytes = 4 * 1024 * 1024;

    /// <summary>Loads a UTF-8 stream without taking ownership of it.</summary>
    public static BpmnExtensionSchema Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int count;
        while ((count = stream.Read(bytes, 0, Math.Min(bytes.Length, MaximumInputBytes + 1 - (int)buffer.Length))) > 0)
        {
            buffer.Write(bytes, 0, count);
            if (buffer.Length > MaximumInputBytes)
                throw new BpmnExtensionSchemaException($"Extension schema exceeds {MaximumInputBytes} bytes.");
        }
        return Parse(buffer.ToArray());
    }

    /// <summary>Loads a moddle JSON string.</summary>
    public static BpmnExtensionSchema Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > MaximumInputBytes)
            throw new BpmnExtensionSchemaException($"Extension schema exceeds {MaximumInputBytes} bytes.");
        return Parse(Encoding.UTF8.GetBytes(json));
    }

    private static BpmnExtensionSchema Parse(byte[] bytes)
    {
        try
        {
            var input = bytes.AsMemory();
            if (input.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) input = input[3..];
            using var document = JsonDocument.Parse(input, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            var name = Required(root, "name");
            var uri = Required(root, "uri");
            var prefix = Required(root, "prefix");
            if (!System.Uri.TryCreate(uri, UriKind.Absolute, out _)) Fail("uri must be an absolute namespace URI.");
            System.Xml.XmlConvert.VerifyNCName(prefix);
            var xml = Object(root, "xml");
            var alias = Optional(xml, "tagAlias");
            var types = Array(root, "types", required: true).Select(t =>
            {
                var typeName = Required(t, "name");
                System.Xml.XmlConvert.VerifyNCName(typeName);
                var properties = Array(t, "properties").Select(p => new BpmnExtensionProperty(
                    Required(p, "name"), Required(p, "type"), Bool(p, "isAttr"), Bool(p, "isMany"),
                    Bool(p, "isBody"), Bool(p, "isReference"), Scalar(p, "default"))
                {
                    Serialize = Optional(Object(p, "xml"), "serialize") ?? Optional(p, "serialize"),
                    NamespaceUri = uri,
                    Prefix = prefix,
                    DeclaringType = $"{prefix}:{typeName}"
                }).ToArray();
                Unique(properties.Select(p => p.Name), $"properties of {typeName}");
                return new BpmnExtensionType(typeName, Strings(t, "superClass"), Strings(t, "extends"), System.Array.AsReadOnly(properties))
                {
                    NamespaceUri = uri,
                    Prefix = prefix,
                    ElementName = alias == "lowerCase" ? char.ToLowerInvariant(typeName[0]) + typeName[1..] : typeName,
                    IsAbstract = Bool(t, "isAbstract"),
                    AllowedIn = Strings(Object(t, "meta"), "allowedIn")
                };
            }).ToArray();
            Unique(types.Select(t => t.Name), "types");
            var enumerations = Array(root, "enumerations").Select(e => new BpmnExtensionEnumeration(
                Required(e, "name"), System.Array.AsReadOnly(Array(e, "literalValues", required: true).Select(v => Required(v, "name")).ToArray()))).ToArray();
            Unique(enumerations.Select(e => e.Name), "enumerations");
            return new(name, uri, prefix, System.Array.AsReadOnly(types))
            {
                Version = Scalar(root, "version"),
                TagAlias = alias,
                TypePrefix = Optional(xml, "typePrefix"),
                Enumerations = System.Array.AsReadOnly(enumerations)
            };
        }
        catch (BpmnExtensionSchemaException) { throw; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or System.Xml.XmlException or ArgumentException)
        {
            throw new BpmnExtensionSchemaException("Malformed moddle extension schema: " + ex.Message, ex);
        }
    }

    private static void Fail(string message) => throw new BpmnExtensionSchemaException("Malformed moddle extension schema: " + message);
    private static string Required(JsonElement element, string name) => Optional(element, name) is { Length: > 0 } value ? value : throw new BpmnExtensionSchemaException($"Missing non-empty string '{name}'.");
    private static string? Optional(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String) Fail($"'{name}' must be a string.");
        return value.GetString();
    }
    private static string? Scalar(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number) return value.GetRawText();
        throw new BpmnExtensionSchemaException($"'{name}' must be a scalar.");
    }
    private static bool Bool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) Fail($"'{name}' must be boolean.");
        return value.GetBoolean();
    }
    private static JsonElement Object(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(name, out var value)) return default;
        if (value.ValueKind != JsonValueKind.Object) Fail($"'{name}' must be an object.");
        return value;
    }
    private static JsonElement[] Array(JsonElement element, string name, bool required = false)
    {
        if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(name, out var value))
        {
            if (required) Fail($"Missing array '{name}'.");
            return [];
        }
        if (value.ValueKind != JsonValueKind.Array) Fail($"'{name}' must be an array.");
        return value.EnumerateArray().ToArray();
    }
    private static System.Collections.ObjectModel.ReadOnlyCollection<string> Strings(JsonElement element, string name) => System.Array.AsReadOnly(Array(element, name).Select(v =>
        v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()! : throw new BpmnExtensionSchemaException($"'{name}' must contain non-empty strings.")).ToArray());
    private static void Unique(IEnumerable<string> values, string kind)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values) if (!names.Add(value)) Fail($"Duplicate name '{value}' in {kind}.");
    }
}
