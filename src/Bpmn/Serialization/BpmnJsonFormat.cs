using System.Collections;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>
/// The identity and version of this library's BPMN JSON format.
/// </summary>
/// <remarks>
/// <para>
/// A file is an envelope around the semantic model:
/// <c>{ "format": "hasseware.bpmn", "version": "1.1", "document": { … } }</c>. The document is
/// <see cref="BpmnDocument"/> as camelCase JSON — the same member names the canvas interop uses —
/// with enums as strings, vendor attributes keyed <c>{namespaceUri}localName</c> and
/// <c>extensionElements</c> as ordered trees, so everything the XML reader keeps survives.
/// </para>
/// <para>
/// Versioning follows the usual contract. A reader accepts every minor version of its own major
/// version — a newer minor may add properties, which are ignored — and refuses any other major
/// version, because a major bump means existing properties changed meaning.
/// </para>
/// </remarks>
public static class BpmnJsonFormat
{
    /// <summary>The value of the envelope's <c>format</c> property.</summary>
    public const string Name = "hasseware.bpmn";

    /// <summary>The format version this library writes.</summary>
    public const string Version = "1.1";

    /// <summary>The major version this library reads.</summary>
    public const int MajorVersion = 1;

    /// <summary>The minor version this library writes.</summary>
    public const int MinorVersion = 1;

    /// <summary>The conventional file extension.</summary>
    public const string FileExtension = ".json";

    /// <summary>The media type a download should declare.</summary>
    public const string MediaType = "application/json";

    /// <summary>The default for <see cref="BpmnJsonReadOptions.MaxInputBytes"/>: 64 MiB.</summary>
    public const long DefaultMaxInputBytes = 64L * 1024 * 1024;

    /// <summary>The default for <see cref="BpmnJsonReadOptions.MaxDepth"/>.</summary>
    public const int DefaultMaxDepth = 256;

    internal const string FormatProperty = "format";
    internal const string VersionProperty = "version";
    internal const string DocumentProperty = "document";

    private static readonly JsonSerializerOptions DefaultIndented = CreateOptions(DefaultMaxDepth, indent: true);
    private static readonly JsonSerializerOptions DefaultCompact = CreateOptions(DefaultMaxDepth, indent: false);

    internal static JsonSerializerOptions Options(int maxDepth, bool indent)
    {
        if (maxDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth), maxDepth, "The maximum depth must be positive.");
        }

        if (maxDepth == DefaultMaxDepth) return indent ? DefaultIndented : DefaultCompact;

        return CreateOptions(maxDepth, indent);
    }

    /// <summary>
    /// Serializer options matching the source-generated context.
    /// </summary>
    /// <remarks>
    /// Source-generated metadata only: the library supports trimmed WebAssembly hosts, and
    /// reflection-based serialization would be trimmed away there.
    /// </remarks>
    private static JsonSerializerOptions CreateOptions(int maxDepth, bool indent)
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = BpmnJsonContext.Default.WithAddedModifier(OmitDefaults),
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IgnoreReadOnlyProperties = true,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            AllowDuplicateProperties = false,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            // A file, never inline HTML: escaping every '<' in a preserved XML attribute value as
            // \u003C makes the output much harder to read for no protection.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            MaxDepth = maxDepth,
            WriteIndented = indent,
        };

        options.MakeReadOnly();
        return options;
    }

    /// <summary>
    /// Leaves out what reading would restore anyway: constructor arguments equal to their default,
    /// and empty extras and collections that the model initialises to empty.
    /// </summary>
    /// <remarks>
    /// Only properties without a constructor argument are omitted when empty. A required list
    /// argument that went missing would be read back as <see langword="null"/>, so those are always
    /// written.
    /// </remarks>
    private static void OmitDefaults(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object) return;

        foreach (var property in typeInfo.Properties)
        {
            var parameter = property.AssociatedParameter;

            if (parameter is { IsMemberInitializer: false, HasDefaultValue: true })
            {
                var fallback = parameter.DefaultValue;
                property.ShouldSerialize = (_, value) => !Equals(value, fallback);
            }
            else if (parameter is null or { IsMemberInitializer: true } && !property.IsRequired)
            {
                if (property.PropertyType == typeof(BpmnElementExtras))
                {
                    property.ShouldSerialize = static (_, value) => value is BpmnElementExtras { IsEmpty: false };
                }
                else if (property.PropertyType == typeof(BpmnRootCatalog))
                {
                    property.ShouldSerialize = static (_, value) => value is BpmnRootCatalog { IsEmpty: false };
                }
                else if (typeof(IEnumerable).IsAssignableFrom(property.PropertyType)
                    && property.PropertyType != typeof(string))
                {
                    property.ShouldSerialize = static (_, value) => value is not null and not ICollection { Count: 0 };
                }
            }
        }
    }

    /// <summary>
    /// Puts back, as empty JSON, every member the writer leaves out when it is empty.
    /// </summary>
    /// <remarks>
    /// The source generator assigns every init-only property when it constructs a record, so a
    /// member missing from the file would arrive as <see langword="null"/> rather than keeping its
    /// initialiser. Restoring it in the JSON first lets the serializer build the empty value, and
    /// keeps hand-written files that leave out empty lists and extras readable.
    /// </remarks>
    internal static void RestoreOmitted(JsonNode? node, JsonTypeInfo typeInfo)
    {
        switch (typeInfo.Kind)
        {
            case JsonTypeInfoKind.Object when node is JsonObject json:
                foreach (var property in typeInfo.Properties)
                {
                    if (!json.TryGetPropertyValue(property.Name, out var value))
                    {
                        // Written as absent when null, but a constructor argument must still be present.
                        if (property.AssociatedParameter is { IsMemberInitializer: false, HasDefaultValue: false, IsNullable: true })
                        {
                            json[property.Name] = null;
                            continue;
                        }

                        if (!IsOmittable(property)) continue;

                        value = property.PropertyType == typeof(BpmnElementExtras)
                            || property.PropertyType == typeof(BpmnRootCatalog)
                            || typeInfo.Options.GetTypeInfo(property.PropertyType).Kind == JsonTypeInfoKind.Dictionary
                                ? new JsonObject()
                                : new JsonArray();
                        json[property.Name] = value;
                    }

                    if (value is not null)
                    {
                        RestoreOmitted(value, typeInfo.Options.GetTypeInfo(property.PropertyType));
                    }
                }

                break;

            case JsonTypeInfoKind.Enumerable when node is JsonArray array && typeInfo.ElementType is { } elementType:
                var elementInfo = typeInfo.Options.GetTypeInfo(elementType);
                foreach (var item in array)
                {
                    RestoreOmitted(item, elementInfo);
                }

                break;

            case JsonTypeInfoKind.Dictionary when node is JsonObject map && typeInfo.ElementType is { } valueType:
                var valueInfo = typeInfo.Options.GetTypeInfo(valueType);
                foreach (var (_, item) in map)
                {
                    RestoreOmitted(item, valueInfo);
                }

                break;
        }
    }

    /// <summary>Whether the writer leaves a property out when it is empty.</summary>
    private static bool IsOmittable(JsonPropertyInfo property) =>
        property.AssociatedParameter is null or { IsMemberInitializer: true }
        && !property.IsRequired
        && (property.PropertyType == typeof(BpmnElementExtras)
            || property.PropertyType == typeof(BpmnRootCatalog)
            || (typeof(IEnumerable).IsAssignableFrom(property.PropertyType) && property.PropertyType != typeof(string)));
}

/// <summary>The file envelope: format identity and version around the document.</summary>
internal sealed record BpmnJsonEnvelope(string Format, string Version, BpmnDocument Document);

/// <summary>Source-generated metadata for the BPMN JSON format.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    IgnoreReadOnlyProperties = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    AllowDuplicateProperties = false,
    UseStringEnumConverter = true,
    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(BpmnJsonEnvelope))]
[JsonSerializable(typeof(BpmnDocument))]
internal sealed partial class BpmnJsonContext : JsonSerializerContext;
