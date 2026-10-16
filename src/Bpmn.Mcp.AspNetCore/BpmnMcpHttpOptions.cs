namespace Hasseware.AspNetCore.Components.Bpmn.Mcp.AspNetCore;

/// <summary>How the HTTP host identifies callers and guards the endpoint.</summary>
/// <remarks>
/// <para>
/// The endpoint requires an authenticated caller. Who a caller is, and which tenant's documents they may see, comes from
/// their claims and never from a tool argument: set <see cref="TenantClaimType"/>, or say <see cref="SingleTenant"/>
/// when every caller shares one document space. A host must choose, so a deployment cannot end up serving the shared space
/// to everyone by forgetting to.
/// </para>
/// </remarks>
public sealed class BpmnMcpHttpOptions
{
    /// <summary>
    /// The claim that names the caller's tenant, such as <c>tid</c> or an organization claim. A caller without it is
    /// refused with 403, never served the shared space.
    /// </summary>
    public string? TenantClaimType { get; set; }

    /// <summary>Every caller works in one shared, unpartitioned document space. Set only when that is the intent.</summary>
    public bool SingleTenant { get; set; }

    /// <summary>The claim that identifies the caller in the audit log; the name identifier, then <c>sub</c>, then the name, when null.</summary>
    public string? CallerClaimType { get; set; }

    /// <summary>
    /// Serves callers who have not authenticated. Off by default and honoured only with <see cref="SingleTenant"/>, because
    /// an anonymous caller has no tenant. For a loopback endpoint on a developer's machine, not for a deployment.
    /// </summary>
    public bool AllowAnonymous { get; set; }

    /// <summary>An authorization policy every caller must also satisfy, by name, such as a scope or role requirement.</summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    /// The <c>Host</c> header values the endpoint answers to, which is what stops a web page from reaching a local server
    /// through a rebound DNS name. Empty means loopback names only (<c>localhost</c>, <c>127.0.0.1</c>, <c>[::1]</c>); a
    /// deployed host lists its own names.
    /// </summary>
    public ISet<string> AllowedHosts { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The <c>Origin</c> values a browser may send. A request with no <c>Origin</c> (every non-browser client) is
    /// not affected; one whose origin is neither listed nor the endpoint's own is refused with 403.
    /// </summary>
    public ISet<string> AllowedOrigins { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The largest request body accepted, in bytes; larger ones are refused with 413.</summary>
    public long MaxRequestBodyBytes
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 1_000_000;

    /// <summary>The name of a rate-limiter policy, from <c>AddRateLimiter</c>, to apply to the endpoint; none when null.</summary>
    public string? RateLimitPolicy { get; set; }
}
