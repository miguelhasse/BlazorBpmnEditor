namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>
/// Where documents are kept: a file share, blob container, database table or memory, behind one
/// contract that the editor, agent tools and hosts all use.
/// </summary>
/// <remarks>
/// <para>
/// Implementations deal in bytes, logical paths and opaque revisions, never in parsed documents or
/// backend locations. Every write is conditional: a save or delete names the revision it expects, and
/// the store makes that compare-and-write atomic with the backend's own primitive (a conditional blob
/// header, a row version), never a read followed by a write in application code. A mismatch is a
/// <see cref="BpmnStoreStatus.Conflict"/> result, never an exception and never an overwrite.
/// </para>
/// <para>
/// Implementations apply <see cref="BpmnDocumentKey.Tenant"/> themselves, refuse
/// <see langword="default"/> keys with <see cref="ArgumentException"/>, and reserve exceptions for
/// failures of the backend itself.
/// </para>
/// </remarks>
public interface IBpmnDocumentStore
{
    /// <summary>Reads a document.</summary>
    /// <param name="key">The document.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The content and revision, or null when there is no such document.</returns>
    public ValueTask<BpmnStoredDocument?> OpenAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default);

    /// <summary>Reads a document's revision, size and modification time without its content.</summary>
    /// <param name="key">The document.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The metadata, or null when there is no such document.</returns>
    public ValueTask<BpmnDocumentStat?> StatAsync(BpmnDocumentKey key, CancellationToken cancellationToken = default);

    /// <summary>Writes a document, if it is still at the revision the caller expects.</summary>
    /// <param name="key">The document.</param>
    /// <param name="content">The new content.</param>
    /// <param name="ifMatchRevision">
    /// The revision the caller holds, or null to create a document that must not exist yet.
    /// </param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// <see cref="BpmnStoreStatus.Succeeded"/> with the new revision;
    /// <see cref="BpmnStoreStatus.Conflict"/> with the current revision when it differs or a create found
    /// a document; <see cref="BpmnStoreStatus.NotFound"/> when an update found no document.
    /// </returns>
    public ValueTask<BpmnStoreResult> SaveAsync(
        BpmnDocumentKey key,
        BpmnDocumentContent content,
        string? ifMatchRevision,
        CancellationToken cancellationToken = default);

    /// <summary>Lists documents and folders; see <see cref="BpmnDocumentQuery"/>.</summary>
    /// <param name="query">What to list.</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>One page of entries.</returns>
    public ValueTask<BpmnDocumentPage> ListAsync(BpmnDocumentQuery query, CancellationToken cancellationToken = default);

    /// <summary>Deletes a document, if it is still at the revision the caller expects.</summary>
    /// <param name="key">The document.</param>
    /// <param name="ifMatchRevision">The revision the caller holds, or null to delete whatever is there.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>
    /// <see cref="BpmnStoreStatus.Succeeded"/>, <see cref="BpmnStoreStatus.Conflict"/> with the current
    /// revision, or <see cref="BpmnStoreStatus.NotFound"/>.
    /// </returns>
    public ValueTask<BpmnStoreResult> DeleteAsync(
        BpmnDocumentKey key,
        string? ifMatchRevision,
        CancellationToken cancellationToken = default);
}
