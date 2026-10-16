using System.Xml.Schema;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// What a file would bring in, offered for a decision before anything changes.
/// </summary>
/// <remarks>
/// <para>
/// Importing replaces the whole document, so it is the one operation that cannot be undone into
/// anything meaningful — the edit history belongs to the file being replaced. The milestone's exit
/// criterion is therefore about what happens when the answer is no: an import preview must be
/// rejectable "without corrupting the current graph".
/// </para>
/// <para>
/// That is structural here rather than careful. A preview is a parsed document held to one side;
/// nothing touches the editor's own until the offer is accepted, and a file that does not parse
/// produces a preview that carries the error and no document at all.
/// </para>
/// </remarks>
/// <param name="FileName">What the file was called, for the offer.</param>
/// <param name="Document">The parsed document, or <see langword="null"/> when it could not be read.</param>
/// <param name="Error">Why it could not be read.</param>
/// <param name="Diagnostics">What the reader could not keep.</param>
/// <param name="Validation">What the validator makes of it, when it parsed.</param>
public sealed record BpmnImportPreview(
    string FileName,
    BpmnDocument? Document,
    string? Error,
    IReadOnlyList<BpmnImportDiagnostic> Diagnostics,
    BpmnValidationReport Validation)
{
    /// <summary>Whether there is anything to accept.</summary>
    public bool CanAccept => Document is not null;

    /// <summary>How many flow nodes the file draws.</summary>
    public int FlowNodeCount => Document?.FlowNodes.Count ?? 0;

    /// <summary>How many connectors of every kind it declares.</summary>
    public int ConnectorCount => Document is null
        ? 0
        : Document.SequenceFlows.Count
            + Document.MessageFlows.Count
            + Document.Associations.Count
            + Document.DataAssociations.Count;

    /// <summary>How many diagrams it draws.</summary>
    public int DiagramCount => Document?.Diagrams.Count ?? 0;

    /// <summary>Whether schema validation ran against a preloaded schema set.</summary>
    public BpmnSchemaValidationStatus SchemaValidationStatus { get; init; } = BpmnSchemaValidationStatus.NotRun;

    /// <summary>
    /// Content the reader could not represent, which an export would therefore not put back.
    /// </summary>
    /// <remarks>
    /// The number a user actually needs before saying yes. Section 10.6 asks for a round trip to be
    /// lossless, and this is where a file is honest about the cases it will not be.
    /// </remarks>
    public int DroppedCount => Diagnostics.Count(diagnostic => diagnostic.Severity == BpmnImportSeverity.Dropped);

    /// <summary>
    /// Content the reader kept, but not as the file stated it.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="DroppedCount"/> because it is a different answer to a different
    /// question: nothing was lost, but what opens is not quite what was written. A Lane set given
    /// the Pool it lacked and a connector whose waypoints cannot describe a route are both this,
    /// and both are things a reader would want to know before saying yes — silently changing a
    /// file's structure is exactly what sections 10.6 and 6.1 exist to prevent.
    /// </remarks>
    public int ChangedCount => Diagnostics.Count(diagnostic => diagnostic.Severity == BpmnImportSeverity.Degraded);

    /// <summary>Schema violations found when the reader was given a preloaded schema set.</summary>
    public IReadOnlyList<BpmnImportDiagnostic> SchemaFindings =>
        [.. Diagnostics.Where(diagnostic => diagnostic.Severity == BpmnImportSeverity.SchemaError)];

    /// <summary>Reads a file and describes what accepting it would bring in.</summary>
    /// <param name="fileName">The file's name, for display.</param>
    /// <param name="xml">Its contents.</param>
    /// <returns>A preview, which carries an error rather than throwing when the file will not read.</returns>
    public static BpmnImportPreview Of(string fileName, string xml)
        => Of(fileName, xml, schemas: null);

    /// <summary>Reads a file preview and optionally validates it against a preloaded schema set.</summary>
    /// <remarks>
    /// Accepts BPMN XML and BPMN JSON alike; <see cref="BpmnFileFormats.Detect"/> decides which from
    /// the content, falling back to the file extension. Schemas apply to XML only.
    /// </remarks>
    /// <param name="fileName">The file's name, for display.</param>
    /// <param name="xml">Its contents, as BPMN XML or BPMN JSON.</param>
    /// <param name="schemas">A preloaded schema set; no schema URLs are resolved from document content.</param>
    /// <returns>A preview that carries parse and schema findings.</returns>
    public static BpmnImportPreview Of(string fileName, string xml, XmlSchemaSet? schemas)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        try
        {
            var result = BpmnFileFormats.Detect(fileName, xml) == BpmnFileFormat.Json
                ? BpmnJsonReader.Read(xml)
                : BpmnXmlReader.Read(xml, schemas);

            return new BpmnImportPreview(
                fileName,
                result.Document,
                null,
                result.Diagnostics,
                BpmnValidator.Validate(result.Document))
            {
                SchemaValidationStatus = result.SchemaValidationStatus,
            };
        }
        catch (BpmnImportException failure)
        {
            /*
             * Caught rather than propagated. A file that will not parse is an ordinary answer to
             * "what is in this file" — the user picked the wrong one, or it is not BPMN — and an
             * exception crossing into the UI would take the editor down over a bad drop.
             */
            return new BpmnImportPreview(fileName, null, failure.Message, [], BpmnValidationReport.Empty);
        }
    }
}
