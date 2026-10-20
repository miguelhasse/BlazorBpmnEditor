using System.Globalization;
using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Storage;

namespace SqlServerChangeIngress;

/// <summary>One change to the documents table, as the ingress forwards it.</summary>
/// <param name="Key">The document.</param>
/// <param name="Revision">The row's version, as the EF Core store reports it; null for a deletion.</param>
/// <param name="Deleted">Whether the row was deleted.</param>
public sealed record SqlServerDocumentChange(BpmnDocumentKey Key, string? Revision, bool Deleted);

/// <summary>
/// Turns what SQL Server reports about the documents table into changes: the JSON the trigger in <c>service-broker.sql</c> sends, and the rows
/// <c>CHANGETABLE(CHANGES …)</c> returns. Both use Change Tracking's operation letters: <c>I</c>, <c>U</c> and <c>D</c>.
/// </summary>
public static class SqlServerChangeParser
{
    /// <summary>Reads a Service Broker message: <c>[{ "tenant": "acme" | "", "path": "claims/a.bpmn", "version": 3 | null, "op": "I" | "U" | "D" }, …]</c>.</summary>
    /// <param name="payload">The message body.</param>
    /// <returns>The changes it describes; an entry that is not a document is left out, and a body that is not such an array gives none.</returns>
    public static IReadOnlyList<SqlServerDocumentChange> ParseMessage(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return [];

            var changes = new List<SqlServerDocumentChange>(root.GetArrayLength());
            foreach (var entry in root.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;

                var tenant = ReadString(entry, "tenant");
                var path = ReadString(entry, "path");
                var operation = ReadString(entry, "op");
                long? version = entry.TryGetProperty("version", out var versionElement) && versionElement.ValueKind == JsonValueKind.Number && versionElement.TryGetInt64(out var number) ? number : null;

                if (FromRow(tenant, path, version, operation) is { } change) changes.Add(change);
            }

            return changes;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Reads one changed row.</summary>
    /// <param name="tenant">The row's tenant; empty or null for the single, unpartitioned space.</param>
    /// <param name="path">The row's path.</param>
    /// <param name="version">The row's version; null when the row no longer exists.</param>
    /// <param name="operation"><c>I</c>, <c>U</c> or <c>D</c>.</param>
    /// <returns>The change; null when the row is not a document the store would accept, or the operation is unknown.</returns>
    public static SqlServerDocumentChange? FromRow(string? tenant, string? path, long? version, string? operation)
    {
        if (string.IsNullOrEmpty(tenant)) tenant = null;

        // A name this store would not accept is not a document, whatever the table holds.
        if (!BpmnDocumentPath.TryParse(path, out var documentPath) || !documentPath.IsDocumentName) return null;
        if (tenant is not null && (tenant.Contains('/', StringComparison.Ordinal) || !BpmnDocumentPath.TryParse(tenant, out _))) return null;

        var key = new BpmnDocumentKey(tenant, documentPath);
        return operation switch
        {
            "D" => new SqlServerDocumentChange(key, Revision: null, Deleted: true),

            // An insert or update whose row is already gone has been deleted since; that is what is true now.
            "I" or "U" when version is null => new SqlServerDocumentChange(key, Revision: null, Deleted: true),
            "I" or "U" => new SqlServerDocumentChange(key, version.Value.ToString(CultureInfo.InvariantCulture), Deleted: false),
            _ => null,
        };
    }

    /// <summary>Forwards a change through the ingress.</summary>
    /// <param name="ingress">The ingress.</param>
    /// <param name="change">The change.</param>
    /// <param name="cancellationToken">Cancels the publication.</param>
    /// <returns>True when it was published; false when it repeated what was last announced for the document.</returns>
    public static ValueTask<bool> ForwardAsync(this BpmnDocumentChangeIngress ingress, SqlServerDocumentChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ingress);
        ArgumentNullException.ThrowIfNull(change);

        return change.Deleted
            ? ingress.PublishDeletedAsync(change.Key, cancellationToken)
            : ingress.PublishUpdatedAsync(change.Key, change.Revision, cancellationToken);
    }

    private static string? ReadString(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString() : null;
}
