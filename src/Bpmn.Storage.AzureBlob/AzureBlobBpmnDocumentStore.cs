using System.Globalization;

using Azure.Storage.Blobs;

using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.AzureBlob;

/// <summary>A document store over an Azure Blob Storage container.</summary>
/// <remarks>
/// <para>
/// A document is a blob named by the configured prefix, then the logical path (or <c>~tenants/&lt;tenant&gt;</c> and the path for a
/// tenant; <c>~</c> cannot appear in a document path, so the two never collide). The blob's ETag is the revision, a create is
/// <c>If-None-Match: *</c> and an update <c>If-Match</c>, so the service makes each compare-and-write atomic.
/// </para>
/// <para>
/// Listing reads the flat list of blobs under a prefix and builds folders in memory, so it is bounded by
/// <see cref="MaxListEntries"/>; a container with more documents than that under a folder must be listed by narrowing it.
/// Soft delete and blob versioning are the container's recovery story: <see cref="DeleteAsync"/> is not undo.
/// </para>
/// </remarks>
public sealed class AzureBlobBpmnDocumentStore : IBpmnDocumentStore
{
    private const string TenantFolder = "~tenants";

    private readonly IBpmnBlobContainer _container;
    private readonly string _prefix;

    /// <summary>Creates a store over an Azure container.</summary>
    /// <param name="container">The container, from the host's configuration and credentials.</param>
    /// <param name="prefix">A folder inside the container to keep documents under; the container's root when null.</param>
    public AzureBlobBpmnDocumentStore(BlobContainerClient container, string? prefix = null)
        : this(new AzureBlobContainer(container), prefix)
    {
    }

    /// <summary>Creates a store over a container.</summary>
    /// <param name="container">The container.</param>
    /// <param name="prefix">A folder inside the container to keep documents under; the container's root when null.</param>
    /// <exception cref="ArgumentException">The prefix is not a valid folder path.</exception>
    public AzureBlobBpmnDocumentStore(IBpmnBlobContainer container, string? prefix = null)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (!string.IsNullOrEmpty(prefix) && !BpmnDocumentPath.TryParse(prefix, out _))
        {
            throw new ArgumentException("The prefix must be a valid folder path such as diagrams/prod.", nameof(prefix));
        }

        _container = container;
        _prefix = string.IsNullOrEmpty(prefix) ? string.Empty : prefix + "/";
    }

    /// <summary>The most documents one listing considers.</summary>
    public int MaxListEntries { get; init; } = 100_000;

    /// <inheritdoc />
    public async ValueTask<BpmnStoredDocument?> OpenAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default)
    {
        var blob = await _container.ReadAsync(NameOf(key), cancellationToken).ConfigureAwait(false);

        return blob is null
            ? null
            : new BpmnStoredDocument(new BpmnDocumentContent(blob.Bytes, BpmnFileFormats.Detect(key.Path.Name, content: null)), blob.ETag, blob.Modified);
    }

    /// <inheritdoc />
    public async ValueTask<BpmnDocumentStat?> StatAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default)
    {
        var info = await _container.StatAsync(NameOf(key), cancellationToken).ConfigureAwait(false);
        return info is null ? null : new BpmnDocumentStat(info.ETag, info.Size, info.Modified);
    }

    /// <inheritdoc />
    public async ValueTask<BpmnStoreResult> SaveAsync(
        BpmnDocumentKey key,
        BpmnDocumentContent content,
        string? ifMatchRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var name = NameOf(key);

        var written = await _container.WriteAsync(name, content.Bytes.ToArray(), ifMatchRevision, cancellationToken).ConfigureAwait(false);
        return written.Outcome switch
        {
            BpmnBlobOutcome.Succeeded => new BpmnStoreResult(BpmnStoreStatus.Succeeded, written.ETag, written.Modified),
            _ => await RefusedAsync(name, ifMatchRevision is null, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <inheritdoc />
    public async ValueTask<BpmnStoreResult> DeleteAsync(
        BpmnDocumentKey key,
        string? ifMatchRevision,
        CancellationToken cancellationToken = default)
    {
        var name = NameOf(key);

        var outcome = await _container.DeleteAsync(name, ifMatchRevision, cancellationToken).ConfigureAwait(false);
        return outcome switch
        {
            BpmnBlobOutcome.Succeeded => new BpmnStoreResult(BpmnStoreStatus.Succeeded),
            _ => await RefusedAsync(name, creating: false, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <inheritdoc />
    public async ValueTask<BpmnDocumentPage> ListAsync(BpmnDocumentQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var root = _prefix + TenantPrefix(query.Tenant);
        var listing = root + (query.Folder.IsRoot ? string.Empty : query.Folder + "/");

        var entries = new List<BpmnDocumentEntry>();
        var folders = new HashSet<BpmnDocumentPath>();
        var seen = 0;
        var tenantFolder = query.Tenant is null ? _prefix + TenantFolder + "/" : null;
        await foreach (var blob in _container.ListAsync(listing, cancellationToken).ConfigureAwait(false))
        {
            // A tenant's folder seen from the default space is not part of it, so it neither counts toward the limit nor becomes a document.
            if (tenantFolder is not null && blob.Name.StartsWith(tenantFolder, StringComparison.Ordinal)) continue;

            if (++seen > MaxListEntries)
            {
                throw new InvalidOperationException($"More than {MaxListEntries.ToString(CultureInfo.InvariantCulture)} documents are under that folder; list a narrower folder or give a name prefix.");
            }

            // Anything that is not a valid document path under the root (a tenant's folder seen from the default space, a stray blob) is not a document here.
            if (!blob.Name.StartsWith(root, StringComparison.Ordinal) || !BpmnDocumentPath.TryParse(blob.Name[root.Length..], out var path) || !path.IsDocumentName) continue;

            if (query.Recursive || path.Parent == query.Folder)
            {
                entries.Add(new BpmnDocumentEntry(path, IsFolder: false, blob.Size, blob.Modified));
                continue;
            }

            var folder = path;
            while (folder.Parent != query.Folder) folder = folder.Parent;
            if (folders.Add(folder)) entries.Add(new BpmnDocumentEntry(folder, IsFolder: true));
        }

        return BpmnDocumentPaging.Page(entries, query);
    }

    /// <summary>Works out which document a blob is, which is what an ingress needs to turn a storage event into a change.</summary>
    /// <param name="blobName">The blob's full name in the container.</param>
    /// <param name="prefix">The prefix the store was given; none when null.</param>
    /// <param name="key">The document the blob is.</param>
    /// <returns>False when the blob is not a document of this store (another prefix, a stray file, a malformed name).</returns>
    public static bool TryKeyOf(string blobName, string? prefix, out BpmnDocumentKey key)
    {
        key = default;
        var root = string.IsNullOrEmpty(prefix) ? string.Empty : prefix + "/";
        if (!blobName.StartsWith(root, StringComparison.Ordinal)) return false;

        var rest = blobName[root.Length..];
        string? tenant = null;
        if (rest.StartsWith(TenantFolder + "/", StringComparison.Ordinal))
        {
            var afterFolder = rest[(TenantFolder.Length + 1)..];
            var slash = afterFolder.IndexOf('/', StringComparison.Ordinal);
            if (slash <= 0) return false;

            tenant = afterFolder[..slash];
            rest = afterFolder[(slash + 1)..];
            if (!BpmnDocumentPath.TryParse(tenant, out _)) return false;
        }

        if (!BpmnDocumentPath.TryParse(rest, out var path) || !path.IsDocumentName) return false;

        key = new BpmnDocumentKey(tenant, path);
        return true;
    }

    private static string TenantPrefix(string? tenant)
    {
        if (tenant is null) return string.Empty;

        if (tenant.Length == 0 || tenant.Contains('/', StringComparison.Ordinal) || !BpmnDocumentPath.TryParse(tenant, out _))
        {
            throw new ArgumentException("A tenant must be a single valid path segment.", nameof(tenant));
        }

        return $"{TenantFolder}/{tenant}/";
    }

    private string NameOf(BpmnDocumentKey key)
    {
        if (!key.IsValid) throw new ArgumentException("The key does not name a document.", nameof(key));

        return _prefix + TenantPrefix(key.Tenant) + key.Path;
    }

    // Why a conditional write did nothing: the blob is there when it had to be absent, or is at another ETag, or is not there.
    private async Task<BpmnStoreResult> RefusedAsync(string name, bool creating, CancellationToken cancellationToken)
    {
        var current = await _container.StatAsync(name, cancellationToken).ConfigureAwait(false);
        if (current is not null) return new BpmnStoreResult(BpmnStoreStatus.Conflict, current.ETag);

        // A create that failed with nothing there was refused for another reason; it is reported as the conflict it looked like.
        return creating ? new BpmnStoreResult(BpmnStoreStatus.Conflict) : new BpmnStoreResult(BpmnStoreStatus.NotFound);
    }
}
