using System.Runtime.CompilerServices;

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.AzureBlob;

/// <summary>An <see cref="IBpmnBlobContainer"/> over a real Azure Blob Storage container.</summary>
/// <remarks>
/// Each write carries the condition as a request header (<c>If-Match</c>, or <c>If-None-Match: *</c> to create), so the service
/// decides atomically. The credentials are those of the <see cref="BlobContainerClient"/> the host built from its own configuration.
/// </remarks>
/// <param name="container">The container.</param>
public sealed class AzureBlobContainer(BlobContainerClient container) : IBpmnBlobContainer
{
    /// <inheritdoc />
    public async ValueTask<BpmnBlob?> ReadAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var response = await container.GetBlobClient(name).DownloadContentAsync(cancellationToken).ConfigureAwait(false);
            return new BpmnBlob(response.Value.Content.ToArray(), ETagOf(response.Value.Details.ETag), response.Value.Details.LastModified);
        }
        catch (RequestFailedException exception) when (exception.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask<BpmnBlobInfo?> StatAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var response = await container.GetBlobClient(name).GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            return new BpmnBlobInfo(ETagOf(response.Value.ETag), response.Value.ContentLength, response.Value.LastModified);
        }
        catch (RequestFailedException exception) when (exception.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask<BpmnBlobWrite> WriteAsync(string name, byte[] bytes, string? ifMatch, CancellationToken cancellationToken)
    {
        var conditions = ifMatch is null
            ? new BlobRequestConditions { IfNoneMatch = ETag.All }
            : new BlobRequestConditions { IfMatch = new ETag(ifMatch) };

        try
        {
            var response = await container.GetBlobClient(name)
                .UploadAsync(BinaryData.FromBytes(bytes), new BlobUploadOptions { Conditions = conditions }, cancellationToken).ConfigureAwait(false);
            return new BpmnBlobWrite(BpmnBlobOutcome.Succeeded, ETagOf(response.Value.ETag), response.Value.LastModified);
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
            // The service refuses a create over an existing blob and an update at another ETag with one of these; which, depends on the operation.
            return new BpmnBlobWrite(BpmnBlobOutcome.ConditionFailed);
        }
        catch (RequestFailedException exception) when (exception.ErrorCode == BlobErrorCode.BlobNotFound)
        {
            return new BpmnBlobWrite(BpmnBlobOutcome.NotFound);
        }
    }

    /// <inheritdoc />
    public async ValueTask<BpmnBlobOutcome> DeleteAsync(string name, string? ifMatch, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await container.GetBlobClient(name)
                .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, ifMatch is null ? null : new BlobRequestConditions { IfMatch = new ETag(ifMatch) }, cancellationToken).ConfigureAwait(false);
            return deleted.Value ? BpmnBlobOutcome.Succeeded : BpmnBlobOutcome.NotFound;
        }
        catch (RequestFailedException exception) when (exception.Status is 409 or 412)
        {
            return BpmnBlobOutcome.ConditionFailed;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<BpmnBlobListing> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var item in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken).ConfigureAwait(false))
        {
            yield return new BpmnBlobListing(item.Name, item.Properties.ContentLength ?? 0, item.Properties.LastModified ?? DateTimeOffset.UnixEpoch);
        }
    }

    private static string ETagOf(ETag etag) => etag.ToString("H");
}
