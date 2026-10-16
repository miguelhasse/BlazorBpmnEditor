namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>
/// The server-side channel for document changes: where writers publish them and where server-side
/// consumers subscribe.
/// </summary>
/// <remarks>
/// The default, <see cref="InProcessBpmnDocumentChangeBus"/>, reaches only subscribers in the same
/// process. A host with several instances, or an agent server in another process, replaces it with a bus
/// backed by a broker. Wire adapters (SignalR, Server-Sent Events) subscribe here and forward to their
/// connected clients.
/// </remarks>
public interface IBpmnDocumentChangeBus
{
    /// <summary>Publishes a change to every subscriber of its document.</summary>
    /// <param name="change">The change.</param>
    /// <param name="cancellationToken">Cancels publishing.</param>
    /// <returns>A task that completes when the change has been handed to every local subscriber.</returns>
    public ValueTask PublishAsync(BpmnDocumentChange change, CancellationToken cancellationToken = default);

    /// <summary>Subscribes to changes of one document. There is no folder or wildcard subscription.</summary>
    /// <param name="key">The document.</param>
    /// <param name="handler">Called for each change; it should hand work off rather than do it inline.</param>
    /// <param name="cancellationToken">Cancels subscribing.</param>
    /// <returns>A subscription; disposing it stops delivery.</returns>
    public ValueTask<IAsyncDisposable> SubscribeAsync(
        BpmnDocumentKey key,
        Func<BpmnDocumentChange, ValueTask> handler,
        CancellationToken cancellationToken = default);
}
