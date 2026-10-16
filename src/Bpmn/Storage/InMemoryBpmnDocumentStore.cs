namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>A document store held in memory, for tests, demos and the playground.</summary>
/// <remarks>
/// Every operation runs under one lock, which is what makes its compare-and-write atomic. Content is
/// copied on the way in, so a caller that reuses its buffer cannot change a stored document. Nothing
/// survives the process.
/// </remarks>
/// <param name="timeProvider">The clock for modification times; the system clock when null.</param>
public sealed class InMemoryBpmnDocumentStore(TimeProvider? timeProvider = null) : IBpmnDocumentStore
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string? Tenant, BpmnDocumentPath Path), Stored> _documents = [];
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private long _revision;

    /// <inheritdoc />
    public ValueTask<BpmnStoredDocument?> OpenAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default)
    {
        EnsureValid(key);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult(_documents.TryGetValue((key.Tenant, key.Path), out var stored)
                ? new BpmnStoredDocument(stored.Content, stored.Revision, stored.Modified)
                : null);
        }
    }

    /// <inheritdoc />
    public ValueTask<BpmnDocumentStat?> StatAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default)
    {
        EnsureValid(key);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult(_documents.TryGetValue((key.Tenant, key.Path), out var stored)
                ? new BpmnDocumentStat(stored.Revision, stored.Content.Bytes.Length, stored.Modified)
                : null);
        }
    }

    /// <inheritdoc />
    public ValueTask<BpmnStoreResult> SaveAsync(
        BpmnDocumentKey key,
        BpmnDocumentContent content,
        string? ifMatchRevision,
        CancellationToken cancellationToken = default)
    {
        EnsureValid(key);
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();

        var copy = content with { Bytes = content.Bytes.ToArray() };

        lock (_gate)
        {
            var exists = _documents.TryGetValue((key.Tenant, key.Path), out var current);
            if (ifMatchRevision is null && exists) return Result(BpmnStoreStatus.Conflict, current!.Revision);
            if (ifMatchRevision is not null && !exists) return Result(BpmnStoreStatus.NotFound);
            if (exists && !string.Equals(current!.Revision, ifMatchRevision, StringComparison.Ordinal))
            {
                return Result(BpmnStoreStatus.Conflict, current.Revision);
            }

            var stored = new Stored(copy, $"r{++_revision}", _time.GetUtcNow());
            _documents[(key.Tenant, key.Path)] = stored;

            return ValueTask.FromResult(new BpmnStoreResult(BpmnStoreStatus.Succeeded, stored.Revision, stored.Modified));
        }
    }

    /// <inheritdoc />
    public ValueTask<BpmnStoreResult> DeleteAsync(
        BpmnDocumentKey key,
        string? ifMatchRevision,
        CancellationToken cancellationToken = default)
    {
        EnsureValid(key);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_documents.TryGetValue((key.Tenant, key.Path), out var current)) return Result(BpmnStoreStatus.NotFound);
            if (ifMatchRevision is not null && !string.Equals(current.Revision, ifMatchRevision, StringComparison.Ordinal))
            {
                return Result(BpmnStoreStatus.Conflict, current.Revision);
            }

            _documents.Remove((key.Tenant, key.Path));
            return Result(BpmnStoreStatus.Succeeded);
        }
    }

    /// <inheritdoc />
    public ValueTask<BpmnDocumentPage> ListAsync(BpmnDocumentQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        List<BpmnDocumentEntry> entries;
        lock (_gate)
        {
            entries = Entries(query);
        }

        return ValueTask.FromResult(BpmnDocumentPaging.Page(entries, query));
    }

    private static void EnsureValid(BpmnDocumentKey key)
    {
        if (!key.IsValid) throw new ArgumentException("The key does not name a document.", nameof(key));
    }

    private static ValueTask<BpmnStoreResult> Result(BpmnStoreStatus status, string? revision = null) =>
        ValueTask.FromResult(new BpmnStoreResult(status, revision));

    private List<BpmnDocumentEntry> Entries(BpmnDocumentQuery query)
    {
        var entries = new List<BpmnDocumentEntry>();
        var folders = new HashSet<BpmnDocumentPath>();

        foreach (var ((tenant, path), stored) in _documents)
        {
            if (!string.Equals(tenant, query.Tenant, StringComparison.Ordinal) || !path.IsUnder(query.Folder)) continue;

            if (query.Recursive || path.Parent == query.Folder)
            {
                entries.Add(new BpmnDocumentEntry(path, IsFolder: false, stored.Content.Bytes.Length, stored.Modified));
                continue;
            }

            // A document deeper down makes the sub-folder on its way visible at this level.
            var folder = path;
            while (folder.Parent != query.Folder) folder = folder.Parent;
            if (folders.Add(folder)) entries.Add(new BpmnDocumentEntry(folder, IsFolder: true));
        }

        return entries;
    }

    private sealed record Stored(BpmnDocumentContent Content, string Revision, DateTimeOffset Modified);
}
