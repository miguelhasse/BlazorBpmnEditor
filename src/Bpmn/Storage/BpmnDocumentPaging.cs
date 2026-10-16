using System.Buffers.Text;
using System.Text;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>Orders and pages a folder listing the way every store must, so that all of them agree.</summary>
/// <remarks>
/// A store gathers the entries that match a <see cref="BpmnDocumentQuery"/> (the documents and implicit
/// sub-folders of a folder, or every document beneath it) and hands them here. Sub-folders come first, then
/// documents, each in ordinal path order. The cursor is the sort position of the last entry returned, so a page
/// stays correct when entries are added or removed between calls; it is encoded only so callers treat it as opaque.
/// </remarks>
public static class BpmnDocumentPaging
{
    /// <summary>Applies the name prefix, the order, the cursor and the page size.</summary>
    /// <param name="entries">Every entry that is in the folder, before the prefix, cursor and limit.</param>
    /// <param name="query">The query.</param>
    /// <returns>One page, and the cursor for the next when there is more.</returns>
    /// <exception cref="ArgumentException">The cursor was not produced by a store.</exception>
    public static BpmnDocumentPage Page(IEnumerable<BpmnDocumentEntry> entries, BpmnDocumentQuery query)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(query);

        var after = DecodeCursor(query.Cursor);
        var ordered = entries
            .Where(entry => query.NamePrefix is null || entry.Path.Name.StartsWith(query.NamePrefix, StringComparison.Ordinal))
            .OrderBy(entry => entry.IsFolder ? 0 : 1)
            .ThenBy(entry => entry.Path)
            .Where(entry => after is null || Compare(entry, after.Value) > 0)
            .Take(query.EffectiveLimit + 1)
            .ToList();

        var hasMore = ordered.Count > query.EffectiveLimit;
        if (hasMore) ordered.RemoveAt(ordered.Count - 1);

        return new BpmnDocumentPage(ordered, hasMore ? EncodeCursor(ordered[^1]) : null);
    }

    private static int Compare(BpmnDocumentEntry entry, (bool IsFolder, BpmnDocumentPath Path) after)
    {
        var kind = (entry.IsFolder ? 0 : 1).CompareTo(after.IsFolder ? 0 : 1);
        return kind != 0 ? kind : entry.Path.CompareTo(after.Path);
    }

    private static string EncodeCursor(BpmnDocumentEntry entry) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes((entry.IsFolder ? "f:" : "d:") + entry.Path));

    private static (bool IsFolder, BpmnDocumentPath Path)? DecodeCursor(string? cursor)
    {
        if (cursor is null) return null;

        string text;
        try
        {
            text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        }
        catch (FormatException)
        {
            throw new ArgumentException("The cursor was not produced by this store.", nameof(cursor));
        }

        if (text.Length < 3 || text[1] != ':' || text[0] is not ('f' or 'd') || !BpmnDocumentPath.TryParse(text[2..], out var path))
        {
            throw new ArgumentException("The cursor was not produced by this store.", nameof(cursor));
        }

        return (text[0] == 'f', path);
    }
}
