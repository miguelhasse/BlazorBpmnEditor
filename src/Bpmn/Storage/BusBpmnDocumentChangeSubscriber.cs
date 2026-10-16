namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>
/// A subscriber that listens to the bus directly, with no wire in between: the right choice when the
/// editor runs on the server, as in Blazor Server.
/// </summary>
/// <remarks>
/// There is no connection to lose, so the feed is always <see cref="BpmnChangeFeedState.Live"/> and
/// <see cref="Resubscribed"/> is never raised.
/// </remarks>
/// <param name="bus">The bus to listen to.</param>
public sealed class BusBpmnDocumentChangeSubscriber(IBpmnDocumentChangeBus bus) : IBpmnDocumentChangeSubscriber
{
    /// <inheritdoc />
    public event Func<BpmnChangeFeedState, ValueTask>? StateChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public event Func<ValueTask>? Resubscribed
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public BpmnChangeFeedState State => BpmnChangeFeedState.Live;

    /// <inheritdoc />
    public ValueTask<IAsyncDisposable> SubscribeAsync(
        BpmnDocumentKey key,
        Func<BpmnDocumentChange, ValueTask> handler,
        CancellationToken cancellationToken = default) =>
        bus.SubscribeAsync(key, handler, cancellationToken);
}
