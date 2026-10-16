using System.Globalization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The logic of the validation surface (specification section 5.2); a UI kit supplies the markup.
/// </summary>
/// <remarks>
/// A finding you cannot get to is a finding you cannot fix, so every row selects the element it is
/// about. The panel reports and decides nothing: whether to validate, and when, are the editor's.
/// </remarks>
public abstract class BpmnValidationPanelBase : BpmnEditorComponentBase
{
    /// <inheritdoc />
    protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) =>
        !ReferenceEquals(previous.Validation, current.Validation);

    /// <summary>The panel's accessible name; a localized default when not set.</summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <summary>The accessible name in force.</summary>
    protected string EffectiveAriaLabel => string.IsNullOrWhiteSpace(AriaLabel) ? Text("ValidationPanel.AriaLabel") : AriaLabel;

    /// <summary>What validation found.</summary>
    protected BpmnValidationReport Report => State.Validation;

    /// <summary>
    /// Findings with the ones that block export first.
    /// </summary>
    /// <remarks>
    /// Errors, then warnings, then information, and within each the validator's own order, which is
    /// the section 5.5 layer order and so groups findings about the same part of the document.
    /// </remarks>
    protected IEnumerable<BpmnDiagnostic> Ordered
    {
        get { EnsureRows(); return _ordered; }
    }

    private BpmnValidationReport? _rowReport;
    private CultureInfo? _rowCulture;
    private IReadOnlyList<BpmnDiagnostic> _ordered = [];
    private readonly Dictionary<BpmnDiagnostic, string> _messages = new(ReferenceEqualityComparer.Instance);

    private void EnsureRows()
    {
        var culture = CultureInfo.CurrentUICulture;
        if (ReferenceEquals(_rowReport, Report) && ReferenceEquals(_rowCulture, culture)) return;
        _rowReport = Report;
        _rowCulture = culture;
        _ordered = Report.Diagnostics.OrderBy(diagnostic => diagnostic.Severity switch
        {
            BpmnSeverity.Error => 0,
            BpmnSeverity.Warning => 1,
            _ => 2,
        }).ToArray();
        _messages.Clear();
        foreach (var diagnostic in _ordered) _messages[diagnostic] = DiagnosticLocalizer.Localize(diagnostic);
    }

    /// <summary>The localized text of a finding.</summary>
    /// <param name="diagnostic">The finding.</param>
    /// <returns>The message to show.</returns>
    protected string MessageOf(BpmnDiagnostic diagnostic)
    {
        EnsureRows();
        return _messages.TryGetValue(diagnostic, out var message) ? message : DiagnosticLocalizer.Localize(diagnostic);
    }

    /// <summary>The localized summary of the counts, such as <c>2 errors</c>.</summary>
    /// <param name="count">How many findings.</param>
    /// <param name="severity">Which severity they have.</param>
    /// <returns>The count and its localized noun.</returns>
    protected string CountText(int count, BpmnSeverity severity)
    {
        var key = severity == BpmnSeverity.Error ? "ValidationPanel.Error" : "ValidationPanel.Warning";
        return $"{count} {Text(count == 1 ? key : $"{key}s")}";
    }

    /// <summary>The localized text saying whether the document may be exported.</summary>
    protected string ExportStateText => Text(Report.IsExportable ? "ValidationPanel.Exportable" : "ValidationPanel.NotExportable");

    /// <summary>The localized text for a report with no findings.</summary>
    protected string CleanText => Text("ValidationPanel.Clean");

    /// <summary>Selects the element a finding is about.</summary>
    /// <param name="diagnostic">The finding.</param>
    /// <returns>A task that completes once the element is selected.</returns>
    protected Task SelectAsync(BpmnDiagnostic diagnostic) => Session.GoToAsync(diagnostic);
}
