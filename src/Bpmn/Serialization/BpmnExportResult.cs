namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>How faithfully one item survived an export.</summary>
public enum BpmnExportSeverity
{
    /// <summary>Nothing was lost; the finding records what the writer did.</summary>
    Info,

    /// <summary>The item was written, but not exactly as the source had it.</summary>
    Normalized,

    /// <summary>The item could not be written and is absent from the output.</summary>
    Dropped,
}

/// <summary>One finding from writing a document.</summary>
/// <param name="Severity">How faithfully the item survived.</param>
/// <param name="Message">What the writer did and why.</param>
/// <param name="ElementId">The element the finding is about, when it is about one.</param>
public sealed record BpmnExportDiagnostic(
    BpmnExportSeverity Severity,
    string Message,
    string? ElementId = null);

/// <summary>
/// The result of writing a document: the XML plus the diagnostics manifest section 10.6 requires.
/// </summary>
/// <param name="Xml">The serialized document.</param>
/// <param name="Mode">
/// The mode actually used, which is not always the mode asked for: fidelity falls back to
/// canonical when the document carries no source to preserve.
/// </param>
/// <param name="Diagnostics">Every finding, in the order the writer produced them.</param>
public sealed record BpmnExportResult(
    string Xml,
    BpmnWriteMode Mode,
    IReadOnlyList<BpmnExportDiagnostic> Diagnostics)
{
    /// <summary>True when nothing was dropped or normalised away.</summary>
    public bool IsLossless => !Diagnostics.Any(d => d.Severity != BpmnExportSeverity.Info);
}

/// <summary>Canonical export was refused because it would omit source content.</summary>
public sealed class BpmnLossyExportException(IReadOnlyList<BpmnExportDiagnostic> diagnostics)
    : InvalidOperationException(
        "Canonical export would drop source content. Review the diagnostics and explicitly set AllowLossyCanonicalExport to continue.")
{
    /// <summary>The loss findings that caused export to be refused.</summary>
    public IReadOnlyList<BpmnExportDiagnostic> Diagnostics { get; } = diagnostics;
}
