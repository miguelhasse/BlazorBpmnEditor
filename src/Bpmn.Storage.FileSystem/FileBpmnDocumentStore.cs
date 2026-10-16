using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.FileSystem;

/// <summary>A document store over a folder of <c>.bpmn</c>, <c>.xml</c> and <c>.json</c> files.</summary>
/// <remarks>
/// <para>
/// A logical path is parsed by <see cref="BpmnDocumentPath"/>, mapped under the root, and checked again: the
/// full path must stay inside the root and no folder or file on the way may be a symbolic link or junction.
/// Tenants live in <c>&lt;root&gt;/~tenants/&lt;tenant&gt;</c>; <c>~</c> cannot appear in a document path, so a tenant folder
/// cannot collide with a document folder.
/// </para>
/// <para>
/// A save writes a temporary file beside the target and moves it over the target in one step, under a lock that
/// works across processes, so the revision comparison and the replacement are one atomic action. A revision is a
/// hash of the file's length and last-write time, and a save always moves the last-write time forward.
/// </para>
/// <para>
/// Where the file system ignores case (Windows, macOS) <c>Order.bpmn</c> and <c>order.bpmn</c> are one file: the
/// store never overwrites one with the other, and reports the differently cased name as missing instead of
/// answering with the other document. It does not watch the folder for changes made by other programs.
/// </para>
/// </remarks>
public sealed class FileBpmnDocumentStore : IBpmnDocumentStore
{
    private const string TenantFolder = "~tenants";
    private const string WorkFolder = "~work";
    private const int Stripes = 64;

    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);
    private static readonly bool CaseInsensitive = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    private readonly string _root;
    private readonly SemaphoreSlim[] _gates = [.. Enumerable.Range(0, Stripes).Select(_ => new SemaphoreSlim(1, 1))];
    private readonly TimeProvider _time;

    /// <summary>Creates a store.</summary>
    /// <param name="root">The folder that holds the documents. It is created on the first save.</param>
    /// <param name="timeProvider">The clock for modification times; the system clock when null.</param>
    public FileBpmnDocumentStore(string root, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async ValueTask<BpmnStoredDocument?> OpenAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default)
    {
        var location = Locate(key);
        cancellationToken.ThrowIfCancellationRequested();
        if (Inspect(location.Components, expectDirectory: false, out var full) != Presence.Present) return null;

        FileStream stream;
        try
        {
            stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        await using (stream.ConfigureAwait(false))
        {
            if (stream.Length > int.MaxValue) throw new IOException("The document is too large to open.");

            // Length and time come from the handle that is read, so the revision describes exactly these bytes.
            var modified = File.GetLastWriteTimeUtc(stream.SafeFileHandle);
            var bytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);

            return new BpmnStoredDocument(
                new BpmnDocumentContent(bytes, BpmnFileFormats.Detect(key.Path.Name, content: null)),
                RevisionOf(bytes.Length, modified),
                new DateTimeOffset(modified, TimeSpan.Zero));
        }
    }

    /// <inheritdoc />
    public ValueTask<BpmnDocumentStat?> StatAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default)
    {
        var location = Locate(key);
        cancellationToken.ThrowIfCancellationRequested();
        if (Inspect(location.Components, expectDirectory: false, out var full) != Presence.Present) return ValueTask.FromResult<BpmnDocumentStat?>(null);

        var info = new FileInfo(full);
        return ValueTask.FromResult<BpmnDocumentStat?>(info.Exists
            ? new BpmnDocumentStat(RevisionOf(info.Length, info.LastWriteTimeUtc), info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero))
            : null);
    }

    /// <inheritdoc />
    public async ValueTask<BpmnStoreResult> SaveAsync(
        BpmnDocumentKey key,
        BpmnDocumentContent content,
        string? ifMatchRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var location = Locate(key);

        await using var held = await LockAsync(location.LockKey, cancellationToken).ConfigureAwait(false);

        var presence = Inspect(location.Components, expectDirectory: false, out var full);
        RefuseLinks(presence);

        // A differently cased name is a document that is not ours to overwrite, and a file or folder in the way is not a document either:
        // both are answered like a document that exists and is not the one the caller meant, not with an exception from the file system.
        if (presence is Presence.CaseMismatch or Presence.Blocked)
        {
            return ifMatchRevision is null ? Result(BpmnStoreStatus.Conflict) : Result(BpmnStoreStatus.NotFound);
        }

        var current = presence == Presence.Missing ? null : new FileInfo(full);
        if (ifMatchRevision is null)
        {
            if (current is not null) return Result(BpmnStoreStatus.Conflict, Revision(current));
        }
        else
        {
            if (presence != Presence.Present || current is null) return Result(BpmnStoreStatus.NotFound);
            if (!string.Equals(Revision(current), ifMatchRevision, StringComparison.Ordinal)) return Result(BpmnStoreStatus.Conflict, Revision(current));
        }

        var directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, content.Bytes.ToArray(), cancellationToken).ConfigureAwait(false);

            // A revision must never repeat for different content, whatever the file system's clock resolution.
            var previous = current is null ? (DateTime?)null : current.LastWriteTimeUtc;
            var stamp = _time.GetUtcNow().UtcDateTime;
            if (previous is { } before && stamp <= before) stamp = before.AddTicks(1);
            File.SetLastWriteTimeUtc(temp, stamp);
            await MoveOverAsync(temp, full, cancellationToken).ConfigureAwait(false);

            var written = new FileInfo(full);
            if (current is not null && string.Equals(Revision(written), Revision(current), StringComparison.Ordinal))
            {
                File.SetLastWriteTimeUtc(full, written.LastWriteTimeUtc.AddSeconds(2));
                written.Refresh();
            }

            return new BpmnStoreResult(BpmnStoreStatus.Succeeded, Revision(written), new DateTimeOffset(written.LastWriteTimeUtc, TimeSpan.Zero));
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <inheritdoc />
    public async ValueTask<BpmnStoreResult> DeleteAsync(
        BpmnDocumentKey key,
        string? ifMatchRevision,
        CancellationToken cancellationToken = default)
    {
        var location = Locate(key);

        await using var held = await LockAsync(location.LockKey, cancellationToken).ConfigureAwait(false);

        var presence = Inspect(location.Components, expectDirectory: false, out var full);
        RefuseLinks(presence);
        if (presence != Presence.Present) return Result(BpmnStoreStatus.NotFound);

        var current = new FileInfo(full);
        if (ifMatchRevision is not null && !string.Equals(Revision(current), ifMatchRevision, StringComparison.Ordinal))
        {
            return Result(BpmnStoreStatus.Conflict, Revision(current));
        }

        current.Delete();
        return Result(BpmnStoreStatus.Succeeded);
    }

    /// <inheritdoc />
    public ValueTask<BpmnDocumentPage> ListAsync(BpmnDocumentQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var components = TenantComponents(query.Tenant).Concat(query.Folder.Segments).ToList();
        var entries = new List<BpmnDocumentEntry>();
        if (Inspect(components, expectDirectory: true, out var directory) == Presence.Present)
        {
            Collect(directory, query.Folder, query.Recursive, entries, cancellationToken);
        }

        return ValueTask.FromResult(BpmnDocumentPaging.Page(entries, query));
    }

    // Windows refuses a replace for a moment while a scanner or indexer holds the target, so a few tries are made.
    private static async Task MoveOverAsync(string temp, string target, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, target, overwrite: true);
                return;
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException && attempt < 10)
            {
                await Task.Delay(attempt * 10, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // --- locating a document ---------------------------------------------------------------------

    private static string Revision(FileInfo info) => RevisionOf(info.Length, info.LastWriteTimeUtc);

    private static string RevisionOf(long length, DateTime lastWriteUtc)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{length}:{lastWriteUtc.Ticks}");
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(text)))[..22];
    }

    private static BpmnStoreResult Result(BpmnStoreStatus status, string? revision = null) => new(status, revision);

    private static void RefuseLinks(Presence presence)
    {
        if (presence == Presence.Link)
        {
            throw new InvalidOperationException("The document's location passes through a symbolic link or junction, which the store does not follow.");
        }
    }

    private static List<string> TenantComponents(string? tenant)
    {
        if (tenant is null) return [];

        if (tenant.Length == 0 || tenant.Contains('/', StringComparison.Ordinal) || !BpmnDocumentPath.TryParse(tenant, out _))
        {
            throw new ArgumentException("A tenant must be a single valid path segment.", nameof(tenant));
        }

        return [TenantFolder, tenant];
    }

    private static (List<string> Components, string LockKey) Locate(BpmnDocumentKey key)
    {
        if (!key.IsValid) throw new ArgumentException("The key does not name a document.", nameof(key));

        var components = TenantComponents(key.Tenant);
        components.AddRange(key.Path.Segments);
        return (components, (key.Tenant ?? string.Empty) + "\0" + key.Path);
    }

    private enum Presence
    {
        Missing,
        Present,
        Link,

        // Something is there under a name that differs only by case, on a file system that ignores it.
        CaseMismatch,

        // Something of the other kind stands at the name or on the way to it: a file where a folder is needed, a folder where a document is.
        // Nothing can be made there, and nothing there is a document.
        Blocked,
    }

    // Walks the existing part of the location one component at a time, because a link anywhere on the way, or a
    // name that matches only by case, must be noticed before anything is read or written.
    private Presence Inspect(List<string> components, bool expectDirectory, out string full)
    {
        var current = _root;
        full = _root;
        if (components.Count == 0)
        {
            return expectDirectory && !Directory.Exists(_root) ? Presence.Missing : Presence.Present;
        }

        for (var index = 0; index < components.Count; index++)
        {
            var next = Path.Combine(current, components[index]);
            full = next;

            var attributes = TryGetAttributes(next);
            if (attributes is null)
            {
                full = Path.Combine([_root, .. components]);
                EnsureInsideRoot(full);
                return Presence.Missing;
            }

            if (attributes.Value.HasFlag(FileAttributes.ReparsePoint)) return Presence.Link;
            if (CaseInsensitive && !EntryNameExists(current, components[index])) return Presence.CaseMismatch;

            var isLast = index == components.Count - 1;
            var isDirectory = attributes.Value.HasFlag(FileAttributes.Directory);
            if (isDirectory != (!isLast || expectDirectory)) return Presence.Blocked;

            current = next;
        }

        EnsureInsideRoot(full);
        return Presence.Present;
    }

    private void EnsureInsideRoot(string full)
    {
        var resolved = Path.GetFullPath(full);
        if (!resolved.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !string.Equals(resolved, _root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The document's location is outside the store's root.");
        }
    }

    private static FileAttributes? TryGetAttributes(string path)
    {
        try
        {
            return File.GetAttributes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool EntryNameExists(string directory, string name) =>
        Directory.EnumerateFileSystemEntries(directory, name).Any(entry => string.Equals(Path.GetFileName(entry), name, StringComparison.Ordinal));

    // --- listing ---------------------------------------------------------------------------------

    private static readonly EnumerationOptions Enumeration = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
        IgnoreInaccessible = true,
        MatchType = MatchType.Simple,
        RecurseSubdirectories = false,
    };

    private static void Collect(string directory, BpmnDocumentPath folder, bool recursive, List<BpmnDocumentEntry> entries, CancellationToken cancellationToken)
    {
        foreach (var info in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", Enumeration))
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Anything that is not a valid path segment (the store's own folders, odd names) is not a document.
            if (!BpmnDocumentPath.TryParse(info.Name, out _)) continue;

            if (info is DirectoryInfo sub)
            {
                if (folder.Depth + 1 >= BpmnDocumentPath.MaxSegments) continue;

                var subFolder = folder.Combine(info.Name);
                if (recursive) Collect(sub.FullName, subFolder, recursive, entries, cancellationToken);
                else if (HoldsDocument(sub.FullName, subFolder, cancellationToken)) entries.Add(new BpmnDocumentEntry(subFolder, IsFolder: true));

                continue;
            }

            var path = folder.Combine(info.Name);
            if (!path.IsDocumentName || info is not FileInfo file) continue;

            entries.Add(new BpmnDocumentEntry(path, IsFolder: false, file.Length, new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)));
        }
    }

    private static bool HoldsDocument(string directory, BpmnDocumentPath folder, CancellationToken cancellationToken)
    {
        foreach (var info in new DirectoryInfo(directory).EnumerateFileSystemInfos("*", Enumeration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!BpmnDocumentPath.TryParse(info.Name, out _)) continue;

            if (info is DirectoryInfo sub)
            {
                if (folder.Depth + 1 < BpmnDocumentPath.MaxSegments && HoldsDocument(sub.FullName, folder.Combine(info.Name), cancellationToken)) return true;
            }
            else if (folder.Combine(info.Name).IsDocumentName)
            {
                return true;
            }
        }

        return false;
    }

    // --- locking ---------------------------------------------------------------------------------

    private async ValueTask<LockRelease> LockAsync(string lockKey, CancellationToken cancellationToken)
    {
        // One of a fixed number of lock files, picked by the document: bounded, and still exclusive per document.
        // On a file system that ignores case, "Order.bpmn" and "order.bpmn" are one file and must share a lock, or two creates of "different" documents run side by side.
        var spelled = CaseInsensitive ? lockKey.ToUpperInvariant() : lockKey;
        var stripe = (int)(BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(spelled)), 0) % Stripes);

        await _gates[stripe].WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await AcquireFileLockAsync(stripe, cancellationToken).ConfigureAwait(false);
            return new LockRelease(_gates[stripe], file);
        }
        catch
        {
            _gates[stripe].Release();
            throw;
        }
    }

    private async Task<FileStream> AcquireFileLockAsync(int stripe, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_root, WorkFolder);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"lock-{stripe.ToString("D2", CultureInfo.InvariantCulture)}");

        var started = _time.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
            }
            catch (IOException) when (_time.GetElapsedTime(started) < LockTimeout)
            {
                await Task.Delay(Random.Shared.Next(5, 25), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private readonly struct LockRelease(SemaphoreSlim gate, FileStream file) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await file.DisposeAsync().ConfigureAwait(false);
            gate.Release();
        }
    }
}
