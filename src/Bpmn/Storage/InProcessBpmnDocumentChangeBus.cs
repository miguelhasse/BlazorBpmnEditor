using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>A change bus that reaches subscribers in the same process only.</summary>
/// <remarks>
/// Enough for Blazor Server on one instance, tests and the playground. Handlers are started one after
/// another on the publisher's thread, outside any lock, and are not waited on to finish; a handler that
/// throws, now or later, is logged and does not stop the others.
/// </remarks>
/// <param name="logger">Where failing handlers are reported; nowhere when null.</param>
public sealed partial class InProcessBpmnDocumentChangeBus(ILogger<InProcessBpmnDocumentChangeBus>? logger = null)
    : IBpmnDocumentChangeBus
{
    private readonly Lock _gate = new();
    private readonly Dictionary<BpmnDocumentKey, List<Subscription>> _subscriptions = [];
    private readonly ILogger _logger = logger ?? NullLogger<InProcessBpmnDocumentChangeBus>.Instance;

    /// <inheritdoc />
    public ValueTask PublishAsync(BpmnDocumentChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        Subscription[] targets;
        lock (_gate)
        {
            targets = _subscriptions.TryGetValue(change.Key, out var list) ? [.. list] : [];
        }

        // Every subscriber is handed the change before any is waited on, and none is waited on to finish:
        // an editor's handler may reload a whole document on its own circuit, and the writer's save must
        // not take as long as every open viewer's reload. A handler that finishes synchronously, as one
        // that only queues work does, has finished by the time this returns.
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ValueTask handled;
            try
            {
                handled = target.Handler(change);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogHandlerFailed(_logger, exception, change.Key.ToString());
                continue;
            }

            if (!handled.IsCompletedSuccessfully) _ = ObserveAsync(handled, change.Key);
        }

        return ValueTask.CompletedTask;
    }

    private async Task ObserveAsync(ValueTask handled, BpmnDocumentKey key)
    {
        try
        {
            await handled.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogHandlerFailed(_logger, exception, key.ToString());
        }
    }

    /// <inheritdoc />
    public ValueTask<IAsyncDisposable> SubscribeAsync(
        BpmnDocumentKey key,
        Func<BpmnDocumentChange, ValueTask> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!key.IsValid) throw new ArgumentException("The key does not name a document.", nameof(key));

        var subscription = new Subscription(this, key, handler);
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(key, out var list)) _subscriptions[key] = list = [];
            list.Add(subscription);
        }

        return ValueTask.FromResult<IAsyncDisposable>(subscription);
    }

    private void Remove(Subscription subscription)
    {
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(subscription.Key, out var list)) return;

            list.Remove(subscription);
            if (list.Count == 0) _subscriptions.Remove(subscription.Key);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A document change handler for {Key} failed.")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string key);

    private sealed class Subscription(InProcessBpmnDocumentChangeBus bus, BpmnDocumentKey key, Func<BpmnDocumentChange, ValueTask> handler)
        : IAsyncDisposable
    {
        public BpmnDocumentKey Key { get; } = key;

        public Func<BpmnDocumentChange, ValueTask> Handler { get; } = handler;

        public ValueTask DisposeAsync()
        {
            bus.Remove(this);
            return ValueTask.CompletedTask;
        }
    }
}
