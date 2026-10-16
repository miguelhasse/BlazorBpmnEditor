using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.SignalR;

/// <summary>
/// The hub editors connect to for live document changes. A connection subscribes to documents by path; the server sends
/// <c>Changed</c> with the change as JSON text. Map it with <see cref="BpmnChangeHubExtensions.MapBpmnDocumentChangeHub"/>.
/// </summary>
/// <remarks>
/// The tenant is the caller's claim, never a parameter. <c>Subscribe</c> answers <c>ok</c>, <c>notfound</c> (a path that is not
/// a document, or one the caller may not read: the two are the same), <c>toomany</c> or <c>forbidden</c> (no tenant).
/// </remarks>
public sealed class BpmnDocumentChangeHub : Hub
{
    private const string SubscriptionsKey = "bpmn.subscriptions";

    private readonly BpmnChangeFeedServer _feed;
    private readonly BpmnChangeFeedServerOptions _options;
    private readonly IHubContext<BpmnDocumentChangeHub> _hub;
    private readonly IHttpContextAccessor _accessor;

    /// <summary>Creates the hub for one invocation; the framework does this.</summary>
    /// <param name="feed">The rules shared by every live-update server.</param>
    /// <param name="options">The limits and how callers are identified.</param>
    /// <param name="hub">Sends to a connection from outside an invocation.</param>
    /// <param name="accessor">Gives an <see cref="IBpmnDocumentAuthorizer"/> the current request.</param>
    public BpmnDocumentChangeHub(BpmnChangeFeedServer feed, BpmnChangeFeedServerOptions options, IHubContext<BpmnDocumentChangeHub> hub, IHttpContextAccessor accessor)
    {
        _feed = feed;
        _options = options;
        _hub = hub;
        _accessor = accessor;
    }

    private Dictionary<string, (BpmnChangeFeedSubscription Subscription, CancellationTokenSource Cancellation)> Subscriptions
    {
        get
        {
            if (Context.Items[SubscriptionsKey] is not Dictionary<string, (BpmnChangeFeedSubscription, CancellationTokenSource)> held)
            {
                held = [];
                Context.Items[SubscriptionsKey] = held;
            }

            return held;
        }
    }

    /// <summary>Starts telling this connection about changes to a document.</summary>
    /// <param name="path">The document's logical path.</param>
    /// <returns><c>ok</c>, <c>notfound</c>, <c>toomany</c> or <c>forbidden</c>.</returns>
    [HubMethodName("Subscribe")]
    public async Task<string> SubscribeAsync(string path)
    {
        string? tenant = null;
        if (!_options.SingleTenant)
        {
            tenant = Context.User?.FindFirst(_options.TenantClaimType!)?.Value;
            if (string.IsNullOrWhiteSpace(tenant)) return "forbidden";
        }

        var subscriptions = Subscriptions;
        lock (subscriptions)
        {
            if (subscriptions.ContainsKey(path)) return "ok";
        }

        // An authorizer that reads the caller from the request needs one, and a hub invocation has none of its own.
        if (_accessor is HttpContextAccessor concrete) concrete.HttpContext = Context.GetHttpContext();

        var caller = CallerOf(Context.User) ?? Context.ConnectionId;
        var (subscription, refusal) = await _feed.OpenAsync(tenant, path, caller, Context.ConnectionAborted).ConfigureAwait(false);
        if (subscription is null) return refusal == BpmnChangeFeedRefusal.TooMany ? "toomany" : "notfound";

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Context.ConnectionAborted);
        lock (subscriptions)
        {
            subscriptions[path] = (subscription, cancellation);
        }

        _ = PumpAsync(Context.ConnectionId, subscription, cancellation.Token);
        return "ok";
    }

    /// <summary>Stops telling this connection about a document.</summary>
    /// <param name="path">The document's logical path.</param>
    /// <returns>A task that completes when the subscription is closed.</returns>
    [HubMethodName("Unsubscribe")]
    public async Task UnsubscribeAsync(string path)
    {
        (BpmnChangeFeedSubscription Subscription, CancellationTokenSource Cancellation) held;
        var subscriptions = Subscriptions;
        lock (subscriptions)
        {
            if (!subscriptions.Remove(path, out held)) return;
        }

        await held.Cancellation.CancelAsync().ConfigureAwait(false);
        held.Cancellation.Dispose();
        await held.Subscription.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override Task OnConnectedAsync()
    {
        // The connection ends after the lifetime the host allows, so it never outlives the credentials it was opened with; the client reconnects and signs in again.
        if (_options.MaxStreamLifetime is { } limit) _ = AbortLaterAsync(Context, limit);

        return base.OnConnectedAsync();
    }

    /// <inheritdoc />
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        List<(BpmnChangeFeedSubscription Subscription, CancellationTokenSource Cancellation)> held;
        var subscriptions = Subscriptions;
        lock (subscriptions)
        {
            held = [.. subscriptions.Values];
            subscriptions.Clear();
        }

        foreach (var (subscription, cancellation) in held)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            cancellation.Dispose();
            await subscription.DisposeAsync().ConfigureAwait(false);
        }

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    private static string? CallerOf(ClaimsPrincipal? user) =>
        user?.Identity?.IsAuthenticated == true
            ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value ?? user.Identity.Name
            : null;

    private static async Task AbortLaterAsync(HubCallerContext context, TimeSpan limit)
    {
        try
        {
            await Task.Delay(limit, context.ConnectionAborted).ConfigureAwait(false);
            context.Abort();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PumpAsync(string connectionId, BpmnChangeFeedSubscription subscription, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var change in subscription.Changes.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await _hub.Clients.Client(connectionId).SendAsync("Changed", BpmnDocumentChangeWire.From(change).ToJson(), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
