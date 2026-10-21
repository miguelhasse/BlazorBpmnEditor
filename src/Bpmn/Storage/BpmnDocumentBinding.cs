using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>Another party changed the stored document an editor has open.</summary>
/// <param name="Kind">Whether it was updated or deleted.</param>
/// <param name="Revision">The stored revision now; null when deleted.</param>
/// <param name="DetectedAt">When the editor learned of it.</param>
public sealed record BpmnExternalChange(BpmnDocumentChangeKind Kind, string? Revision, DateTimeOffset DetectedAt);

/// <summary>
/// Ties one editor to one stored document: loads it, saves it with a revision check, tracks unsaved
/// edits, and notices when someone else changes it.
/// </summary>
/// <remarks>
/// <para>
/// The host creates a binding through <see cref="IBpmnDocumentBindingFactory"/>, passes it to the
/// editor's <c>DocumentBinding</c> parameter, and disposes it. The editor then takes its document from
/// the binding and reports every edit back to it, so <see cref="IsDirty"/> follows undo and redo: undoing
/// back to the saved state is clean again, because history hands back the very snapshot that was saved.
/// </para>
/// <para>
/// A conflict is always a result, never an exception and never an overwrite. While another party's
/// change is pending, <see cref="SaveAsync"/> fails with <see cref="BpmnStoreStatus.Conflict"/> until the
/// user reloads or saves a copy.
/// </para>
/// <para>
/// Public members must be called on the renderer's synchronization context, like any component API.
/// Change notifications arrive on other threads and are handed to that context before they touch state.
/// </para>
/// </remarks>
public sealed class BpmnDocumentBinding : IAsyncDisposable
{
    private readonly IBpmnDocumentStore _store;
    private readonly IBpmnDocumentChangeSubscriber? _subscriber;
    private readonly StorageOptions _options;
    private readonly TimeProvider _time;
    private readonly List<BpmnDocumentChange> _heldDuringSave = [];
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private IAsyncDisposable? _subscription;
    private bool _saving;

    // The format the loaded document was in; a file named .json may hold XML, and saving must not change what it holds. Null once saved under another key.
    private BpmnFileFormat? _format;
    private bool _wasDirty;
    private bool _disposed;
    private bool _missedWhileDetached;

    /// <summary>Creates a binding.</summary>
    /// <param name="store">Where the document is kept.</param>
    /// <param name="subscriber">Where external changes come from; none are noticed when null.</param>
    /// <param name="options">Storage behaviour; the defaults when null.</param>
    /// <param name="timeProvider">The clock for <see cref="BpmnExternalChange.DetectedAt"/>; the system clock when null.</param>
    public BpmnDocumentBinding(
        IBpmnDocumentStore store,
        IBpmnDocumentChangeSubscriber? subscriber = null,
        StorageOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _subscriber = subscriber;
        _options = options ?? StorageOptions.Default;
        _time = timeProvider ?? TimeProvider.System;

        if (_subscriber is not null)
        {
            _subscriber.StateChanged += OnFeedStateChangedAsync;
            _subscriber.Resubscribed += OnResubscribedAsync;
        }
    }

    /// <summary>
    /// Raised when the key, document, saved state, dirty flag, external change or feed state changes.
    /// Not raised for every edit; only when <see cref="IsDirty"/> flips.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>The document's key, or null before anything is loaded.</summary>
    public BpmnDocumentKey? Key { get; private set; }

    /// <summary>The document as last loaded, saved or published by the editor.</summary>
    public BpmnDocument? Document { get; private set; }

    /// <summary>The snapshot that matches what is stored at <see cref="StoredRevision"/>.</summary>
    public BpmnDocument? SavedDocument { get; private set; }

    /// <summary>The stored revision this editor last loaded or saved.</summary>
    public string? StoredRevision { get; private set; }

    /// <summary>Whether the editor holds edits that are not stored.</summary>
    /// <remarks>
    /// The editor reports every document it publishes, undo and redo included, so <see cref="Document"/>
    /// is always the latest and the comparison needs nothing from the editor at the moment it is read.
    /// </remarks>
    public bool IsDirty => Document is { } current && !ReferenceEquals(current, SavedDocument);

    /// <summary>A change by another party that the user has not resolved, or null.</summary>
    public BpmnExternalChange? ExternalChange { get; private set; }

    /// <summary>Whether change notifications are currently arriving.</summary>
    public BpmnChangeFeedState FeedState => _subscriber?.State ?? BpmnChangeFeedState.Closed;

    /// <summary>What the reader normalized or could not keep when the document was last loaded.</summary>
    public IReadOnlyList<BpmnImportDiagnostic> ImportDiagnostics { get; private set; } = [];

    /// <summary>The editor this binding is attached to, if any; one editor at a time.</summary>
    internal object? Owner { get; set; }

    /// <summary>Detaches the binding from its current editor, so another can take it over.</summary>
    internal Action? Release { get; set; }

    /// <summary>Runs work on the renderer's context; set by the editor that attaches the binding.</summary>
    /// <remarks>
    /// Setting it hands over whatever was heard while no editor was attached, as a single catch-up check.
    /// </remarks>
    internal Func<Func<Task>, Task>? Dispatcher
    {
        get;
        set
        {
            field = value;
            if (value is null || !_missedWhileDetached) return;

            _missedWhileDetached = false;
            _ = CatchUpAsync(value);
        }
    }

    /// <summary>
    /// Whether notifications that arrive while no editor is attached wait for one, instead of running on the
    /// notifier's thread.
    /// </summary>
    /// <remarks>
    /// A binding a host creates is loaded before its editor renders. Until then there is no renderer context
    /// to hand notifications to, and running them on whichever thread the writer used would race the host's
    /// own use of the binding.
    /// </remarks>
    internal bool DeferUntilAttached { get; init; }

    /// <summary>Whether the editor is mid-gesture, such as editing a label, and must not be reloaded.</summary>
    internal Func<bool>? IsBusy { get; set; }

    /// <summary>Loads a stored document, replacing whatever the editor had open.</summary>
    /// <param name="key">The document.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns><see cref="BpmnStoreStatus.Succeeded"/> with the revision, or <see cref="BpmnStoreStatus.NotFound"/>.</returns>
    /// <exception cref="BpmnImportException">The stored content is not a readable BPMN document.</exception>
    public async Task<BpmnStoreResult> LoadAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var loaded = await _store.LoadDocumentAsync(key, cancellationToken).ConfigureAwait(true);
        if (loaded is null) return new BpmnStoreResult(BpmnStoreStatus.NotFound);

        Key = key;
        Document = SavedDocument = loaded.Document;
        StoredRevision = loaded.Revision;
        _format = loaded.Format;
        ImportDiagnostics = loaded.Diagnostics;
        ExternalChange = null;
        _wasDirty = false;

        // What was loaded is announced before the subscription is made: a feed that is slow or cannot be opened must not leave the editor showing
        // the document it had, nor undo a load that worked.
        RaiseChanged();
        await SubscribeQuietlyAsync(key, cancellationToken).ConfigureAwait(true);

        return new BpmnStoreResult(BpmnStoreStatus.Succeeded, loaded.Revision, loaded.Modified);
    }

    /// <summary>Loads the stored document again, discarding unsaved edits and any pending external change.</summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>The load result; <see cref="BpmnStoreStatus.NotFound"/> when the document was deleted.</returns>
    public Task<BpmnStoreResult> ReloadAsync(CancellationToken cancellationToken = default) =>
        LoadAsync(RequireKey(), cancellationToken);

    /// <summary>
    /// Asks the store whether the document changed since it was loaded or saved, and reports it as an
    /// external change when it did.
    /// </summary>
    /// <remarks>
    /// The binding makes this check itself after subscribing, after every gap in notifications and
    /// before every save. Hosts may call it on events they know about, such as the browser tab regaining
    /// focus. It is never called on a timer.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A task that completes when the check is done.</returns>
    public async Task CheckForChangesAsync(CancellationToken cancellationToken = default)
    {
        if (await ProbeAsync(cancellationToken).ConfigureAwait(true) is { } change) await ApplyAsync(change).ConfigureAwait(true);
    }

    /// <summary>Stores the editor's current document, if the stored revision is still the one it holds.</summary>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>
    /// <see cref="BpmnStoreStatus.Succeeded"/> with the new revision; <see cref="BpmnStoreStatus.Conflict"/>
    /// or <see cref="BpmnStoreStatus.NotFound"/> when someone else changed or deleted it, which is then
    /// also reported as <see cref="ExternalChange"/>. Nothing is overwritten.
    /// </returns>
    /// <exception cref="InvalidOperationException">Nothing has been loaded.</exception>
    public async Task<BpmnStoreResult> SaveAsync(CancellationToken cancellationToken = default)
    {
        RequireKey();

        // One write at a time: a second Save waits for the first and then writes on top of it, so a
        // double click neither conflicts with itself nor mistakes its own echo for somebody else's.
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            var key = RequireKey();
            var document = Document!;

            // The revision this document was based on, taken with it. A reload during the probe below replaces both; writing the document the user
            // had with the revision the reload brought would put it over what the reload showed, so the write is made against the one it was based on.
            var basedOn = StoredRevision;

            // Checked first so the user hears about a conflict before the write rather than as a failed
            // one. The check reports and never reloads, whatever the policy: reloading here would write the
            // stale snapshot over the newer stored one.
            if (await ProbeAsync(cancellationToken).ConfigureAwait(true) is { } early) return Refused(early);

            var result = await WriteAsync(key, document, basedOn, key, cancellationToken).ConfigureAwait(true);
            if (!result.Succeeded && await ProbeAsync(cancellationToken).ConfigureAwait(true) is { } late) Refused(late);

            return result;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>Stores the editor's current document under a new key, which then becomes the bound document.</summary>
    /// <param name="key">The new document's key; nothing may be stored there yet.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>
    /// <see cref="BpmnStoreStatus.Succeeded"/> with the revision, or <see cref="BpmnStoreStatus.Conflict"/>
    /// when a document already exists at <paramref name="key"/>.
    /// </returns>
    /// <exception cref="InvalidOperationException">There is no document to save.</exception>
    public async Task<BpmnStoreResult> SaveAsAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Document is null) throw new InvalidOperationException("There is no document to save.");

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            // Changes heard while a save-as is in flight are for the document still bound; if the write fails it stays bound and they still count.
            var result = await WriteAsync(key, Document, ifMatchRevision: null, Key ?? key, cancellationToken).ConfigureAwait(true);
            if (!result.Succeeded) return result;

            Key = key;
            ExternalChange = null;
            RaiseChanged();

            // The write is done and stands whether or not live updates can be had for the new key.
            await SubscribeQuietlyAsync(key, cancellationToken).ConfigureAwait(true);

            return result;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>
    /// Hides a pending external change without resolving it; the next save still reports the conflict.
    /// </summary>
    public void DismissExternalChange()
    {
        if (ExternalChange is null) return;

        ExternalChange = null;
        RaiseChanged();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_subscriber is not null)
        {
            _subscriber.StateChanged -= OnFeedStateChangedAsync;
            _subscriber.Resubscribed -= OnResubscribedAsync;
        }

        if (_subscription is not null) await _subscription.DisposeAsync().ConfigureAwait(false);
        _subscription = null;

        // A save in flight still has the gate and releases it when it ends; disposing underneath it would
        // make that release throw. Saves that start after this are refused by the disposed check.
        await _writeGate.WaitAsync().ConfigureAwait(false);
        _writeGate.Dispose();
    }

    /// <summary>Records a document the editor published, so <see cref="IsDirty"/> follows every edit.</summary>
    /// <param name="published">The editor's new document.</param>
    internal void Track(BpmnDocument published)
    {
        Document = published;
        if (_wasDirty != IsDirty) RaiseChanged();
    }

    private async Task<BpmnStoreResult> WriteAsync(
        BpmnDocumentKey key,
        BpmnDocument document,
        string? ifMatchRevision,
        BpmnDocumentKey heldKeyIfNotWritten,
        CancellationToken cancellationToken)
    {
        // A store wrapped for notifications publishes this very save before returning, and an in-process
        // bus delivers it at once. Hold notifications until the new revision is known, or the editor's
        // own save would be reported as somebody else's.
        _saving = true;
        BpmnStoreResult result;
        var sameKey = key == Key;
        try
        {
            result = await _store.SaveDocumentAsync(key, document, ifMatchRevision, sameKey ? _format : null, cancellationToken).ConfigureAwait(true);
        }
        catch
        {
            // The write failed, but what was heard meanwhile is still true.
            _saving = false;
            await ApplyHeldAsync(heldKeyIfNotWritten).ConfigureAwait(true);
            throw;
        }

        _saving = false;
        if (result.Succeeded)
        {
            // Only the snapshot that was written is now saved. Document is left alone: if the user kept
            // editing while the write was in flight it already holds newer work, which stays dirty.
            SavedDocument = document;
            StoredRevision = result.Revision;
            if (!sameKey) _format = null;
        }

        await ApplyHeldAsync(result.Succeeded ? key : heldKeyIfNotWritten).ConfigureAwait(true);

        if (result.Succeeded) RaiseChanged();
        return result;
    }

    // Only changes to the document just written count. For a save-as that is the new key, which nothing
    // was subscribed to yet, so changes heard for the document being left behind are dropped.
    private async Task ApplyHeldAsync(BpmnDocumentKey writtenKey)
    {
        var held = _heldDuringSave.ToList();
        _heldDuringSave.Clear();
        foreach (var change in held.Where(change => change.Key == writtenKey)) await ApplyAsync(change).ConfigureAwait(true);
    }

    // Live updates improve on the check every save makes, they are not what makes a save safe: failing to get them is not a failure of the operation
    // that asked for them. Cancellation and disposal still end it.
    private async Task SubscribeQuietlyAsync(BpmnDocumentKey key, CancellationToken cancellationToken)
    {
        try
        {
            await SubscribeAsync(key, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ObjectDisposedException)
        {
            // No feed for now: the next save, and any call to CheckForChangesAsync, still compares revisions.
        }
    }

    private async Task SubscribeAsync(BpmnDocumentKey key, CancellationToken cancellationToken)
    {
        if (_subscription is not null) await _subscription.DisposeAsync().ConfigureAwait(true);
        _subscription = null;

        if (_subscriber is null) return;

        _subscription = await _subscriber.SubscribeAsync(key, OnNotifiedAsync, cancellationToken).ConfigureAwait(true);

        // Anything that changed between reading the document and subscribing would otherwise go unseen.
        await CheckForChangesAsync(cancellationToken).ConfigureAwait(true);
    }

    private ValueTask OnNotifiedAsync(BpmnDocumentChange change) => DispatchAsync(() => ApplyAsync(change));

    private ValueTask OnResubscribedAsync() => DispatchAsync(() => CheckForChangesAsync());

    private ValueTask OnFeedStateChangedAsync(BpmnChangeFeedState state) => DispatchAsync(() =>
    {
        RaiseChanged();
        return Task.CompletedTask;
    });

    private ValueTask DispatchAsync(Func<Task> work)
    {
        if (_disposed) return ValueTask.CompletedTask;
        if (Dispatcher is { } dispatcher) return new ValueTask(dispatcher(work));

        if (DeferUntilAttached)
        {
            _missedWhileDetached = true;
            return ValueTask.CompletedTask;
        }

        return new ValueTask(work());
    }

    // What was missed is not replayed one by one: the store says what is true now.
    private async Task CatchUpAsync(Func<Func<Task>, Task> dispatcher)
    {
        try
        {
            await dispatcher(() => CheckForChangesAsync()).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ObjectDisposedException)
        {
            // The store is unreachable for now; the next notification or save checks again.
        }
    }

    private async Task ApplyAsync(BpmnDocumentChange change)
    {
        if (_disposed || change.Key != Key) return;

        if (_saving)
        {
            _heldDuringSave.Add(change);
            return;
        }

        // Our own revision coming back, or a change already reported, is not news. Revisions are opaque,
        // so equality is the only comparison made.
        if (change.Kind == BpmnDocumentChangeKind.Updated && change.Revision == StoredRevision) return;
        if (ExternalChange is { } pending && pending.Kind == change.Kind && pending.Revision == change.Revision) return;

        if (_options.ExternalChangePolicy == BpmnExternalChangePolicy.Ignore) return;

        if (_options.ExternalChangePolicy == BpmnExternalChangePolicy.AutoReloadWhenClean
            && change.Kind == BpmnDocumentChangeKind.Updated
            && !IsDirty
            && IsBusy?.Invoke() != true)
        {
            // A reload that cannot finish must not swallow the change: fall back to telling the user.
            try
            {
                var reloaded = await ReloadAsync().ConfigureAwait(true);
                if (reloaded.Succeeded) return;

                change = change with { Kind = BpmnDocumentChangeKind.Deleted, Revision = null };
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not ObjectDisposedException)
            {
                // Unreadable content or an unreachable store; the notice still offers Save a copy.
            }
        }

        ExternalChange = new BpmnExternalChange(change.Kind, change.Revision, _time.GetUtcNow());
        RaiseChanged();
    }

    // What the store holds now, as a change, when it is not the revision this editor holds.
    private async Task<BpmnDocumentChange?> ProbeAsync(CancellationToken cancellationToken)
    {
        if (Key is not { } key || _disposed) return null;

        var stat = await _store.StatAsync(key, cancellationToken).ConfigureAwait(true);
        if (stat is null) return new BpmnDocumentChange(key, BpmnDocumentChangeKind.Deleted, null, null, _time.GetUtcNow());

        return stat.Revision == StoredRevision
            ? null
            : new BpmnDocumentChange(key, BpmnDocumentChangeKind.Updated, stat.Revision, null, _time.GetUtcNow());
    }

    // A save that met someone else's change: report it, unless the host asked to hear about conflicts only
    // from the save result, and answer as the store would have.
    private BpmnStoreResult Refused(BpmnDocumentChange change)
    {
        var reported = ExternalChange is { } pending && pending.Kind == change.Kind && pending.Revision == change.Revision;
        if (_options.ExternalChangePolicy != BpmnExternalChangePolicy.Ignore && !reported)
        {
            ExternalChange = new BpmnExternalChange(change.Kind, change.Revision, _time.GetUtcNow());
            RaiseChanged();
        }

        return change.Kind == BpmnDocumentChangeKind.Deleted
            ? new BpmnStoreResult(BpmnStoreStatus.NotFound)
            : new BpmnStoreResult(BpmnStoreStatus.Conflict, change.Revision);
    }

    private BpmnDocumentKey RequireKey()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return Key ?? throw new InvalidOperationException("No document has been loaded.");
    }

    private void RaiseChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        _wasDirty = IsDirty;
    }
}
