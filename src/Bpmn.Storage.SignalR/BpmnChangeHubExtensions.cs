using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage.SignalR;

/// <summary>Serves live document changes to editors over SignalR.</summary>
public static class BpmnChangeHubExtensions
{
    /// <summary>Registers what the hub needs. The host registers the document store and a change bus (<c>AddBpmnDocumentChanges</c>) itself.</summary>
    /// <param name="services">Host services.</param>
    /// <param name="configure">How callers are identified and the limits.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddBpmnChangeHub(this IServiceCollection services, Action<BpmnChangeFeedServerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new BpmnChangeFeedServerOptions();
        configure?.Invoke(options);

        // The options and the rules are shared by every live-update transport the host maps; the first to register them decides.
        services.TryAddSingleton(options);
        services.AddBpmnDocumentChanges();
        services.AddHttpContextAccessor();
        services.AddSignalR();
        services.TryAddSingleton<IBpmnDocumentAuthorizer, AllowAllBpmnDocumentAuthorizer>();
        services.TryAddSingleton<BpmnChangeFeedServer>();
        return services;
    }

    /// <summary>Maps the hub: authenticated, with the tenant taken from the caller's claims.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">Where to serve it.</param>
    /// <returns>The endpoint's convention builder.</returns>
    /// <exception cref="InvalidOperationException">The options leave open how callers are partitioned, or contradict one another.</exception>
    public static HubEndpointConventionBuilder MapBpmnDocumentChangeHub(this IEndpointRouteBuilder endpoints, string pattern = "/bpmn/changes")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<BpmnChangeFeedServerOptions>();
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

        var hub = endpoints.MapHub<BpmnDocumentChangeHub>(pattern);
        if (options.AllowAnonymous)
        {
            hub.AllowAnonymous();
        }
        else
        {
            hub.RequireAuthorization(policy =>
            {
                policy.RequireAuthenticatedUser();
                if (options.TenantClaimType is { } claim) policy.RequireClaim(claim);
            });
        }

        return hub;
    }
}
