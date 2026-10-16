namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>
/// Which of the two export modes the specification requires (section 10.6).
/// </summary>
/// <remarks>
/// The two are not quality levels — neither is "better". They answer different questions, and
/// section 10.6 requires both to exist and each save command to document which it uses.
/// </remarks>
public enum BpmnWriteMode
{
    /// <summary>
    /// Regenerate the whole document from the semantic model, in schema order.
    /// </summary>
    /// <remarks>
    /// Deterministic: the same model always produces byte-identical output, whatever file it was
    /// read from or whether it was read from one at all. That is what makes it usable as a test
    /// oracle and as the format for a new document. It also means anything the model does not
    /// hold is not written, so exporting a vendor file this way normalises it — every such loss is
    /// reported as a <see cref="BpmnExportSeverity.Normalized"/> diagnostic rather than left silent.
    /// </remarks>
    Canonical,

    /// <summary>
    /// Edit the document that was read, touching only what changed.
    /// </summary>
    /// <remarks>
    /// Comments, processing instructions, document order, unknown attributes, foreign root
    /// elements and out-of-scope Choreography content all survive because they are never rebuilt —
    /// they are simply left where they were. This is the mode for saving a file the editor did not
    /// create. It requires the document to still carry the source it was read from; without one
    /// there is nothing to preserve, and the writer falls back to
    /// <see cref="Canonical"/> and says so.
    /// </remarks>
    Fidelity,
}

/// <summary>Settings for one export.</summary>
public sealed record BpmnWriteOptions
{
    /// <summary>Canonical output with indentation: the default for a new document and for tests.</summary>
    public static readonly BpmnWriteOptions Canonical = new();

    /// <summary>Fidelity output: the default for saving a file that was imported.</summary>
    public static readonly BpmnWriteOptions Fidelity = new() { Mode = BpmnWriteMode.Fidelity };

    /// <summary>Which mode to write in.</summary>
    public BpmnWriteMode Mode { get; init; } = BpmnWriteMode.Canonical;

    /// <summary>Whether to indent the output. Ignored in fidelity mode, which preserves the original whitespace.</summary>
    public bool Indent { get; init; } = true;

    /// <summary>
    /// Whether canonical mode may write XML after reporting that source content will be dropped.
    /// </summary>
    /// <remarks>
    /// Defaults to <see langword="false"/>. Set this only when the caller has reviewed the
    /// <see cref="BpmnExportResult.Diagnostics"/> and explicitly accepts the reported losses.
    /// This does not affect fidelity mode or newly-created documents without retained source.
    /// </remarks>
    public bool AllowLossyCanonicalExport { get; init; }

    /// <summary>The <c>exporter</c> attribute to stamp on the root, or <see langword="null"/> to keep the document's own.</summary>
    public string? Exporter { get; init; }

    /// <summary>The <c>exporterVersion</c> attribute to stamp on the root.</summary>
    public string? ExporterVersion { get; init; }

    /// <summary>
    /// Whether to write the redundant <c>incoming</c>/<c>outgoing</c> children on flow nodes.
    /// </summary>
    /// <remarks>
    /// They restate what <c>sequenceFlow</c> already says with <c>sourceRef</c>/<c>targetRef</c>,
    /// and BPMN makes them optional. Off by default in canonical mode because duplicated references
    /// are two places to disagree; fidelity mode preserves whatever the document had either way.
    /// </remarks>
    public bool WriteFlowNodeReferences { get; init; }
}
