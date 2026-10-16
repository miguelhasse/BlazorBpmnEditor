using System.Globalization;

using Hasseware.AspNetCore.Components.Bpmn.Serialization;

using Microsoft.EntityFrameworkCore;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.EfCore;

/// <summary>One stored document: a row of the table <see cref="BpmnDocumentModelBuilderExtensions.ApplyBpmnDocuments"/> adds to a model.</summary>
public sealed class BpmnDocumentRow
{
    /// <summary>The tenant; empty for the single, unpartitioned space.</summary>
    public string Tenant { get; set; } = string.Empty;

    /// <summary>The document's logical path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary><c>Xml</c> or <c>Json</c>.</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>The document's bytes.</summary>
    public byte[] Content { get; set; } = [];

    /// <summary>The size of <see cref="Content"/>, recorded beside it so a listing and a size check never load content.</summary>
    public long Size { get; set; }

    /// <summary>The revision: starts at a value taken from the clock when the document is created and goes up by one with every write, whatever the database. A document created again after a delete therefore never reuses a revision.</summary>
    public long Version { get; set; }

    /// <summary>When the document was last written, in UTC.</summary>
    public DateTime ModifiedUtc { get; set; }
}

/// <summary>Adds the document table to an Entity Framework Core model.</summary>
public static class BpmnDocumentModelBuilderExtensions
{
    /// <summary>Maps <see cref="BpmnDocumentRow"/>.</summary>
    /// <param name="modelBuilder">The model of the host's context.</param>
    /// <param name="table">The table's name.</param>
    /// <param name="schema">The table's schema; the provider's default when null.</param>
    /// <param name="pathCollation">
    /// An ordinal (binary) collation for the path column where the provider's default is not one, such as
    /// <c>Latin1_General_100_BIN2</c> on SQL Server, so that case-sensitivity and folder ranges match the store contract.
    /// </param>
    /// <returns>The same model builder for chaining.</returns>
    public static ModelBuilder ApplyBpmnDocuments(this ModelBuilder modelBuilder, string table = "BpmnDocuments", string? schema = null, string? pathCollation = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<BpmnDocumentRow>(entity =>
        {
            entity.ToTable(table, schema);
            entity.HasKey(row => new { row.Tenant, row.Path });
            entity.Property(row => row.Tenant).HasMaxLength(128);
            var path = entity.Property(row => row.Path).HasMaxLength(BpmnDocumentPath.MaxLength);
            if (pathCollation is not null) path.UseCollation(pathCollation);
            entity.Property(row => row.Format).HasMaxLength(8);
            entity.Property(row => row.Version).IsConcurrencyToken();
        });

        return modelBuilder;
    }
}

/// <summary>
/// A document store over any Entity Framework Core database: one table, with the version column as the revision.
/// </summary>
/// <remarks>
/// <para>
/// The compare-and-write is a single <c>UPDATE … WHERE Version = @held</c>, so it is atomic in every database, and a create is an
/// insert that the primary key refuses when the document exists. Neither reads first and writes after.
/// </para>
/// <para>
/// A folder is a range of paths, never a <c>LIKE</c> over text the caller chose, so <c>%</c> and <c>_</c> in a name cannot widen a
/// query. That needs the path column to compare ordinally; see <see cref="BpmnDocumentModelBuilderExtensions.ApplyBpmnDocuments"/>.
/// A listing reads only paths, sizes and times, and builds folders in memory, so it is bounded by <see cref="MaxListEntries"/>:
/// a folder with more documents than that must be listed by narrowing it, with <see cref="BpmnDocumentQuery.NamePrefix"/> or a subfolder.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The host's context, which called <c>ApplyBpmnDocuments</c>.</typeparam>
public sealed class EfCoreBpmnDocumentStore<TContext> : IBpmnDocumentStore
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contexts;
    private readonly TimeProvider _time;

    /// <summary>Creates a store.</summary>
    /// <param name="contexts">Makes a context per operation, so the store keeps no state of its own.</param>
    /// <param name="timeProvider">The clock for modification times; the system clock when null.</param>
    public EfCoreBpmnDocumentStore(IDbContextFactory<TContext> contexts, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(contexts);

        _contexts = contexts;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The most documents one listing considers.</summary>
    public int MaxListEntries { get; init; } = 100_000;

    /// <inheritdoc />
    public async ValueTask<BpmnStoredDocument?> OpenAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default)
    {
        var (tenant, path) = Parts(key);
        await using var context = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var row = await context.Set<BpmnDocumentRow>().AsNoTracking()
            .Where(candidate => candidate.Tenant == tenant && candidate.Path == path)
            .Select(candidate => new { candidate.Content, candidate.Format, candidate.Version, candidate.ModifiedUtc })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return row is null
            ? null
            : new BpmnStoredDocument(new BpmnDocumentContent(row.Content, FormatOf(row.Format)), Revision(row.Version), Modified(row.ModifiedUtc));
    }

    /// <inheritdoc />
    public async ValueTask<BpmnDocumentStat?> StatAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default)
    {
        var (tenant, path) = Parts(key);
        await using var context = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var row = await context.Set<BpmnDocumentRow>().AsNoTracking()
            .Where(candidate => candidate.Tenant == tenant && candidate.Path == path)
            .Select(candidate => new { candidate.Size, candidate.Version, candidate.ModifiedUtc })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        return row is null ? null : new BpmnDocumentStat(Revision(row.Version), row.Size, Modified(row.ModifiedUtc));
    }

    /// <inheritdoc />
    public async ValueTask<BpmnStoreResult> SaveAsync(
        BpmnDocumentKey key,
        BpmnDocumentContent content,
        string? ifMatchRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var (tenant, path) = Parts(key);
        var bytes = content.Bytes.ToArray();
        var now = _time.GetUtcNow().UtcDateTime;
        var first = now.Ticks;
        await using var context = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        if (ifMatchRevision is null)
        {
            // A create: the primary key is what says "only if absent", atomically, in every database.
            context.Set<BpmnDocumentRow>().Add(new BpmnDocumentRow
            {
                Tenant = tenant,
                Path = path,
                Format = content.Format.ToString(),
                Content = bytes,
                Size = bytes.LongLength,
                Version = first,
                ModifiedUtc = now,
            });

            try
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return new BpmnStoreResult(BpmnStoreStatus.Succeeded, Revision(first), Modified(now));
            }
            catch (DbUpdateException)
            {
                // Either the document exists, which is a conflict, or the database is failing, which is not ours to hide.
                context.ChangeTracker.Clear();
                if (await VersionOfAsync(context, tenant, path, cancellationToken).ConfigureAwait(false) is { } existing) return new BpmnStoreResult(BpmnStoreStatus.Conflict, Revision(existing));

                throw;
            }
        }

        if (!TryVersion(ifMatchRevision, out var held))
        {
            // A revision this store never gave out matches nothing.
            return await RefusedAsync(context, tenant, path, cancellationToken).ConfigureAwait(false);
        }

        var updated = await context.Set<BpmnDocumentRow>()
            .Where(candidate => candidate.Tenant == tenant && candidate.Path == path && candidate.Version == held)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(row => row.Content, bytes)
                    .SetProperty(row => row.Format, content.Format.ToString())
                    .SetProperty(row => row.Size, bytes.LongLength)
                    .SetProperty(row => row.Version, held + 1)
                    .SetProperty(row => row.ModifiedUtc, now),
                cancellationToken).ConfigureAwait(false);

        return updated == 1
            ? new BpmnStoreResult(BpmnStoreStatus.Succeeded, Revision(held + 1), Modified(now))
            : await RefusedAsync(context, tenant, path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<BpmnStoreResult> DeleteAsync(
        BpmnDocumentKey key,
        string? ifMatchRevision,
        CancellationToken cancellationToken = default)
    {
        var (tenant, path) = Parts(key);
        await using var context = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        long held = 0;
        if (ifMatchRevision is not null && !TryVersion(ifMatchRevision, out held)) return await RefusedAsync(context, tenant, path, cancellationToken).ConfigureAwait(false);

        var matching = context.Set<BpmnDocumentRow>().Where(candidate => candidate.Tenant == tenant && candidate.Path == path);
        var deleted = await (ifMatchRevision is null ? matching : matching.Where(candidate => candidate.Version == held))
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        return deleted == 1
            ? new BpmnStoreResult(BpmnStoreStatus.Succeeded)
            : await RefusedAsync(context, tenant, path, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<BpmnDocumentPage> ListAsync(BpmnDocumentQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = TenantOf(query.Tenant);
        await using var context = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var rows = context.Set<BpmnDocumentRow>().AsNoTracking().Where(candidate => candidate.Tenant == tenant);
        if (!query.Folder.IsRoot)
        {
            // Everything under "a/b" sorts between "a/b/" and "a/b0" ('/' is followed by '0' in ordinal order), whatever the names are.
            var low = query.Folder + "/";
            var high = query.Folder + "0";
            rows = rows.Where(candidate => candidate.Path.CompareTo(low) >= 0 && candidate.Path.CompareTo(high) < 0);
        }

        var found = await rows.OrderBy(candidate => candidate.Path)
            .Select(candidate => new { candidate.Path, candidate.Size, candidate.ModifiedUtc })
            .Take(MaxListEntries + 1)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (found.Count > MaxListEntries)
        {
            throw new InvalidOperationException($"More than {MaxListEntries.ToString(CultureInfo.InvariantCulture)} documents are under that folder; list a narrower folder or give a name prefix.");
        }

        var entries = new List<BpmnDocumentEntry>();
        var folders = new HashSet<BpmnDocumentPath>();
        foreach (var row in found)
        {
            if (!BpmnDocumentPath.TryParse(row.Path, out var path)) continue;

            // A path column that does not compare ordinally (the default collation of SQL Server is not) can put a row in the range that is not under the
            // folder: "A/B/x" under "a/b". It is not one of this folder's documents, and walking up from it never reaches the folder.
            if (!path.IsUnder(query.Folder)) continue;

            if (query.Recursive || path.Parent == query.Folder)
            {
                entries.Add(new BpmnDocumentEntry(path, IsFolder: false, row.Size, Modified(row.ModifiedUtc)));
                continue;
            }

            // A document deeper down makes the sub-folder on its way visible at this level.
            var folder = path;
            while (folder.Parent != query.Folder) folder = folder.Parent;
            if (folders.Add(folder)) entries.Add(new BpmnDocumentEntry(folder, IsFolder: true));
        }

        return BpmnDocumentPaging.Page(entries, query);
    }

    private static (string Tenant, string Path) Parts(BpmnDocumentKey key)
    {
        if (!key.IsValid) throw new ArgumentException("The key does not name a document.", nameof(key));

        return (TenantOf(key.Tenant), key.Path.ToString());
    }

    private static string TenantOf(string? tenant)
    {
        if (tenant is null) return string.Empty;

        if (tenant.Length == 0 || tenant.Contains('/', StringComparison.Ordinal) || !BpmnDocumentPath.TryParse(tenant, out _))
        {
            throw new ArgumentException("A tenant must be a single valid path segment.", nameof(tenant));
        }

        return tenant;
    }

    private static string Revision(long version) => version.ToString(CultureInfo.InvariantCulture);

    private static bool TryVersion(string revision, out long version) => long.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out version);

    private static DateTimeOffset Modified(DateTime utc) => new(DateTime.SpecifyKind(utc, DateTimeKind.Utc));

    private static BpmnFileFormat FormatOf(string format) => Enum.TryParse<BpmnFileFormat>(format, out var parsed) ? parsed : BpmnFileFormat.Xml;

    private static async Task<long?> VersionOfAsync(DbContext context, string tenant, string path, CancellationToken cancellationToken) =>
        await context.Set<BpmnDocumentRow>().AsNoTracking()
            .Where(candidate => candidate.Tenant == tenant && candidate.Path == path)
            .Select(candidate => (long?)candidate.Version)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    // Why a conditional write did nothing: there is no document, or it is at a revision the caller does not hold.
    private static async Task<BpmnStoreResult> RefusedAsync(DbContext context, string tenant, string path, CancellationToken cancellationToken) =>
        await VersionOfAsync(context, tenant, path, cancellationToken).ConfigureAwait(false) is { } current
            ? new BpmnStoreResult(BpmnStoreStatus.Conflict, Revision(current))
            : new BpmnStoreResult(BpmnStoreStatus.NotFound);
}
