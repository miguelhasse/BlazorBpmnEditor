using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>
/// Writes this library's semantic model as BPMN JSON (see <see cref="BpmnJsonFormat"/>).
/// </summary>
/// <remarks>
/// <para>
/// The JSON counterpart of <see cref="BpmnXmlWriter"/> in canonical mode: the file is generated
/// from the model, so it carries everything the model carries — semantics, DI geometry, labels,
/// documentation, foreign attributes and preserved vendor extensions — and reading it back with
/// <see cref="BpmnJsonReader"/> and writing that as XML produces the same canonical XML as the
/// original model would.
/// </para>
/// <para>
/// There is no fidelity mode. What fidelity mode preserves beyond the model — comments, the
/// source's element order and whitespace, its XML declaration — is a property of an XML file, and
/// <see cref="BpmnDocument.Source"/> is deliberately not part of the JSON.
/// </para>
/// </remarks>
public static class BpmnJsonWriter
{
    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>Writes a document as JSON text.</summary>
    /// <param name="document">The document to write.</param>
    /// <param name="options">Formatting and limits; <see cref="BpmnJsonWriteOptions.Default"/> when omitted.</param>
    /// <returns>The JSON, ending in a newline.</returns>
    /// <exception cref="BpmnJsonException">The document cannot be represented within the configured limits.</exception>
    public static string Write(BpmnDocument document, BpmnJsonWriteOptions? options = null)
    {
        var (envelope, serializerOptions) = Prepare(document, options);

        try
        {
            return JsonSerializer.Serialize(envelope, TypeInfo(serializerOptions)) + "\n";
        }
        catch (JsonException exception)
        {
            throw Failure(exception);
        }
    }

    /// <summary>Writes a document as UTF-8 JSON to a stream.</summary>
    /// <param name="document">The document to write.</param>
    /// <param name="stream">The stream to write to; left open.</param>
    /// <param name="options">Formatting and limits; <see cref="BpmnJsonWriteOptions.Default"/> when omitted.</param>
    /// <exception cref="BpmnJsonException">The document cannot be represented within the configured limits.</exception>
    public static void Write(BpmnDocument document, Stream stream, BpmnJsonWriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var bytes = Utf8.GetBytes(Write(document, options));
        stream.Write(bytes);
    }

    /// <summary>Writes a document as UTF-8 JSON to a stream asynchronously.</summary>
    /// <param name="document">The document to write.</param>
    /// <param name="stream">The stream to write to; left open.</param>
    /// <param name="options">Formatting and limits; <see cref="BpmnJsonWriteOptions.Default"/> when omitted.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the document has been written.</returns>
    /// <exception cref="BpmnJsonException">The document cannot be represented within the configured limits.</exception>
    public static async Task WriteAsync(
        BpmnDocument document,
        Stream stream,
        BpmnJsonWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var (envelope, serializerOptions) = Prepare(document, options);

        try
        {
            await JsonSerializer.SerializeAsync(stream, envelope, TypeInfo(serializerOptions), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw Failure(exception);
        }

        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    private static (BpmnJsonEnvelope Envelope, JsonSerializerOptions Options) Prepare(
        BpmnDocument document,
        BpmnJsonWriteOptions? options)
    {
        ArgumentNullException.ThrowIfNull(document);

        options ??= BpmnJsonWriteOptions.Default;

        return (
            new BpmnJsonEnvelope(BpmnJsonFormat.Name, BpmnJsonFormat.Version, document),
            BpmnJsonFormat.Options(options.MaxDepth, options.Indent));
    }

    private static JsonTypeInfo<BpmnJsonEnvelope> TypeInfo(
        JsonSerializerOptions options) =>
        (JsonTypeInfo<BpmnJsonEnvelope>)options.GetTypeInfo(typeof(BpmnJsonEnvelope));

    private static BpmnJsonException Failure(JsonException exception) =>
        new($"The document could not be written as BPMN JSON: {exception.Message}", exception);
}
