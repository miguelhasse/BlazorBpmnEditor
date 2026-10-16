using Hasseware.AspNetCore.Components.Bpmn.Configuration;

namespace Hasseware.AspNetCore.Components.Bpmn.Validation;

/// <summary>How much a validation finding matters (specification section 5.2).</summary>
public enum BpmnSeverity
{
    /// <summary>Advisory. The document renders and exports; something is probably not intended.</summary>
    Warning,

    /// <summary>The document is wrong. Section 5.2: an error blocks save and export.</summary>
    Error,

    /// <summary>Neither: something the user should know about a document that is otherwise fine.</summary>
    Info,
}

/// <summary>
/// One validation finding.
/// </summary>
/// <remarks>
/// The rule id is the contract. Section 5.3 exists so a finding can be referred to from a tooltip,
/// a test and a document without depending on its wording — so the message here may be reworded
/// freely and the id may not.
/// </remarks>
/// <param name="RuleId">The stable code, for example <c>D001</c> or <c>SF-006</c>.</param>
/// <param name="Severity">Whether this blocks export.</param>
/// <param name="Message">Invariant English text for logs, exports and runtime parity.</param>
/// <param name="ElementId">
/// The element the finding is about. Null only for findings about the document as a whole.
/// </param>
/// <param name="RelatedId">
/// A second element the finding also implicates — the other end of a flow, the duplicate id, the
/// Lane that disagrees. Surfaced so a panel can offer to go to either.
/// </param>
public sealed record BpmnDiagnostic(
    string RuleId,
    BpmnSeverity Severity,
    string Message,
    string? ElementId = null,
    string? RelatedId = null)
{
    /// <summary>Arguments for the localized message or catalog detail format.</summary>
    public IReadOnlyList<object?>? MessageArguments { get; init; }

    /// <summary>Optional resource key for native wording that differs from the catalog message.</summary>
    public string? MessageResourceKey { get; init; }

    /// <summary>Optional resource key for a localized detail appended to the catalog message.</summary>
    public string? DetailResourceKey { get; init; }

    /// <summary>Invariant detail appended to the catalog message; retained as a translation fallback.</summary>
    public string? MessageDetail { get; init; }

    /// <summary>Host profile name appended to the rendered message.</summary>
    public string? ProfileName { get; init; }

    /// <summary>Compares findings independently of localization presentation metadata.</summary>
    public bool Equals(BpmnDiagnostic? other) => other is not null
        && RuleId == other.RuleId && Severity == other.Severity && Message == other.Message
        && ElementId == other.ElementId && RelatedId == other.RelatedId;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(RuleId, Severity, Message, ElementId, RelatedId);

    internal static BpmnDiagnostic FromDetail(
        BpmnRuleInfo rule,
        string? elementId = null,
        string? relatedId = null,
        BpmnDiagnosticText? detail = null) =>
        From(rule, elementId, relatedId, detail?.Message, detail?.Arguments, detail?.ResourceKey);

    /// <summary>
    /// Builds a finding from the shared rule catalog, so the wording lives in one place.
    /// </summary>
    /// <remarks>
    /// <c>rules/bpmn-rules.json</c> is the single source for a rule's severity and message, shared
    /// with the TypeScript side. A validator that carried its own copy of either would be a second
    /// definition of the same rule, and the one that drifts.
    /// </remarks>
    /// <param name="rule">The catalog entry.</param>
    /// <param name="elementId">The element the finding is about.</param>
    /// <param name="relatedId">A second implicated element, when there is one.</param>
    /// <param name="detail">
    /// Extra wording appended to the catalog message — the specific name or count that makes a
    /// generic rule concrete.
    /// </param>
    /// <param name="messageArguments">Arguments for the localized detail format.</param>
    /// <param name="detailResourceKey">Resource key for the detail; null preserves the supplied detail verbatim.</param>
    /// <returns>The diagnostic.</returns>
    public static BpmnDiagnostic From(
        BpmnRuleInfo rule,
        string? elementId = null,
        string? relatedId = null,
        string? detail = null,
        IReadOnlyList<object?>? messageArguments = null,
        string? detailResourceKey = null)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new BpmnDiagnostic(
            rule.Id,
            SeverityOf(rule.Severity),
            detail is null ? rule.Message : $"{rule.Message} {detail}",
            elementId,
            relatedId)
        {
            MessageDetail = detail,
            MessageArguments = messageArguments,
            DetailResourceKey = detailResourceKey,
        };
    }

    private static BpmnSeverity SeverityOf(string severity) => severity switch
    {
        "error" => BpmnSeverity.Error,
        "info" => BpmnSeverity.Info,
        _ => BpmnSeverity.Warning,
    };
}

/// <summary>
/// Everything one validation run found.
/// </summary>
/// <remarks>
/// Section 5.5 requires the pipeline to "retain all issues rather than stopping after the first
/// failure", and to support three outcomes: renderable and exportable, renderable with warnings, or
/// quarantined. Those are the three states <see cref="IsExportable"/> and the counts describe —
/// a report is never a boolean, because a document with warnings is a document the user can still
/// work in.
/// </remarks>
/// <param name="Diagnostics">Every finding, in the order the layers produced them.</param>
public sealed record BpmnValidationReport(IReadOnlyList<BpmnDiagnostic> Diagnostics)
{
    /// <summary>A report with nothing in it.</summary>
    public static readonly BpmnValidationReport Empty = new([]);

    /// <summary>How many findings block export.</summary>
    public int ErrorCount => Diagnostics.Count(diagnostic => diagnostic.Severity == BpmnSeverity.Error);

    /// <summary>How many findings are advisory.</summary>
    public int WarningCount => Diagnostics.Count(diagnostic => diagnostic.Severity == BpmnSeverity.Warning);

    /// <summary>Whether the document may be saved and exported (section 5.2).</summary>
    public bool IsExportable => ErrorCount == 0;

    /// <summary>Every finding about one element, in report order.</summary>
    /// <param name="elementId">The element to look up.</param>
    /// <returns>Its findings, including those where it is the related element.</returns>
    public IReadOnlyList<BpmnDiagnostic> For(string elementId) =>
    [
        .. Diagnostics.Where(diagnostic =>
            diagnostic.ElementId == elementId || diagnostic.RelatedId == elementId),
    ];
}
