using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// Document validation (specification sections 5.2 and 5.5).
/// </summary>
/// <remarks>
/// The editor owns when validation runs, because it owns the document. The validator is pure and
/// the panel renders what it is given; what is here is the one decision neither of them can make —
/// that a report describes the document currently open, and stops describing it the moment an edit
/// lands.
/// </remarks>
public sealed partial class BpmnEditorSession
{
    private BpmnDocument? _validated;
    private BpmnValidationReport _report = BpmnValidationReport.Empty;

    /// <summary>
    /// Whether the editor validates after every change.
    /// </summary>
    /// <remarks>
    /// On by default. Validation is linear in the document and runs in well under a frame even on
    /// the 602-shape fixture, so the cost of being always-right is lower than the cost of a stale
    /// panel. A host that validates on its own schedule turns this off and calls
    /// <see cref="ValidateAsync"/>.
    /// </remarks>
    internal bool ValidateOnChange { get; set; } = true;

    /// <summary>Raised whenever a new report is produced.</summary>
    internal EventCallback<BpmnValidationReport> OnValidated { get; set; }

    /// <summary>
    /// The current findings.
    /// </summary>
    /// <remarks>
    /// Computed on demand and cached against the document instance. The model is immutable, so a
    /// document that is the same reference cannot have changed — which makes the cache exact rather
    /// than a guess, and means a panel may read this as often as it likes.
    /// </remarks>
    public BpmnValidationReport Validation
    {
        get
        {
            if (!ValidateOnChange) return _report;

            var document = EditedDocument;

            if (document is null) return BpmnValidationReport.Empty;
            if (ReferenceEquals(document, _validated)) return _report;

            _validated = document;
            _report = BpmnValidator.Validate(document, TemplateProfiles());

            return _report;
        }
    }

    private readonly IServiceProvider _profileServices;

    private IBpmnProfileValidator[] TemplateProfiles()
    {
        var profiles = _profileServices.GetServices<IBpmnProfileValidator>().ToArray();
        return profiles.Any(profile => profile is BpmnElementTemplateValidator) ? profiles :
            [.. profiles, new BpmnElementTemplateValidator(_options.Value.Templates)];
    }

    /// <summary>
    /// Validates the document now and reports the result.
    /// </summary>
    /// <remarks>
    /// For a host that has turned <see cref="ValidateOnChange"/> off, and for a "validate now"
    /// command. Always re-runs: the caller asked.
    /// </remarks>
    /// <returns>The findings.</returns>
    public async Task<BpmnValidationReport> ValidateAsync()
    {
        var document = EditedDocument;

        _validated = document;
        _report = document is null ? BpmnValidationReport.Empty : BpmnValidator.Validate(document, TemplateProfiles());

        await OnValidated.InvokeAsync(_report).ConfigureAwait(true);
        NotifyStateChanged();

        return _report;
    }

    /// <summary>
    /// Selects the element a finding is about.
    /// </summary>
    /// <remarks>
    /// The whole point of a finding carrying an element id. A diagnostic that named an element but
    /// could not take you to it would leave the user searching a diagram for the thing the editor
    /// already knows the location of.
    /// </remarks>
    /// <param name="diagnostic">The finding to go to.</param>
    /// <returns>A task that completes once the selection has been applied.</returns>
    public async Task GoToAsync(BpmnDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        if (diagnostic.ElementId is not { Length: > 0 } elementId) return;

        await SetSelectionAsync([elementId]).ConfigureAwait(true);
    }
}
