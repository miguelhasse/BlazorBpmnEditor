using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>How much of an element survived a read.</summary>
public enum BpmnImportSeverity
{
    /// <summary>Nothing was lost; the finding is informational.</summary>
    Info,

    /// <summary>The element was read, but some of its content could not be represented.</summary>
    Degraded,

    /// <summary>The element could not be read at all.</summary>
    Dropped,

    /// <summary>The XML parsed, but it failed a supplied BPMN XML Schema validation set.</summary>
    SchemaError,
}

/// <summary>Whether XML Schema validation ran against the supplied schema set.</summary>
public enum BpmnSchemaValidationStatus
{
    /// <summary>No non-empty schema set was provided; XML Schema validation did not run.</summary>
    NotRun,

    /// <summary>The document passed the schema set the caller supplied.</summary>
    Passed,

    /// <summary>The document produced one or more errors against the supplied schema set.</summary>
    Failed,
}

/// <summary>One element-scoped finding from reading a document.</summary>
/// <param name="Severity">How much was lost.</param>
/// <param name="Message">What the document said and what the reader did about it.</param>
/// <param name="ElementId">The element the finding is about, when it is about one.</param>
/// <param name="ContainerId">The Process or Collaboration it occurred in, when scoped to one.</param>
public sealed record BpmnImportDiagnostic(
    BpmnImportSeverity Severity,
    string Message,
    string? ElementId = null,
    string? ContainerId = null);

/// <summary>The result of reading a BPMN document.</summary>
/// <param name="Document">The document as this library's semantic model.</param>
/// <param name="Diagnostics">
/// Every finding, in the order the reader produced them. Surfaced rather than thrown: a document
/// that is partly unreadable is still worth showing, and the specification's import pipeline
/// (section 5.5) explicitly wants all issues retained instead of stopping at the first.
/// </param>
/// <param name="SchemaValidationStatus">Whether validation ran against the supplied schema set.</param>
public sealed record BpmnImportResult(
    BpmnDocument Document,
    IReadOnlyList<BpmnImportDiagnostic> Diagnostics,
    BpmnSchemaValidationStatus SchemaValidationStatus = BpmnSchemaValidationStatus.NotRun)
{
    /// <summary>True when nothing was dropped, so the document round-trips without loss.</summary>
    public bool IsLossless => !Diagnostics.Any(d => d.Severity == BpmnImportSeverity.Dropped);
}
