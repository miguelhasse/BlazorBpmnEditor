using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ModelContextProtocol.AspNetCore;

namespace Hasseware.AspNetCore.Components.Bpmn.Mcp.AspNetCore;

/// <summary>Hosts the BPMN MCP server over streamable HTTP.</summary>
public static class BpmnMcpHttpExtensions
{
    private static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "[::1]"];

    /// <summary>
    /// Registers the BPMN tools for HTTP: stateless sessions (set explicitly, so a change of the SDK's default cannot
    /// change this host), and the tenant and caller taken from the authenticated principal.
    /// </summary>
    /// <param name="services">Host services; an <c>IBpmnDocumentStore</c> must be registered too.</param>
    /// <param name="configureHttp">How callers are identified and the endpoint guarded.</param>
    /// <param name="configureMcp">The server's own options, such as <c>AllowWrites</c> and the limits.</param>
    /// <returns>The server builder, so a host can add its own tools. The tools that write are registered when <c>AllowWrites</c> is set.</returns>
    public static IMcpServerBuilder AddBpmnMcpHttp(
        this IServiceCollection services,
        Action<BpmnMcpHttpOptions>? configureHttp = null,
        Action<BpmnMcpOptions>? configureMcp = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var http = new BpmnMcpHttpOptions();
        configureHttp?.Invoke(http);

        services.AddSingleton(http);
        services.AddHttpContextAccessor();
        services.AddBpmnMcp(configureMcp);
        services.Replace(ServiceDescriptor.Singleton<IBpmnMcpTenantResolver, HttpBpmnMcpTenantResolver>());

        var server = services.AddMcpServer()
            .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
            .WithBpmnTools();

        // Allowing writes is one decision, so it also offers the tools that write; leaving it off offers none.
        var mcp = new BpmnMcpOptions();
        configureMcp?.Invoke(mcp);
        if (mcp.AllowWrites) server.WithBpmnWriteTools();

        return server;
    }

    /// <summary>Maps the endpoint, guarded as <see cref="BpmnMcpHttpOptions"/> says.</summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="pattern">Where to serve it.</param>
    /// <returns>The endpoint's convention builder, for anything the host wants to add.</returns>
    /// <exception cref="InvalidOperationException">The options leave open how callers are partitioned, or allow anonymous callers without a single shared space.</exception>
    public static IEndpointConventionBuilder MapBpmnMcp(this IEndpointRouteBuilder endpoints, string pattern = "/mcp")
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetRequiredService<BpmnMcpHttpOptions>();
        Validate(options);

        var endpoint = endpoints.MapMcp(pattern);

        if (!options.AllowAnonymous)
        {
            endpoint.RequireAuthorization(policy =>
            {
                policy.RequireAuthenticatedUser();
                if (options.TenantClaimType is { } claim) policy.RequireClaim(claim);
            });
            if (options.AuthorizationPolicy is { } named) endpoint.RequireAuthorization(named);
        }
        else
        {
            endpoint.AllowAnonymous();
        }

        if (options.RateLimitPolicy is { } rateLimit) endpoint.RequireRateLimiting(rateLimit);

        endpoint.Add(builder =>
        {
            var next = builder.RequestDelegate!;
            builder.RequestDelegate = async context =>
            {
                if (Refusal(context, options) is { } status)
                {
                    context.Response.StatusCode = status;
                    return;
                }

                await next(context).ConfigureAwait(false);
            };
        });

        return endpoint;
    }

    private static void Validate(BpmnMcpHttpOptions options)
    {
        if (options.TenantClaimType is null && !options.SingleTenant)
        {
            throw new InvalidOperationException(
                $"Choose how callers are partitioned: set {nameof(BpmnMcpHttpOptions.TenantClaimType)} to the claim that names the tenant, or set {nameof(BpmnMcpHttpOptions.SingleTenant)} when every caller shares one document space.");
        }

        if (options.SingleTenant && options.TenantClaimType is not null)
        {
            throw new InvalidOperationException($"{nameof(BpmnMcpHttpOptions.SingleTenant)} and {nameof(BpmnMcpHttpOptions.TenantClaimType)} contradict each other: choose one.");
        }

        if (options.AllowAnonymous && !options.SingleTenant)
        {
            throw new InvalidOperationException($"{nameof(BpmnMcpHttpOptions.AllowAnonymous)} needs {nameof(BpmnMcpHttpOptions.SingleTenant)}: an anonymous caller has no tenant.");
        }

        if (options.AllowAnonymous && options.AuthorizationPolicy is not null)
        {
            throw new InvalidOperationException($"{nameof(BpmnMcpHttpOptions.AuthorizationPolicy)} is applied only to authenticated callers, so it has no effect with {nameof(BpmnMcpHttpOptions.AllowAnonymous)}: remove one of them rather than believe the policy is enforced.");
        }
    }

    // The status to refuse with, or null when the request may go on: the Host and Origin a browser could be tricked into
    // sending, and a body larger than the host allows.
    private static int? Refusal(HttpContext context, BpmnMcpHttpOptions options)
    {
        var request = context.Request;

        var host = request.Host.Host;
        var hostAllowed = options.AllowedHosts.Count == 0
            ? LoopbackHosts.Contains(host, StringComparer.OrdinalIgnoreCase)
            : options.AllowedHosts.Contains(host) || options.AllowedHosts.Contains(request.Host.Value ?? string.Empty);
        if (!hostAllowed) return StatusCodes.Status403Forbidden;

        if (request.Headers.Origin.Count > 0)
        {
            var origin = request.Headers.Origin.ToString();
            var own = $"{request.Scheme}://{request.Host}";
            if (!string.Equals(origin, own, StringComparison.OrdinalIgnoreCase) && !options.AllowedOrigins.Contains(origin)) return StatusCodes.Status403Forbidden;
        }

        // A claim that names no tenant the stores could hold (an email address, a URL, something too long) is a caller the endpoint cannot place: refused here,
        // as a caller with no claim at all is, and not discovered as an exception from a store on every tool call.
        if (!options.AllowAnonymous && !options.SingleTenant && options.TenantClaimType is { } claim
            && !BpmnDocumentKey.IsValidTenant(context.User.FindFirst(claim)?.Value))
        {
            return StatusCodes.Status403Forbidden;
        }

        if (request.ContentLength > options.MaxRequestBodyBytes) return StatusCodes.Status413PayloadTooLarge;

        var limit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = options.MaxRequestBodyBytes;

        return null;
    }
}
