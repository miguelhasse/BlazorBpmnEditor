using System.Text;

using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>A document read from a store and parsed.</summary>
/// <param name="Document">The parsed document.</param>
/// <param name="Diagnostics">What the reader normalized or could not keep, exactly as for a file import.</param>
/// <param name="Revision">The revision the content was read at.</param>
/// <param name="Modified">When the content was last written.</param>
/// <param name="Format">The format the content was in, which is not always the one its name implies (a <c>.json</c> file may hold XML); null when not known.</param>
public sealed record BpmnLoadedDocument(
    BpmnDocument Document,
    IReadOnlyList<BpmnImportDiagnostic> Diagnostics,
    string Revision,
    DateTimeOffset Modified,
    BpmnFileFormat? Format = null);

/// <summary>Reads and writes parsed documents through any <see cref="IBpmnDocumentStore"/>.</summary>
/// <remarks>
/// Parsing and writing live here rather than in stores, so a document read from a blob container is
/// read by exactly the same code, with the same diagnostics, as one opened from a file.
/// </remarks>
public static class BpmnDocumentStoreExtensions
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Reads and parses a document.</summary>
    /// <param name="store">The store.</param>
    /// <param name="key">The document.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The parsed document and its revision, or null when there is no such document.</returns>
    /// <exception cref="BpmnImportException">The stored content is not a readable BPMN document.</exception>
    /// <exception cref="DecoderFallbackException">The stored content is not valid UTF-8.</exception>
    public static async ValueTask<BpmnLoadedDocument?> LoadDocumentAsync(
        this IBpmnDocumentStore store,
        BpmnDocumentKey key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);

        var stored = await store.OpenAsync(key, cancellationToken).ConfigureAwait(false);
        if (stored is null) return null;

        var text = Decode(stored.Content.Bytes.Span);
        var import = BpmnFileFormats.Read(text, key.Path.Name);
        return new BpmnLoadedDocument(import.Document, import.Diagnostics, stored.Revision, stored.Modified, BpmnFileFormats.Detect(key.Path.Name, text));
    }

    /// <summary>Writes a document in the format its name implies, if the stored revision still matches.</summary>
    /// <remarks>
    /// A <c>.json</c> name is written in the JSON interchange format and anything else as BPMN XML,
    /// using fidelity mode so content read from a file is written back as faithfully as possible.
    /// </remarks>
    /// <param name="store">The store.</param>
    /// <param name="key">The document.</param>
    /// <param name="document">The document to write.</param>
    /// <param name="ifMatchRevision">The revision the caller holds, or null to create a new document.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The store's result; see <see cref="IBpmnDocumentStore.SaveAsync"/>.</returns>
    /// <exception cref="BpmnExportException">The document cannot be written in that format.</exception>
    public static ValueTask<BpmnStoreResult> SaveDocumentAsync(
        this IBpmnDocumentStore store,
        BpmnDocumentKey key,
        BpmnDocument document,
        string? ifMatchRevision,
        CancellationToken cancellationToken = default) =>
        SaveDocumentAsync(store, key, document, ifMatchRevision, format: null, cancellationToken);

    /// <summary>Writes a document in a chosen format, if the stored revision still matches.</summary>
    /// <param name="store">The store.</param>
    /// <param name="key">The document.</param>
    /// <param name="document">The document to write.</param>
    /// <param name="ifMatchRevision">The revision the caller holds, or null to create a new document.</param>
    /// <param name="format">The format to write, or null for the one the name implies. Pass <see cref="BpmnLoadedDocument.Format"/> so saving does not change what the file holds.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The store's result; see <see cref="IBpmnDocumentStore.SaveAsync"/>.</returns>
    /// <exception cref="BpmnExportException">The document cannot be written in that format.</exception>
    public static ValueTask<BpmnStoreResult> SaveDocumentAsync(
        this IBpmnDocumentStore store,
        BpmnDocumentKey key,
        BpmnDocument document,
        string? ifMatchRevision,
        BpmnFileFormat? format,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(document);

        return store.SaveAsync(key, Serialize(key, document, format), ifMatchRevision, cancellationToken);
    }

    /// <summary>Serializes a document in the format its key's name implies.</summary>
    /// <param name="key">The document's key, whose name decides the format.</param>
    /// <param name="document">The document.</param>
    /// <param name="format">The format to write, or null for the one the name implies.</param>
    /// <returns>The UTF-8 content.</returns>
    /// <exception cref="BpmnExportException">The document cannot be written: a name that is not valid XML, or a tree nested too deeply.</exception>
    public static BpmnDocumentContent Serialize(BpmnDocumentKey key, BpmnDocument document, BpmnFileFormat? format = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var chosen = format ?? BpmnFileFormats.Detect(key.Path.Name, content: null);
        string text;
        try
        {
            text = chosen == BpmnFileFormat.Json
                ? BpmnJsonWriter.Write(document)
                : BpmnXmlWriter.Write(document, BpmnWriteOptions.Fidelity).Xml;
        }
        catch (Exception exception) when (exception is BpmnJsonException or System.Xml.XmlException or ArgumentException or InvalidOperationException)
        {
            throw new BpmnExportException("The document cannot be written: " + exception.Message, exception);
        }

        return new BpmnDocumentContent(Utf8.GetBytes(text), chosen);
    }

    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        var preamble = Encoding.UTF8.Preamble;
        return Utf8.GetString(bytes.StartsWith(preamble) ? bytes[preamble.Length..] : bytes);
    }
}
