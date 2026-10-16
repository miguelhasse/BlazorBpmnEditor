using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>A document's stored bytes and the format they are in.</summary>
/// <remarks>
/// Stores deal in bytes, not in parsed documents: parsing and writing stay in
/// <see cref="BpmnDocumentStoreExtensions"/>, so every backend is a thin byte store and import
/// diagnostics behave identically whichever store a document came from.
/// </remarks>
/// <param name="Bytes">The content, UTF-8 encoded.</param>
/// <param name="Format">Whether the content is BPMN XML or the JSON interchange format.</param>
public sealed record BpmnDocumentContent(ReadOnlyMemory<byte> Bytes, BpmnFileFormat Format);

/// <summary>A stored document and the revision it was read at.</summary>
/// <param name="Content">The stored content.</param>
/// <param name="Revision">
/// An opaque token that changes whenever the stored content changes. Compare it only for equality;
/// it is a blob ETag, a row version or a counter, never a number to order by.
/// </param>
/// <param name="Modified">When the content was last written.</param>
public sealed record BpmnStoredDocument(BpmnDocumentContent Content, string Revision, DateTimeOffset Modified);

/// <summary>What a store knows about a document without reading its content.</summary>
/// <param name="Revision">The current revision; see <see cref="BpmnStoredDocument.Revision"/>.</param>
/// <param name="Size">The content's length in bytes, so oversize documents can be refused before download.</param>
/// <param name="Modified">When the content was last written.</param>
public sealed record BpmnDocumentStat(string Revision, long Size, DateTimeOffset Modified);

/// <summary>How a conditional write or delete ended.</summary>
public enum BpmnStoreStatus
{
    /// <summary>The write or delete happened.</summary>
    Succeeded,

    /// <summary>
    /// The document's revision was not the one the caller held, or a create-only save found a document
    /// already there. Nothing was written.
    /// </summary>
    Conflict,

    /// <summary>The caller expected a document that does not exist. Nothing was written.</summary>
    NotFound,
}

/// <summary>The outcome of <see cref="IBpmnDocumentStore.SaveAsync"/> or <see cref="IBpmnDocumentStore.DeleteAsync"/>.</summary>
/// <param name="Status">How the operation ended.</param>
/// <param name="Revision">
/// After a successful save, the new revision. After a conflict, the document's current revision, or
/// <see langword="null"/> when it no longer exists. Otherwise <see langword="null"/>.
/// </param>
/// <param name="Modified">After a successful save, when it was written.</param>
public sealed record BpmnStoreResult(BpmnStoreStatus Status, string? Revision = null, DateTimeOffset? Modified = null)
{
    /// <summary>Whether the operation happened.</summary>
    public bool Succeeded => Status == BpmnStoreStatus.Succeeded;
}
