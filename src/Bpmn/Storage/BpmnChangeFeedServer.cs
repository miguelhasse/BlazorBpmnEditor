using System.Threading.Channels;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>How a live-update server adapter identifies callers and bounds what it holds for them.</summary>
public sealed class BpmnChangeFeedServerOptions
{
    /// <summary>The claim that names the caller's tenant. A caller without it is refused, never served the shared space.</summary>
    public string? TenantClaimType { get; set; }

    /// <summary>Every caller works in one shared, unpartitioned document space. Set only when that is the intent.</summary>
    public bool SingleTenant { get; set; }

    /// <summary>Serves callers who have not authenticated; honoured only with <see cref="SingleTenant"/>. For a loopback developer endpoint.</summary>
    public bool AllowAnonymous { get; set; }

    /// <summary>How often a stream sends a heartbeat, so that proxies do not cut a quiet connection and clients can tell a dead one.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long one stream or connection lives before the server ends it, so a subscription never outlives the credentials it
    /// was opened with: the client reconnects, and is authenticated again. Null for no limit.
    /// </summary>
    public TimeSpan? MaxStreamLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>The most subscriptions the host holds at once.</summary>
    public int MaxSubscriptions
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 10_000;

    /// <summary>The most subscriptions one caller holds at once.</summary>
    public int MaxSubscriptionsPerCaller
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 16;
}

/// <summary>Why a subscription was not opened.</summary>
public enum BpmnChangeFeedRefusal
{
    /// <summary>It was opened.</summary>
    None,

    /// <summary>The path is not a document path, or the caller may not read it; the two look the same.</summary>
    NotFound,

    /// <summary>A limit was reached.</summary>
    TooMany,
}

/// <summary>One caller's subscription to one document, as a server adapter holds it.</summary>
public sealed class BpmnChangeFeedSubscription : IAsyncDisposable
{
    private readonly IAsyncDisposable _busSubscription;
    private readonly Action _release;
    private int _disposed;

    internal BpmnChangeFeedSubscription(BpmnDocumentKey key, Channel<BpmnDocumentChange> channel, IAsyncDisposable busSubscription, Action release)
    {
        Key = key;
        Channel = channel;
        _busSubscription = busSubscription;
        _release = release;
    }

    /// <summary>The document, with the caller's tenant.</summary>
    public BpmnDocumentKey Key { get; }

    /// <summary>The changes to send. Bursts are coalesced: when the client is behind, only the latest change is kept.</summary>
    public ChannelReader<BpmnDocumentChange> Changes => Channel.Reader;

    private Channel<BpmnDocumentChange> Channel { get; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _release();
        await _busSubscription.DisposeAsync().ConfigureAwait(false);
        Channel.Writer.TryComplete();
    }
}

/// <summary>
/// The rules every live-update server adapter applies before a caller may watch a document, so that no transport has
/// to get them right on its own: the path is parsed, the caller must be allowed to read the document (and learns nothing
/// otherwise), subscriptions are counted, and what is held per subscription is one slot.
/// </summary>
/// <remarks>
/// A transport authenticates the connection, takes the tenant from the principal, and passes both here. The bus
/// is subscribed to one document at a time, and a client that falls behind is given the latest change only: a replay
/// would tell the editor nothing the stored revision does not.
/// </remarks>
public sealed class BpmnChangeFeedServer
{
    private readonly IBpmnDocumentChangeBus _bus;
    private readonly IBpmnDocumentAuthorizer _authorizer;
    private readonly BpmnChangeFeedServerOptions _options;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _perCaller = new(StringComparer.Ordinal);
    private int _total;

    /// <summary>Creates the server logic.</summary>
    /// <param name="bus">Where changes are published.</param>
    /// <param name="authorizer">Decides whether the caller may read a document.</param>
    /// <param name="options">The limits.</param>
    public BpmnChangeFeedServer(IBpmnDocumentChangeBus bus, IBpmnDocumentAuthorizer authorizer, BpmnChangeFeedServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(authorizer);
        ArgumentNullException.ThrowIfNull(options);

        _bus = bus;
        _authorizer = authorizer;
        _options = options;
    }

    /// <summary>The number of subscriptions open now.</summary>
    public int Open
    {
        get
        {
            lock (_gate) return _total;
        }
    }

    /// <summary>Opens a subscription for a caller.</summary>
    /// <param name="tenant">The caller's tenant, from their credentials; null for the unpartitioned space.</param>
    /// <param name="path">The document the caller asks to watch.</param>
    /// <param name="callerId">Who is asking, for the per-caller limit.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>The subscription, or why there is none.</returns>
    public async ValueTask<(BpmnChangeFeedSubscription? Subscription, BpmnChangeFeedRefusal Refusal)> OpenAsync(
        string? tenant,
        string? path,
        string? callerId,
        CancellationToken cancellationToken = default)
    {
        if (!BpmnDocumentPath.TryParse(path, out var parsed) || !parsed.IsDocumentName) return (null, BpmnChangeFeedRefusal.NotFound);

        var key = new BpmnDocumentKey(tenant, parsed);
        if (!await _authorizer.IsAllowedAsync(BpmnDocumentAction.Read, key, cancellationToken).ConfigureAwait(false)) return (null, BpmnChangeFeedRefusal.NotFound);

        var caller = callerId ?? string.Empty;
        if (!TryReserve(caller)) return (null, BpmnChangeFeedRefusal.TooMany);

        try
        {
            // One slot, newest wins: the editor needs only to learn that the revision it holds is no longer the stored one.
            var channel = Channel.CreateBounded<BpmnDocumentChange>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
            });
            var bus = await _bus.SubscribeAsync(key, change =>
            {
                channel.Writer.TryWrite(change);
                return ValueTask.CompletedTask;
            }, cancellationToken).ConfigureAwait(false);

            return (new BpmnChangeFeedSubscription(key, channel, bus, () => Release(caller)), BpmnChangeFeedRefusal.None);
        }
        catch
        {
            Release(caller);
            throw;
        }
    }

    private bool TryReserve(string caller)
    {
        lock (_gate)
        {
            if (_total >= _options.MaxSubscriptions) return false;
            _perCaller.TryGetValue(caller, out var held);
            if (held >= _options.MaxSubscriptionsPerCaller) return false;

            _perCaller[caller] = held + 1;
            _total++;
            return true;
        }
    }

    private void Release(string caller)
    {
        lock (_gate)
        {
            _total--;
            if (_perCaller.TryGetValue(caller, out var held))
            {
                if (held <= 1) _perCaller.Remove(caller);
                else _perCaller[caller] = held - 1;
            }
        }
    }
}
