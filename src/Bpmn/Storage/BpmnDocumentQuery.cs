namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>Which documents and folders to list.</summary>
/// <param name="Tenant">The tenant to list; see <see cref="BpmnDocumentKey.Tenant"/>.</param>
/// <param name="Folder">The folder to list; the root by default.</param>
/// <param name="Recursive">
/// False lists the folder's documents and its immediate sub-folders; true lists every document beneath
/// it and no folders.
/// </param>
/// <param name="NamePrefix">When set, only entries whose name starts with it, compared ordinally.</param>
/// <param name="Cursor">The <see cref="BpmnDocumentPage.NextCursor"/> of the previous page, or null for the first.</param>
/// <param name="Limit">The most entries to return; capped at <see cref="MaxLimit"/>.</param>
public sealed record BpmnDocumentQuery(
    string? Tenant,
    BpmnDocumentPath Folder = default,
    bool Recursive = false,
    string? NamePrefix = null,
    string? Cursor = null,
    int Limit = BpmnDocumentQuery.DefaultLimit)
{
    /// <summary>The page size when none is given.</summary>
    public const int DefaultLimit = 100;

    /// <summary>The largest page any store returns, whatever the caller asked for.</summary>
    public const int MaxLimit = 1000;

    /// <summary>The page size actually used: <see cref="Limit"/> clamped to 1 through <see cref="MaxLimit"/>.</summary>
    public int EffectiveLimit => Math.Clamp(Limit, 1, MaxLimit);
}

/// <summary>One listed document or folder.</summary>
/// <param name="Path">The full path.</param>
/// <param name="IsFolder">Whether this is a folder, which exists while any document lies beneath it.</param>
/// <param name="Size">The document's size in bytes; null for a folder.</param>
/// <param name="Modified">When the document was last written; null for a folder.</param>
public sealed record BpmnDocumentEntry(BpmnDocumentPath Path, bool IsFolder, long? Size = null, DateTimeOffset? Modified = null);

/// <summary>One page of a listing.</summary>
/// <remarks>
/// Within a level, folders come before documents, each in ordinal order; a recursive listing is
/// documents only, in ordinal order. Listings never contain content or revisions.
/// </remarks>
/// <param name="Entries">The entries on this page.</param>
/// <param name="NextCursor">An opaque token for the next page, or null when this is the last.</param>
public sealed record BpmnDocumentPage(IReadOnlyList<BpmnDocumentEntry> Entries, string? NextCursor);
