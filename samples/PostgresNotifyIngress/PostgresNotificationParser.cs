using System.Globalization;
using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Storage;

namespace PostgresNotifyIngress;

/// <summary>What a notification from the documents table asks of the ingress.</summary>
/// <param name="Key">The document, or null when the payload is not one.</param>
/// <param name="Revision">The row's version, as the EF Core store reports it.</param>
/// <param name="Deleted">Whether the row was deleted.</param>
public sealed record DocumentNotification(BpmnDocumentKey? Key, string? Revision, bool Deleted);

/// <summary>Reads the JSON the trigger in <c>trigger.sql</c> sends with <c>NOTIFY bpmn_documents</c>.</summary>
public static class PostgresNotificationParser
{
    /// <summary>Reads a payload: <c>{ "tenant": "acme" | "", "path": "claims/a.bpmn", "version": 3, "op": "UPDATE" }</c>.</summary>
    /// <param name="payload">The notification's payload.</param>
    /// <returns>What it says; a payload that is not one gives a notification with no key.</returns>
    public static DocumentNotification Parse(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("path", out var pathElement)) return new DocumentNotification(null, null, false);

            var tenant = root.TryGetProperty("tenant", out var tenantElement) && tenantElement.ValueKind == JsonValueKind.String ? tenantElement.GetString() : null;
            if (string.IsNullOrEmpty(tenant)) tenant = null;

            // A name this store would not accept is not a document, whatever the table holds.
            if (!BpmnDocumentPath.TryParse(pathElement.GetString(), out var path) || !path.IsDocumentName) return new DocumentNotification(null, null, false);
            if (tenant is not null && (tenant.Contains('/', StringComparison.Ordinal) || !BpmnDocumentPath.TryParse(tenant, out _))) return new DocumentNotification(null, null, false);

            var deleted = root.TryGetProperty("op", out var op) && string.Equals(op.GetString(), "DELETE", StringComparison.Ordinal);
            string? revision = root.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Number
                ? version.GetInt64().ToString(CultureInfo.InvariantCulture)
                : null;

            return new DocumentNotification(new BpmnDocumentKey(tenant, path), revision, deleted);
        }
        catch (JsonException)
        {
            return new DocumentNotification(null, null, false);
        }
    }
}
