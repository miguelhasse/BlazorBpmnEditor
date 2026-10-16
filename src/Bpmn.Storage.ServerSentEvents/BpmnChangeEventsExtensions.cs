using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.ServerSentEvents;

/// <summary>Serves live document changes to editors over Server-Sent Events.</summary>
public static class BpmnChangeEventsExtensions
{
    /// <summary>The event that says the stream is open and subscribed.</summary>
    public const string ReadyEvent = "ready";

    /// <summary>The event that says nothing changed but the connection is alive.</summary>
    public const string PingEvent = "ping";

    /// <summary>The event that carries a change.</summary>
    public const string ChangeEvent = "change";

    /// <summary>Registers what the endpoint needs. The host registers the document store and a change bus (<c>AddBpmnDocumentChanges</c>) itself.</summary>
    /// <param name="services">Host services.</param>
    /// <param name="configure">How callers are identified and the limits.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddBpmnChangeEvents(this IServiceCollection services, Action<BpmnChangeFeedServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new BpmnChangeFeedServerOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.AddBpmnDocumentChanges();
        services.TryAddSingleton<IBpmnDocumentAuthorizer, AllowAllBpmnDocumentAuthorizer>();
        services.TryAddSingleton<BpmnChangeFeedServer>();
        return services;
    }

    /// <summary>
    /// Maps <c>GET {pattern}?path=…</c>: one stream per document, authenticated, with the tenant taken from the caller's claims.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">Where to serve it.</param>
    /// <returns>The endpoint's convention builder.</returns>
    /// <exception cref="InvalidOperationException">The options leave open how callers are partitioned, or contradict one another.</exception>
    public static IEndpointConventionBuilder MapBpmnDocumentChangeEvents(this IEndpointRouteBuilder endpoints, string pattern = "/bpmn/changes")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<BpmnChangeFeedServerOptions>();
        Validate(options);

        var endpoint = endpoints.MapGet(pattern, (HttpContext context, string? path, BpmnChangeFeedServer feed, CancellationToken cancellationToken) =>
            HandleAsync(context, path, feed, options, cancellationToken));

        if (options.AllowAnonymous)
        {
            endpoint.AllowAnonymous();
        }
        else
        {
            endpoint.RequireAuthorization(policy =>
            {
                policy.RequireAuthenticatedUser();
                if (options.TenantClaimType is { } claim) policy.RequireClaim(claim);
            });
        }

        return endpoint;
    }

    private static void Validate(BpmnChangeFeedServerOptions options)
    {
        if (options.TenantClaimType is null && !options.SingleTenant)
        {
            throw new InvalidOperationException(
                $"Choose how callers are partitioned: set {nameof(BpmnChangeFeedServerOptions.TenantClaimType)} to the claim that names the tenant, or set {nameof(BpmnChangeFeedServerOptions.SingleTenant)} when every caller shares one document space.");
        }

        if (options.SingleTenant && options.TenantClaimType is not null)
        {
            throw new InvalidOperationException($"{nameof(BpmnChangeFeedServerOptions.SingleTenant)} and {nameof(BpmnChangeFeedServerOptions.TenantClaimType)} contradict each other: choose one.");
        }

        if (options.AllowAnonymous && !options.SingleTenant)
        {
            throw new InvalidOperationException($"{nameof(BpmnChangeFeedServerOptions.AllowAnonymous)} needs {nameof(BpmnChangeFeedServerOptions.SingleTenant)}: an anonymous caller has no tenant.");
        }
    }

    private static async Task<IResult> HandleAsync(HttpContext context, string? path, BpmnChangeFeedServer feed, BpmnChangeFeedServerOptions options, CancellationToken cancellationToken)
    {
        var tenant = options.SingleTenant ? null : context.User.FindFirst(options.TenantClaimType!)?.Value;
        var caller = CallerOf(context.User) ?? "anonymous";

        var (subscription, refusal) = await feed.OpenAsync(tenant, path, caller, cancellationToken).ConfigureAwait(false);
        if (subscription is null)
        {
            // A path that is not one, and one the caller may not read, are answered the same.
            return refusal == BpmnChangeFeedRefusal.TooMany ? Results.StatusCode(StatusCodes.Status429TooManyRequests) : Results.NotFound();
        }

        // The stream ends with the connection, and with the lifetime the host allows, which bounds how long it can outlive the caller's credentials.
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        return TypedResults.ServerSentEvents(StreamEventsAsync(subscription, options, cancellationToken));
    }

    private static string? CallerOf(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated == true
            ? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value ?? user.Identity.Name
            : null;

    private static async IAsyncEnumerable<SseItem<string>> StreamEventsAsync(
        BpmnChangeFeedSubscription subscription,
        BpmnChangeFeedServerOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (subscription.ConfigureAwait(false))
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (options.MaxStreamLifetime is { } limit) lifetime.CancelAfter(limit);

            // Open and subscribed: the client may treat the feed as live from here.
            yield return new SseItem<string>(string.Empty, ReadyEvent);

            while (!lifetime.IsCancellationRequested)
            {
                BpmnDocumentChange? change = null;
                var ended = false;
                using var beat = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                beat.CancelAfter(options.HeartbeatInterval);
                try
                {
                    if (await subscription.Changes.WaitToReadAsync(beat.Token).ConfigureAwait(false)) subscription.Changes.TryRead(out change);
                    else ended = true;
                }
                catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
                {
                    // The interval passed with nothing to say: say that the connection is alive.
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                if (ended) yield break;

                yield return change is null
                    ? new SseItem<string>(string.Empty, PingEvent)
                    : new SseItem<string>(BpmnDocumentChangeWire.From(change).ToJson(), ChangeEvent);
            }
        }
    }
}
