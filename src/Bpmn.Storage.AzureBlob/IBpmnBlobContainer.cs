namespace Hasseware.AspNetCore.Components.Bpmn.Storage.AzureBlob;

/// <summary>A blob that was read.</summary>
/// <param name="Bytes">Its content.</param>
/// <param name="ETag">Its ETag, which is the document's revision.</param>
/// <param name="Modified">When it was last written.</param>
public sealed record BpmnBlob(byte[] Bytes, string ETag, DateTimeOffset Modified);

/// <summary>What is known about a blob without reading it.</summary>
/// <param name="ETag">Its ETag.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="Modified">When it was last written.</param>
public sealed record BpmnBlobInfo(string ETag, long Size, DateTimeOffset Modified);

/// <summary>A blob in a listing.</summary>
/// <param name="Name">Its full name in the container.</param>
/// <param name="Size">Its size in bytes.</param>
/// <param name="Modified">When it was last written.</param>
public sealed record BpmnBlobListing(string Name, long Size, DateTimeOffset Modified);

/// <summary>How a conditional write or delete ended.</summary>
public enum BpmnBlobOutcome
{
    /// <summary>It happened.</summary>
    Succeeded,

    /// <summary>The condition did not hold: the blob exists when it had to be absent, or is at another ETag.</summary>
    ConditionFailed,

    /// <summary>There is no such blob.</summary>
    NotFound,
}

/// <summary>The result of a conditional write.</summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="ETag">The new ETag after a success.</param>
/// <param name="Modified">When it was written after a success.</param>
public sealed record BpmnBlobWrite(BpmnBlobOutcome Outcome, string? ETag = null, DateTimeOffset? Modified = null);

/// <summary>
/// The part of a blob container the document store uses, so that it can be tested without Azure and pointed at something
/// that is not Azure. <see cref="AzureBlobBpmnDocumentStore"/> uses <see cref="AzureBlobContainer"/> over a real container.
/// </summary>
/// <remarks>Every write is conditional and atomic on the service's side; an implementation must never read and then write in application code.</remarks>
public interface IBpmnBlobContainer
{
    /// <summary>Reads a blob.</summary>
    /// <param name="name">The blob's name.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The blob, or null when there is none.</returns>
    public ValueTask<BpmnBlob?> ReadAsync(string name, CancellationToken cancellationToken);

    /// <summary>Reads a blob's ETag, size and time.</summary>
    /// <param name="name">The blob's name.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>What is known, or null when there is no such blob.</returns>
    public ValueTask<BpmnBlobInfo?> StatAsync(string name, CancellationToken cancellationToken);

    /// <summary>Writes a blob.</summary>
    /// <param name="name">The blob's name.</param>
    /// <param name="bytes">The content.</param>
    /// <param name="ifMatch">The ETag the blob must have; null to require that it does not exist.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>How it ended.</returns>
    public ValueTask<BpmnBlobWrite> WriteAsync(string name, byte[] bytes, string? ifMatch, CancellationToken cancellationToken);

    /// <summary>Deletes a blob.</summary>
    /// <param name="name">The blob's name.</param>
    /// <param name="ifMatch">The ETag the blob must have; null to delete whatever is there.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>How it ended.</returns>
    public ValueTask<BpmnBlobOutcome> DeleteAsync(string name, string? ifMatch, CancellationToken cancellationToken);

    /// <summary>Lists every blob whose name starts with a prefix.</summary>
    /// <param name="prefix">The prefix.</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>The blobs, in any order.</returns>
    public IAsyncEnumerable<BpmnBlobListing> ListAsync(string prefix, CancellationToken cancellationToken);
}
