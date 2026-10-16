namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>The interchange formats this library reads and writes.</summary>
public enum BpmnFileFormat
{
    /// <summary>BPMN 2.0 XML, read by <see cref="BpmnXmlReader"/> and written by <see cref="BpmnXmlWriter"/>.</summary>
    Xml,

    /// <summary>BPMN JSON (<see cref="BpmnJsonFormat"/>), read by <see cref="BpmnJsonReader"/> and written by <see cref="BpmnJsonWriter"/>.</summary>
    Json,
}

/// <summary>
/// Format detection and dispatch for callers that accept either interchange format.
/// </summary>
public static class BpmnFileFormats
{
    /// <summary>The conventional BPMN XML file extension.</summary>
    public const string XmlFileExtension = ".bpmn";

    /// <summary>The media type a BPMN XML download should declare.</summary>
    public const string XmlMediaType = "application/xml";

    /// <summary>
    /// The file types a picker should offer, as an HTML <c>accept</c> attribute value.
    /// </summary>
    public const string Accept = ".bpmn,.xml,.json,application/xml,text/xml,application/json";

    /// <summary>
    /// Decides which format some content is in.
    /// </summary>
    /// <remarks>
    /// The content decides when it can: a first non-whitespace character of <c>{</c> is JSON and
    /// <c>&lt;</c> is XML, whatever the file is called. Otherwise the extension decides —
    /// <c>.json</c> is JSON — and anything else is treated as XML, which is the format whose
    /// reader produces the more useful error for a file that is neither.
    /// </remarks>
    /// <param name="fileName">The file name, when known.</param>
    /// <param name="content">The file content.</param>
    /// <returns>The detected format.</returns>
    public static BpmnFileFormat Detect(string? fileName, string? content)
    {
        foreach (var character in content ?? string.Empty)
        {
            if (character == '\uFEFF' || char.IsWhiteSpace(character)) continue;
            if (character == '{') return BpmnFileFormat.Json;
            if (character == '<') return BpmnFileFormat.Xml;
            break;
        }

        return fileName is not null
            && fileName.EndsWith(BpmnJsonFormat.FileExtension, StringComparison.OrdinalIgnoreCase)
            ? BpmnFileFormat.Json
            : BpmnFileFormat.Xml;
    }

    /// <summary>Reads content in whichever format it is in.</summary>
    /// <param name="content">The file content.</param>
    /// <param name="fileName">The file name, when known, used only when the content is ambiguous.</param>
    /// <returns>The document and every finding produced while reading it.</returns>
    /// <exception cref="BpmnImportException">The input cannot be read as a BPMN document.</exception>
    public static BpmnImportResult Read(string content, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        return Detect(fileName, content) == BpmnFileFormat.Json
            ? BpmnJsonReader.Read(content)
            : BpmnXmlReader.Read(content);
    }

    /// <summary>The conventional file extension for a format.</summary>
    /// <param name="format">The format.</param>
    /// <returns><c>.bpmn</c> or <c>.json</c>.</returns>
    public static string FileExtension(BpmnFileFormat format) =>
        format == BpmnFileFormat.Json ? BpmnJsonFormat.FileExtension : XmlFileExtension;

    /// <summary>The media type for a format.</summary>
    /// <param name="format">The format.</param>
    /// <returns><c>application/xml</c> or <c>application/json</c>.</returns>
    public static string MediaType(BpmnFileFormat format) =>
        format == BpmnFileFormat.Json ? BpmnJsonFormat.MediaType : XmlMediaType;
}
