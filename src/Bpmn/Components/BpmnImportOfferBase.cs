using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The logic of the import confirmation, which shows what a file would bring in before it
/// replaces anything; a UI kit supplies the markup.
/// </summary>
/// <remarks>
/// An import cannot be undone into anything useful, because the history belongs to the document
/// being replaced. So it is a question rather than an action, and the answer has to be informed:
/// how much is in the file, what the validator makes of it and whether anything in it will not
/// survive a round trip.
/// </remarks>
public abstract class BpmnImportOfferBase : BpmnEditorComponentBase
{
    private BpmnImportPreview? _confirmedPreview;

    /// <summary>Consent belongs to the current preview, never a previous file.</summary>
    protected bool AnnotationNormalizationConfirmed
    {
        get => Preview is not null && ReferenceEquals(Preview, _confirmedPreview);
        set => _confirmedPreview = value ? Preview : null;
    }

    /// <summary>Whether parsing and required loss confirmation allow opening the file.</summary>
    protected bool CanAccept => Preview is { CanAccept: true } preview
        && (!preview.AnnotationNormalization.RequiresConfirmation || AnnotationNormalizationConfirmed);

    /// <inheritdoc />
    protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) =>
        !ReferenceEquals(previous.PendingImport, current.PendingImport);

    /// <summary>
    /// How many dropped-content messages to list before the list stops being read.
    /// </summary>
    /// <remarks>
    /// A file with two hundred Choreography tasks would otherwise print two hundred lines, and the
    /// count above the list already says how many there are.
    /// </remarks>
    protected const int MaxListed = 6;

    /// <summary>The accessible name of the dialog; a localized default when not set.</summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <summary>The accessible name in force.</summary>
    protected string EffectiveAriaLabel => string.IsNullOrWhiteSpace(AriaLabel) ? Text("ImportOffer.AriaLabel") : AriaLabel;

    /// <summary>The import awaiting an answer, or <see langword="null"/> to show nothing.</summary>
    protected BpmnImportPreview? Preview => State.PendingImport;

    /// <summary>What the reader could not represent.</summary>
    protected IReadOnlyList<BpmnImportDiagnostic> Dropped => Preview is null
        ? []
        : [.. Preview.Diagnostics
            .Where(diagnostic => diagnostic.Severity == BpmnImportSeverity.Dropped)
            .Take(MaxListed)];

    /// <summary>What the reader kept but altered, which is a different warning from losing it.</summary>
    protected IReadOnlyList<BpmnImportDiagnostic> Changed => Preview is null
        ? []
        : [.. Preview.Diagnostics
            .Where(diagnostic => diagnostic.Severity == BpmnImportSeverity.Degraded)
            .Take(MaxListed)];

    /// <summary>Opens the file, replacing what is on screen.</summary>
    /// <returns>A task that completes once the file is open.</returns>
    protected Task AcceptAsync() => Session.AcceptImportAsync(AnnotationNormalizationConfirmed);

    /// <summary>Declines the file.</summary>
    /// <returns>A completed task.</returns>
    protected Task CancelAsync()
    {
        Session.CancelImport();
        return Task.CompletedTask;
    }
}
