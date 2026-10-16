namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>
/// The one place a signal from outside the decorated store becomes a change on the bus: an event from a storage account, a
/// notification from a database, a webhook from another application.
/// </summary>
/// <remarks>
/// <para>
/// A store that was written through <see cref="NotifyingBpmnDocumentStore"/> already tells the bus. Writers that bypass it (an upload in a
/// portal, a job that updates a table, a second application) cannot, unless the host forwards the backend's own change signal here. Such
/// signals are delivered at least once and in no particular order, so a signal whose revision is the one last announced for the document
/// is dropped; nothing else is assumed about order, because the binding only asks "is the stored revision still mine".
/// </para>
/// <para>The samples in the repository's <c>samples</c> folder forward an Azure Event Grid blob event and a PostgreSQL <c>NOTIFY</c> through this.</para>
/// </remarks>
public sealed class BpmnDocumentChangeIngress
{
    private const int RememberedKeys = 4096;

    private readonly IBpmnDocumentChangeBus _bus;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<BpmnDocumentKey, (bool Deleted, string? Revision)> _last = [];
    private readonly Queue<BpmnDocumentKey> _order = new();

    /// <summary>Creates an ingress for a bus.</summary>
    /// <param name="bus">Where changes are published.</param>
    /// <param name="timeProvider">The clock for <see cref="BpmnDocumentChange.OccurredAt"/>; the system clock when null.</param>
    public BpmnDocumentChangeIngress(IBpmnDocumentChangeBus bus, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(bus);

        _bus = bus;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Announces that a document was created or replaced.</summary>
    /// <param name="key">The document.</param>
    /// <param name="revision">Its new revision, as the store reports it (an ETag, a version).</param>
    /// <param name="cancellationToken">Cancels the publication.</param>
    /// <returns>True when it was published; false when it repeated the revision last announced for the document.</returns>
    public async ValueTask<bool> PublishUpdatedAsync(BpmnDocumentKey key, string? revision, CancellationToken cancellationToken = default)
    {
        if (!Remember(key, deleted: false, revision, out var previous)) return false;

        try
        {
            await _bus.PublishAsync(new BpmnDocumentChange(key, BpmnDocumentChangeKind.Updated, revision, Origin: null, _time.GetUtcNow()), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Forget(key, previous);
            throw;
        }

        return true;
    }

    /// <summary>Announces that a document was deleted.</summary>
    /// <param name="key">The document.</param>
    /// <param name="cancellationToken">Cancels the publication.</param>
    /// <returns>True when it was published; false when the document was already announced as deleted.</returns>
    public async ValueTask<bool> PublishDeletedAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default)
    {
        if (!Remember(key, deleted: true, revision: null, out var previous)) return false;

        try
        {
            await _bus.PublishAsync(new BpmnDocumentChange(key, BpmnDocumentChangeKind.Deleted, Revision: null, Origin: null, _time.GetUtcNow()), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Forget(key, previous);
            throw;
        }

        return true;
    }

    // The last thing announced for a document: its kind and revision, so a delete is never mistaken for an update that carried no revision.
    private bool Remember(BpmnDocumentKey key, bool deleted, string? revision, out (bool Deleted, string? Revision)? previous)
    {
        lock (_gate)
        {
            previous = _last.TryGetValue(key, out var found) ? found : null;
            if (previous is { } last && last.Deleted == deleted && string.Equals(last.Revision, revision, StringComparison.Ordinal)) return false;

            if (previous is null)
            {
                _order.Enqueue(key);
                if (_order.Count > RememberedKeys) _last.Remove(_order.Dequeue());
            }

            _last[key] = (deleted, revision);
            return true;
        }
    }

    // A publication that failed was not announced, so a redelivery of the same signal must get through.
    private void Forget(BpmnDocumentKey key, (bool Deleted, string? Revision)? previous)
    {
        lock (_gate)
        {
            if (previous is { } last) _last[key] = last;
            else _last.Remove(key);
        }
    }
}
