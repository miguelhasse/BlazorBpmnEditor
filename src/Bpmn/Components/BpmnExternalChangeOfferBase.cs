using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The logic of the notice shown when someone else changed or deleted the stored document this editor
/// has open; a UI kit supplies the markup.
/// </summary>
/// <remarks>
/// <para>
/// Every decision here is the user's. Nothing is replaced or overwritten until they choose: Reload takes
/// the other version (asking first when there are unsaved edits), Save a copy keeps their version under
/// a new name, and Dismiss hides the notice while leaving the conflict in place, so the next save still
/// refuses. After a delete, Save again puts their version back where it was.
/// </para>
/// <para>
/// It is a notice rather than a modal dialog: the user can keep working, and may want to look at what
/// they have before deciding.
/// </para>
/// </remarks>
public abstract class BpmnExternalChangeOfferBase : BpmnEditorComponentBase
{
    private const int MaxCopyAttempts = 20;

    private BpmnExternalChange? _confirmingFor;

    /// <summary>The accessible name of the notice; a localized default when not set.</summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <summary>The accessible name in force.</summary>
    protected string EffectiveAriaLabel => string.IsNullOrWhiteSpace(AriaLabel) ? Text("ExternalChangeOffer.AriaLabel") : AriaLabel;

    /// <summary>The change awaiting an answer, or <see langword="null"/> to show nothing.</summary>
    protected BpmnExternalChange? Change => State.ExternalChange;

    /// <summary>Whether the stored document was deleted rather than updated.</summary>
    protected bool IsDeleted => Change?.Kind == BpmnDocumentChangeKind.Deleted;

    /// <summary>Whether this editor holds edits that a reload would discard.</summary>
    protected bool IsDirty => State.IsDirty;

    /// <summary>The bound document's name, for the notice's text.</summary>
    protected string DocumentName => Session.DocumentBinding?.Key?.Path.Name ?? string.Empty;

    /// <summary>Whether Reload was chosen with unsaved edits and now waits for confirmation.</summary>
    /// <remarks>
    /// Armed for one particular change only. Any other change — a newer one, or the same one resolved by a
    /// save or an automatic reload — shows its own message first, so a single click can never discard edits
    /// in answer to a notice the user has not read.
    /// </remarks>
    protected bool IsConfirmingReload => _confirmingFor is not null && ReferenceEquals(_confirmingFor, Change);

    /// <summary>Whether an action is running, so the kit can disable its buttons.</summary>
    protected bool IsBusy { get; private set; }

    /// <summary>Where the last copy was saved, once Save a copy has succeeded.</summary>
    protected BpmnDocumentPath? SavedCopyPath { get; private set; }

    /// <summary>What the last action came to, for the notice to show; null before one has run.</summary>
    /// <remarks>
    /// Saving a copy resolves the change, so the notice would otherwise vanish while the editor quietly
    /// switched to another document; a failed action would otherwise change nothing the user can see.
    /// </remarks>
    protected string? Outcome { get; private set; }

    /// <summary>Whether <see cref="Outcome"/> reports that the action did not work.</summary>
    protected bool OutcomeIsFailure { get; private set; }

    /// <summary>Whether there is anything to show: a change awaiting an answer or the outcome of an action.</summary>
    protected bool HasNotice => Change is not null || Outcome is not null;

    /// <summary>The resource key of the text describing <see cref="Change"/>.</summary>
    /// <remarks>Formatted with <see cref="DocumentName"/>.</remarks>
    protected string MessageKey => IsDeleted
        ? "ExternalChangeOffer.Deleted"
        : IsDirty ? "ExternalChangeOffer.UpdatedDirty" : "ExternalChangeOffer.Updated";

    /// <inheritdoc />
    protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) =>
        previous.ExternalChange != current.ExternalChange || previous.IsDirty != current.IsDirty;

    /// <summary>
    /// Loads the other version. With unsaved edits, the first call only asks for confirmation; the
    /// second, or <see cref="ConfirmReloadAsync"/>, reloads.
    /// </summary>
    /// <returns>A task that completes when the document is reloaded or confirmation is requested.</returns>
    protected Task ReloadAsync()
    {
        if (IsDirty && !IsConfirmingReload)
        {
            _confirmingFor = Change;
            return Task.CompletedTask;
        }

        return ConfirmReloadAsync();
    }

    /// <summary>Loads the other version, discarding unsaved edits.</summary>
    /// <returns>A task that completes when the document is reloaded.</returns>
    protected Task ConfirmReloadAsync() => RunAsync(async binding =>
    {
        _confirmingFor = null;
        var result = await binding.ReloadAsync().ConfigureAwait(true);
        if (!result.Succeeded) Fail();
    });

    /// <summary>Withdraws a pending Reload confirmation.</summary>
    protected void CancelReload() => _confirmingFor = null;

    /// <summary>
    /// Saves this editor's version beside the other one, as <c>name (copy).bpmn</c> or the first free
    /// numbered variant, and continues editing the copy.
    /// </summary>
    /// <returns>A task that completes when the copy is saved.</returns>
    protected Task SaveCopyAsync() => RunAsync(async binding =>
    {
        if (binding.Key is not { } key) return;

        for (var attempt = 1; attempt <= MaxCopyAttempts; attempt++)
        {
            if (CopyPath(key.Path, attempt) is not { } path) break;

            var copy = new BpmnDocumentKey(key.Tenant, path);
            var result = await binding.SaveAsAsync(copy).ConfigureAwait(true);
            if (result.Succeeded)
            {
                SavedCopyPath = copy.Path;
                Outcome = Text("ExternalChangeOffer.Copied", copy.Path.Name);
                return;
            }

            if (result.Status != BpmnStoreStatus.Conflict) break;
        }

        Fail();
    });

    /// <summary>After a delete, stores this editor's version where the document was.</summary>
    /// <returns>A task that completes when the document is saved.</returns>
    protected Task SaveAgainAsync() => RunAsync(async binding =>
    {
        if (binding.Key is not { } key) return;

        var result = await binding.SaveAsAsync(key).ConfigureAwait(true);
        if (!result.Succeeded) Fail();
    });

    /// <summary>Hides the notice; the conflict remains, so the next save still refuses.</summary>
    protected void Dismiss()
    {
        _confirmingFor = null;
        Outcome = null;
        Session.DocumentBinding?.DismissExternalChange();
    }

    private void Fail()
    {
        Outcome = Text("ExternalChangeOffer.Failed");
        OutcomeIsFailure = true;
    }

    private async Task RunAsync(Func<BpmnDocumentBinding, Task> action)
    {
        if (Session.DocumentBinding is not { } binding || IsBusy) return;

        IsBusy = true;
        Outcome = null;
        OutcomeIsFailure = false;
        try
        {
            await action(binding).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ObjectDisposedException)
        {
            // Unreadable stored content or an unreachable store: tell the user rather than end the circuit.
            Fail();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // The copy's path, shortening the name when the suffix would push it past the segment limit; null when
    // no valid name can be made, for example because the folder path itself is already at its limit.
    internal static BpmnDocumentPath? CopyPath(BpmnDocumentPath path, int attempt)
    {
        var name = path.Name;
        var dot = name.LastIndexOf('.');
        var extension = name[dot..];
        var suffix = attempt == 1 ? " (copy)" : $" (copy {attempt})";

        var room = BpmnDocumentPath.MaxSegmentLength - suffix.Length - extension.Length;
        if (room < 1) return null;

        var stem = name[..dot];
        if (stem.Length > room)
        {
            var cut = char.IsHighSurrogate(stem[room - 1]) ? room - 1 : room;
            stem = stem[..cut].TrimEnd(' ', '.');
        }

        var segment = stem + suffix + extension;
        var candidate = path.Parent.IsRoot ? segment : $"{path.Parent}/{segment}";
        return BpmnDocumentPath.TryParse(candidate, out var copy) ? copy : null;
    }
}
