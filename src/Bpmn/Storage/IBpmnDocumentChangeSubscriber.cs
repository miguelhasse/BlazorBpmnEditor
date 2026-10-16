namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>
/// The client-side view of document changes, which is what a <see cref="BpmnDocumentBinding"/> uses.
/// </summary>
/// <remarks>
/// One implementation per environment: Blazor Server subscribes straight to the bus
/// (<see cref="BusBpmnDocumentChangeSubscriber"/>); a WebAssembly client uses a wire transport. An
/// implementation raises <see cref="Resubscribed"/> after any gap in delivery, so the binding can make
/// its single catch-up check.
/// </remarks>
public interface IBpmnDocumentChangeSubscriber
{
    /// <summary>Raised when <see cref="State"/> changes.</summary>
    public event Func<BpmnChangeFeedState, ValueTask>? StateChanged;

    /// <summary>
    /// Raised after any gap in delivery: a reconnect, a connection found dead, a restarted bus or a
    /// renewed token. Changes made during the gap were not delivered.
    /// </summary>
    public event Func<ValueTask>? Resubscribed;

    /// <summary>Whether notifications are currently arriving.</summary>
    public BpmnChangeFeedState State { get; }

    /// <summary>Subscribes to changes of one document.</summary>
    /// <param name="key">The document.</param>
    /// <param name="handler">Called for each change; it receives key and revision, never content.</param>
    /// <param name="cancellationToken">Cancels subscribing.</param>
    /// <returns>A subscription; disposing it stops delivery.</returns>
    public ValueTask<IAsyncDisposable> SubscribeAsync(
        BpmnDocumentKey key,
        Func<BpmnDocumentChange, ValueTask> handler,
        CancellationToken cancellationToken = default);
}
