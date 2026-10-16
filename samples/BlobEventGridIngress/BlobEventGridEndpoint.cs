using System.Security.Cryptography;
using System.Text;

using Hasseware.AspNetCore.Components.Bpmn.Storage;

namespace BlobEventGridIngress;

/// <summary>Where the ingress listens, and what it trusts.</summary>
/// <param name="Container">The blob container the store uses.</param>
/// <param name="Prefix">The prefix the store was given, if any.</param>
/// <param name="SharedSecret">The key Event Grid sends in the <c>aeg-sas-key</c> header; every delivery without it is refused.</param>
public sealed record BlobIngressOptions(string Container, string? Prefix, string SharedSecret);

/// <summary>The web endpoint an Event Grid subscription delivers blob events to.</summary>
public static class BlobEventGridEndpoint
{
    private const int MaxBodyBytes = 1_000_000;

    /// <summary>Maps <c>POST {pattern}</c>.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="options">Where to listen and what to trust.</param>
    /// <param name="pattern">The route.</param>
    /// <returns>The endpoint's convention builder.</returns>
    public static IEndpointConventionBuilder MapBlobEventGridIngress(this IEndpointRouteBuilder endpoints, BlobIngressOptions options, string pattern = "/events/blob") =>
        endpoints.MapPost(pattern, async (HttpContext context, BpmnDocumentChangeIngress ingress, CancellationToken cancellationToken) =>
        {
            // The inbound surface is the host's to defend: nothing is read from a caller that cannot name the shared key.
            if (!Matches(context.Request.Headers["aeg-sas-key"].ToString(), options.SharedSecret)) return Results.Unauthorized();
            if (context.Request.ContentLength > MaxBodyBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(cancellationToken);
            var events = BlobEventGridParser.Parse(body, options.Container, options.Prefix);

            // The handshake that proves this endpoint is ours to receive events.
            if (events.FirstOrDefault(item => item.Kind == BlobEventKind.Validation) is { ValidationCode: { } code }) return Results.Json(new { validationResponse = code });

            foreach (var item in events)
            {
                if (item is { Kind: BlobEventKind.Updated, Key: { } updated }) await ingress.PublishUpdatedAsync(updated, item.Revision, cancellationToken);
                else if (item is { Kind: BlobEventKind.Deleted, Key: { } deleted }) await ingress.PublishDeletedAsync(deleted, cancellationToken);
            }

            return Results.Ok();
        });

    private static bool Matches(string given, string expected) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(given)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));
}
