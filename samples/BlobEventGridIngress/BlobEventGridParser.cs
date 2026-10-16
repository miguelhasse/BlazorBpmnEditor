using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Hasseware.AspNetCore.Components.Bpmn.Storage.AzureBlob;

namespace BlobEventGridIngress;

/// <summary>What an Event Grid delivery asks of the ingress.</summary>
public enum BlobEventKind
{
    /// <summary>The subscription handshake: answer with the validation code.</summary>
    Validation,

    /// <summary>A blob was created or replaced: the document changed.</summary>
    Updated,

    /// <summary>A blob was deleted: the document is gone.</summary>
    Deleted,

    /// <summary>Not about a document of this store.</summary>
    Ignored,
}

/// <summary>One event of an Event Grid delivery, read.</summary>
/// <param name="Kind">What to do about it.</param>
/// <param name="Key">The document, for an update or a deletion.</param>
/// <param name="Revision">The blob's ETag in the form the store reports it, quoted; for an update.</param>
/// <param name="ValidationCode">The code to answer a handshake with.</param>
public sealed record BlobEvent(BlobEventKind Kind, BpmnDocumentKey? Key = null, string? Revision = null, string? ValidationCode = null);

/// <summary>Reads a delivery in Event Grid's schema into what the ingress does with it.</summary>
/// <remarks>
/// <c>Microsoft.Storage.BlobCreated</c> fires when a blob is created or replaced, and its data carries the blob's ETag, which is what the Azure Blob store
/// uses as the revision. Events are at least once and unordered; that is fine, because the ingress publishes "changed, revision X" and the binding compares.
/// </remarks>
public static class BlobEventGridParser
{
    private const string ValidationType = "Microsoft.EventGrid.SubscriptionValidationEvent";
    private const string CreatedType = "Microsoft.Storage.BlobCreated";
    private const string DeletedType = "Microsoft.Storage.BlobDeleted";

    /// <summary>Reads a delivery.</summary>
    /// <param name="json">The request body: a JSON array of events.</param>
    /// <param name="container">The container the store uses; events about other containers are ignored.</param>
    /// <param name="prefix">The prefix the store was given.</param>
    /// <returns>One entry per event; an unreadable delivery gives none.</returns>
    public static IReadOnlyList<BlobEvent> Parse(string json, string container, string? prefix)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return [];
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];

            return [.. document.RootElement.EnumerateArray().Select(element => ParseOne(element, container, prefix))];
        }
    }

    private static BlobEvent ParseOne(JsonElement element, string container, string? prefix)
    {
        var type = Text(element, "eventType");
        if (type == ValidationType)
        {
            return element.TryGetProperty("data", out var validation) && Text(validation, "validationCode") is { } code
                ? new BlobEvent(BlobEventKind.Validation, ValidationCode: code)
                : new BlobEvent(BlobEventKind.Ignored);
        }

        if (type is not (CreatedType or DeletedType) || Text(element, "subject") is not { } subject) return new BlobEvent(BlobEventKind.Ignored);

        // /blobServices/default/containers/<container>/blobs/<blob name>
        var marker = $"/containers/{container}/blobs/";
        var at = subject.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0 || !AzureBlobBpmnDocumentStore.TryKeyOf(subject[(at + marker.Length)..], prefix, out var key)) return new BlobEvent(BlobEventKind.Ignored);

        if (type == DeletedType) return new BlobEvent(BlobEventKind.Deleted, key);

        var etag = element.TryGetProperty("data", out var data) ? Text(data, "eTag") : null;
        return new BlobEvent(BlobEventKind.Updated, key, etag is null ? null : Quoted(etag));
    }

    // The event carries the ETag bare; the store reports it as the service's header does, in quotes.
    private static string Quoted(string etag) => etag.StartsWith('"') ? etag : $"\"{etag}\"";

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
