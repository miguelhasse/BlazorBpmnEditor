using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>
/// Reads BPMN JSON (see <see cref="BpmnJsonFormat"/>) into this library's semantic model.
/// </summary>
/// <remarks>
/// <para>
/// The JSON counterpart of <see cref="BpmnXmlReader"/>, with the same contract: input that cannot
/// be read at all throws (<see cref="BpmnJsonException"/>, a <see cref="BpmnImportException"/>
/// like its XML sibling), and everything else is returned as a <see cref="BpmnImportResult"/> with
/// its findings, so the import preview treats both formats alike.
/// </para>
/// <para>
/// Untrusted input is bounded before it is parsed: the size and depth limits come from
/// <see cref="BpmnJsonReadOptions"/>. Unknown properties are ignored, so a file written by a newer
/// minor version still opens; a different major version is refused.
/// </para>
/// </remarks>
public static class BpmnJsonReader
{
    /// <summary>Reads a document from JSON text.</summary>
    /// <param name="json">The BPMN JSON to read.</param>
    /// <param name="options">Size and depth limits; <see cref="BpmnJsonReadOptions.Default"/> when omitted.</param>
    /// <returns>The document and every finding produced while reading it.</returns>
    /// <exception cref="BpmnJsonException">The input is not BPMN JSON this library can read.</exception>
    public static BpmnImportResult Read(string json, BpmnJsonReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(json);

        options ??= BpmnJsonReadOptions.Default;
        if (options.MaxInputBytes > 0 && Encoding.UTF8.GetByteCount(json) > options.MaxInputBytes)
        {
            throw TooLarge(options);
        }

        // A string read from a file may still carry the byte-order mark the parser rejects.
        var text = json.AsMemory();
        if (text.Span is ['\uFEFF', ..]) text = text[1..];

        return Parse(options, documentOptions => JsonDocument.Parse(text, documentOptions));
    }

    /// <summary>Reads a document from a stream of UTF-8 JSON.</summary>
    /// <param name="stream">A stream positioned at the start of the JSON; left open.</param>
    /// <param name="options">Size and depth limits; <see cref="BpmnJsonReadOptions.Default"/> when omitted.</param>
    /// <returns>The document and every finding produced while reading it.</returns>
    /// <exception cref="BpmnJsonException">The input is not BPMN JSON this library can read.</exception>
    public static BpmnImportResult Read(Stream stream, BpmnJsonReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        options ??= BpmnJsonReadOptions.Default;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk)) > 0)
        {
            Append(buffer, chunk, read, options);
        }

        return ReadBuffer(buffer, options);
    }

    /// <summary>Reads a document from a stream of UTF-8 JSON asynchronously.</summary>
    /// <param name="stream">A stream positioned at the start of the JSON; left open.</param>
    /// <param name="options">Size and depth limits; <see cref="BpmnJsonReadOptions.Default"/> when omitted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The document and every finding produced while reading it.</returns>
    /// <exception cref="BpmnJsonException">The input is not BPMN JSON this library can read.</exception>
    public static async Task<BpmnImportResult> ReadAsync(
        Stream stream,
        BpmnJsonReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        options ??= BpmnJsonReadOptions.Default;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            Append(buffer, chunk, read, options);
        }

        return ReadBuffer(buffer, options);
    }

    private static void Append(MemoryStream buffer, byte[] chunk, int count, BpmnJsonReadOptions options)
    {
        // Checked while reading rather than after, so an endless stream is cut off at the limit.
        if (options.MaxInputBytes > 0 && buffer.Length + count > options.MaxInputBytes)
        {
            throw TooLarge(options);
        }

        buffer.Write(chunk, 0, count);
    }

    private static BpmnImportResult ReadBuffer(MemoryStream buffer, BpmnJsonReadOptions options)
    {
        ReadOnlyMemory<byte> bytes = buffer.GetBuffer().AsMemory(0, (int)buffer.Length);
        if (bytes.Span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF])) bytes = bytes[3..];

        return Parse(options, documentOptions => JsonDocument.Parse(bytes, documentOptions));
    }

    private static BpmnImportResult Parse(BpmnJsonReadOptions options, Func<JsonDocumentOptions, JsonDocument> parse)
    {
        if (options.MaxDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxDepth, "The maximum depth must be positive.");
        }

        JsonDocument json;
        try
        {
            json = parse(new JsonDocumentOptions
            {
                MaxDepth = options.MaxDepth,
                AllowDuplicateProperties = false,
            });
        }
        catch (JsonException exception)
        {
            throw new BpmnJsonException($"The document is not well-formed JSON: {exception.Message}", exception);
        }

        using (json)
        {
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new BpmnJsonException("The document root is not a JSON object.");
            }

            if (!root.TryGetProperty(BpmnJsonFormat.FormatProperty, out var format)
                || format.ValueKind != JsonValueKind.String
                || !string.Equals(format.GetString(), BpmnJsonFormat.Name, StringComparison.Ordinal))
            {
                throw new BpmnJsonException(
                    $"The document is not BPMN JSON: expected a \"{BpmnJsonFormat.FormatProperty}\" property of \"{BpmnJsonFormat.Name}\".");
            }

            var diagnostics = new List<BpmnImportDiagnostic>();
            CheckVersion(root, diagnostics);

            if (!root.TryGetProperty(BpmnJsonFormat.DocumentProperty, out var body)
                || body.ValueKind != JsonValueKind.Object)
            {
                throw new BpmnJsonException(
                    $"The document has no \"{BpmnJsonFormat.DocumentProperty}\" object.");
            }

            BpmnDocument? document;
            try
            {
                var serializerOptions = BpmnJsonFormat.Options(options.MaxDepth, indent: false);
                var typeInfo = (JsonTypeInfo<BpmnDocument>)serializerOptions.GetTypeInfo(typeof(BpmnDocument));
                var node = JsonNode.Parse(body.GetRawText(), documentOptions: new JsonDocumentOptions
                {
                    MaxDepth = options.MaxDepth,
                    AllowDuplicateProperties = false,
                });
                EnsureNoNullEntries(node, string.Empty);
                BpmnJsonFormat.RestoreOmitted(node, typeInfo);
                document = node.Deserialize(typeInfo);
            }
            catch (JsonException exception)
            {
                throw new BpmnJsonException($"The document is not valid BPMN JSON: {exception.Message}", exception);
            }

            if (document is null) throw new BpmnJsonException("The document is empty.");

            diagnostics.AddRange(BpmnColorDiagnostics.Find(document));
            return new BpmnImportResult(document, diagnostics);
        }
    }

    private static void CheckVersion(JsonElement root, List<BpmnImportDiagnostic> diagnostics)
    {
        if (!root.TryGetProperty(BpmnJsonFormat.VersionProperty, out var element)
            || element.ValueKind != JsonValueKind.String)
        {
            throw new BpmnJsonException(
                $"The document has no \"{BpmnJsonFormat.VersionProperty}\" string; expected \"{BpmnJsonFormat.Version}\".");
        }

        var text = element.GetString() ?? string.Empty;
        var parts = text.Split('.');
        if (parts.Length is < 1 or > 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || (parts.Length == 2 && !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            throw new BpmnJsonException($"The document version \"{text}\" is not of the form major.minor.");
        }

        if (major != BpmnJsonFormat.MajorVersion)
        {
            throw new BpmnJsonException(
                $"The document is BPMN JSON version {text}; this library reads version {BpmnJsonFormat.MajorVersion}.x only.");
        }

        var minor = parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        if (minor > BpmnJsonFormat.MinorVersion)
        {
            diagnostics.Add(new BpmnImportDiagnostic(
                BpmnImportSeverity.Info,
                $"The document was written as BPMN JSON version {text}, newer than {BpmnJsonFormat.Version}; properties this library does not know were ignored."));
        }
    }

    /// <summary>
    /// Refuses <see langword="null"/> entries in any array of the document.
    /// </summary>
    /// <remarks>
    /// The serializer enforces nullability on properties but not on collection elements, and a
    /// <c>null</c> entry anywhere in the model (flow nodes, nested catalog values, extension
    /// children, ...) would otherwise surface much later as a crash in validation, rendering or
    /// export. No model collection admits <c>null</c>, so the check is structural.
    /// </remarks>
    private static void EnsureNoNullEntries(JsonNode? node, string path)
    {
        switch (node)
        {
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    var item = array[index];
                    var itemPath = string.Create(CultureInfo.InvariantCulture, $"{path}[{index}]");
                    if (item is null)
                    {
                        throw new BpmnJsonException($"The document is not valid BPMN JSON: {itemPath} is null.");
                    }

                    EnsureNoNullEntries(item, itemPath);
                }

                break;
            case JsonObject obj:
                foreach (var (name, value) in obj)
                {
                    EnsureNoNullEntries(value, path.Length == 0 ? name : $"{path}.{name}");
                }

                break;
        }
    }

    private static BpmnJsonException TooLarge(BpmnJsonReadOptions options) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"The document is larger than the {options.MaxInputBytes}-byte limit for BPMN JSON input."));
}
