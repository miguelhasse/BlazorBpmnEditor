using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>What a live-update transport puts on the wire for one change: the path, never the tenant, and never content.</summary>
/// <remarks>
/// The tenant is the caller's, taken from their credentials on the server and from their own key on the client, so
/// no message can name a tenant. Both transports and both clients use this one shape.
/// </remarks>
/// <param name="Path">The document's logical path.</param>
/// <param name="Kind"><c>updated</c> or <c>deleted</c>.</param>
/// <param name="Revision">The new revision; absent for a deletion.</param>
/// <param name="Origin">Who wrote it, a hint for echo suppression.</param>
/// <param name="OccurredAt">When.</param>
public sealed record BpmnDocumentChangeWire(string Path, string Kind, string? Revision, string? Origin, DateTimeOffset OccurredAt)
{
    /// <summary>Turns a change into its wire form.</summary>
    /// <param name="change">The change.</param>
    /// <returns>What goes on the wire.</returns>
    public static BpmnDocumentChangeWire From(BpmnDocumentChange change)
    {
        ArgumentNullException.ThrowIfNull(change);

        return new BpmnDocumentChangeWire(
            change.Key.Path.ToString(),
            change.Kind == BpmnDocumentChangeKind.Deleted ? "deleted" : "updated",
            change.Revision,
            change.Origin,
            change.OccurredAt);
    }

    /// <summary>Reads JSON text received from a transport.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The message, or null when it is not one.</returns>
    public static BpmnDocumentChangeWire? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, BpmnChangeWireJsonContext.Default.BpmnDocumentChangeWire);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes the message as JSON text.</summary>
    /// <returns>The JSON.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, BpmnChangeWireJsonContext.Default.BpmnDocumentChangeWire);

    /// <summary>Gives the change a client acts on, for the document it subscribed to.</summary>
    /// <param name="key">The document the client subscribed to; its tenant is the caller's own.</param>
    /// <returns>The change, or null when the message names a different document or is not well formed.</returns>
    public BpmnDocumentChange? ToChange(BpmnDocumentKey key)
    {
        if (!BpmnDocumentPath.TryParse(Path, out var path) || path != key.Path) return null;

        var kind = Kind switch
        {
            "updated" => BpmnDocumentChangeKind.Updated,
            "deleted" => BpmnDocumentChangeKind.Deleted,
            _ => (BpmnDocumentChangeKind?)null,
        };

        return kind is null ? null : new BpmnDocumentChange(key, kind.Value, Revision, Origin, OccurredAt);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BpmnDocumentChangeWire))]
internal sealed partial class BpmnChangeWireJsonContext : JsonSerializerContext;
