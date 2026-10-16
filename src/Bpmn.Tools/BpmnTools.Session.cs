using Hasseware.AspNetCore.Components.Bpmn.Storage;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools;

public static partial class BpmnTools
{
    /// <summary>Reports whether the open document is the stored one, and whether it moved underneath.</summary>
    /// <param name="host">The document; an <see cref="IBpmnPersistenceHost"/> to have a stored document at all.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The stored document's name, revision, unsaved state and external change.</returns>
    public static async ValueTask<BpmnToolResult<BpmnDocumentInfo>> GetDocumentInfoAsync(
        IBpmnToolHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (await host.GetDocumentAsync(cancellationToken).ConfigureAwait(false) is null) return NoDocument<BpmnDocumentInfo>(host);
        if (host is not IBpmnPersistenceHost { IsBound: true } store) return NotStored<BpmnDocumentInfo>(host);

        var change = store.ExternalChange?.Kind switch
        {
            BpmnDocumentChangeKind.Updated => "updated",
            BpmnDocumentChangeKind.Deleted => "deleted",
            _ => "none",
        };

        return Succeeded(host, new BpmnDocumentInfo(
            BpmnUntrustedText.From(store.DocumentName, host.Limits.MaxTextLength),
            store.StoredRevision,
            store.IsDirty,
            change));
    }

    /// <summary>
    /// Saves the open document where it came from. An adapter that can ask the user does so before calling this.
    /// </summary>
    /// <param name="host">The document; an <see cref="IBpmnPersistenceHost"/> to have somewhere to save.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The new stored revision; <see cref="BpmnToolCodes.PersistConflict"/> when someone else changed it first.</returns>
    public static async ValueTask<BpmnToolResult<BpmnSaved>> SaveAsync(
        IBpmnToolHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (await host.GetDocumentAsync(cancellationToken).ConfigureAwait(false) is null) return NoDocument<BpmnSaved>(host);
        if (host is not IBpmnPersistenceHost { IsBound: true } store) return NotStored<BpmnSaved>(host);
        if (host.IsReadOnly) return ReadOnly<BpmnSaved>(host);
        if (store.ExternalChange is not null) return StaleDocument<BpmnSaved>(host);

        BpmnStoreResult result;
        try
        {
            result = await store.SaveAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return NotStored<BpmnSaved>(host);
        }

        return result.Succeeded
            ? Succeeded(host, new BpmnSaved(result.Revision))
            : Failed<BpmnSaved>(host, BpmnToolCodes.PersistConflict, "The stored document changed since it was loaded; nothing was written. Tell the user, who decides what to do.");
    }

    /// <summary>Undoes the last edit, when that edit was this agent's.</summary>
    /// <param name="host">The document; an <see cref="IBpmnInteractiveHost"/> to have a history.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result; refused when the last edit is the user's, so an agent cannot unwind their work.</returns>
    public static async ValueTask<BpmnToolResult> UndoAsync(IBpmnToolHost host, CancellationToken cancellationToken = default) =>
        await StepAsync(host, redo: false, cancellationToken).ConfigureAwait(false);

    /// <summary>Reapplies the last undone edit, when that edit was this agent's.</summary>
    /// <param name="host">The document; an <see cref="IBpmnInteractiveHost"/> to have a history.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result; refused when the edit is the user's.</returns>
    public static async ValueTask<BpmnToolResult> RedoAsync(IBpmnToolHost host, CancellationToken cancellationToken = default) =>
        await StepAsync(host, redo: true, cancellationToken).ConfigureAwait(false);

    /// <summary>Selects elements in the editor so the user sees what the agent means. It changes no content.</summary>
    /// <param name="host">The document; an <see cref="IBpmnInteractiveHost"/> to have a selection to set.</param>
    /// <param name="request">The elements.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The result.</returns>
    public static async ValueTask<BpmnToolResult> SelectAsync(
        IBpmnToolHost host,
        BpmnSelectRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(request);
        if (await host.GetDocumentAsync(cancellationToken).ConfigureAwait(false) is not { } document) return NoDocument<object>(host);
        if (host is not IBpmnInteractiveHost interactive) return Failed<object>(host, BpmnToolCodes.NotFound, "There is no user interface to show a selection in.");

        var ids = request.ElementIds ?? [];
        if (ids.Count > host.Limits.MaxBatch) return Failed<object>(host, BpmnToolCodes.TooLarge, "That is more elements than one call may select.");
        if (ids.Any(id => string.IsNullOrEmpty(id) || document.Find(id) is null))
        {
            return Failed<object>(host, BpmnToolCodes.NotFound, "An element to select does not exist.");
        }

        await interactive.SelectAsync(ids).ConfigureAwait(false);
        return new BpmnToolResult { Ok = true, Revision = host.Revision, IsDirty = DirtyOf(host) };
    }

    private static async ValueTask<BpmnToolResult> StepAsync(IBpmnToolHost host, bool redo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (await host.GetDocumentAsync(cancellationToken).ConfigureAwait(false) is null) return NoDocument<object>(host);
        if (host.IsReadOnly) return ReadOnly<object>(host);
        if (host is not IBpmnInteractiveHost interactive) return Failed<object>(host, BpmnToolCodes.NotFound, "This document has no undo history.");

        var origin = redo ? interactive.RedoOrigin : interactive.UndoOrigin;
        if (origin is null) return Failed<object>(host, BpmnToolCodes.Refused, redo ? "There is nothing to redo." : "There is nothing to undo.");
        if (origin != host.Origin)
        {
            return Failed<object>(host, BpmnToolCodes.Refused, "That edit was not made by this agent, so only the user can undo or redo it.");
        }

        await (redo ? interactive.RedoAsync() : interactive.UndoAsync()).ConfigureAwait(false);
        return new BpmnToolResult { Ok = true, Applied = true, Revision = host.Revision, IsDirty = DirtyOf(host) };
    }

    private static BpmnToolResult<TData> NotStored<TData>(IBpmnToolHost host) =>
        Failed<TData>(host, BpmnToolCodes.NotFound, "This editor is not bound to a stored document.");

    private static BpmnToolResult<TData> StaleDocument<TData>(IBpmnToolHost host) =>
        Failed<TData>(host, BpmnToolCodes.StaleDocument, "The stored document changed behind the editor. The user must choose Reload, Save as copy or Dismiss in the editor first.");

    private static bool? DirtyOf(IBpmnToolHost host) => host is IBpmnPersistenceHost { IsBound: true } store ? store.IsDirty : null;
}
