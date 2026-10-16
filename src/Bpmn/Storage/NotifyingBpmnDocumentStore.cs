namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>A store that publishes a change notification after every successful write or delete.</summary>
/// <remarks>
/// <para>
/// Registration wraps the host's store in this automatically whenever an
/// <see cref="IBpmnDocumentChangeBus"/> is registered, so every writer that goes through the registered
/// store — the editor, agent tools, host code — is heard by open editors.
/// </para>
/// <para>
/// It publishes after the inner store has written, never before, and never for a conflict or a missing
/// document: a notification always describes something that happened. Writes that bypass the store
/// entirely are not seen; a host that needs them adds an ingress that publishes to the bus.
/// </para>
/// </remarks>
/// <param name="inner">The store that does the writing.</param>
/// <param name="bus">Where changes are published.</param>
/// <param name="timeProvider">The clock for <see cref="BpmnDocumentChange.OccurredAt"/>; the system clock when null.</param>
/// <param name="origin">Identifies this writer in notifications; a hint for recipients only.</param>
public sealed class NotifyingBpmnDocumentStore(
    IBpmnDocumentStore inner,
    IBpmnDocumentChangeBus bus,
    TimeProvider? timeProvider = null,
    string? origin = null) : IBpmnDocumentStore
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>The store that does the writing.</summary>
    public IBpmnDocumentStore Inner => inner;

    /// <inheritdoc />
    public ValueTask<BpmnStoredDocument?> OpenAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default) =>
        inner.OpenAsync(key, cancellationToken);

    /// <inheritdoc />
    public ValueTask<BpmnDocumentStat?> StatAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default) =>
        inner.StatAsync(key, cancellationToken);

    /// <inheritdoc />
    public ValueTask<BpmnDocumentPage> ListAsync(BpmnDocumentQuery query, CancellationToken cancellationToken = default) =>
        inner.ListAsync(query, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<BpmnStoreResult> SaveAsync(
        BpmnDocumentKey key,
        BpmnDocumentContent content,
        string? ifMatchRevision,
        CancellationToken cancellationToken = default)
    {
        var result = await inner.SaveAsync(key, content, ifMatchRevision, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) await PublishAsync(key, BpmnDocumentChangeKind.Updated, result.Revision).ConfigureAwait(false);

        return result;
    }

    /// <inheritdoc />
    public async ValueTask<BpmnStoreResult> DeleteAsync(
        BpmnDocumentKey key,
        string? ifMatchRevision,
        CancellationToken cancellationToken = default)
    {
        var result = await inner.DeleteAsync(key, ifMatchRevision, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded) await PublishAsync(key, BpmnDocumentChangeKind.Deleted, revision: null).ConfigureAwait(false);

        return result;
    }

    // The write has already happened, so a caller cancelling now must not suppress the notification:
    // open editors would then miss a change that is real.
    //
    // And a bus that cannot be reached must not fail it either. In process the bus never throws, but a broker-backed one can, and the caller
    // would take a successful write for a failed one, retry it, and conflict with itself. Open editors learn at their next save or catch-up check.
    private async ValueTask PublishAsync(BpmnDocumentKey key, BpmnDocumentChangeKind kind, string? revision)
    {
        try
        {
            await bus.PublishAsync(new BpmnDocumentChange(key, kind, revision, origin, _time.GetUtcNow()), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Best effort, as described above.
        }
    }
}
