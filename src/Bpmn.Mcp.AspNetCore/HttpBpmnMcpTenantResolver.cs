using System.Security.Claims;

using Microsoft.AspNetCore.Http;

namespace Hasseware.AspNetCore.Components.Bpmn.Mcp.AspNetCore;

/// <summary>Takes the tenant and the caller's identity from the authenticated principal of the current request.</summary>
/// <param name="accessor">The current request.</param>
/// <param name="options">Which claims to read.</param>
public sealed class HttpBpmnMcpTenantResolver(IHttpContextAccessor accessor, BpmnMcpHttpOptions options) : IBpmnMcpTenantResolver
{
    /// <inheritdoc />
    /// <exception cref="BpmnMcpTenantUnavailableException">The caller has no tenant claim; the endpoint refuses such callers before a tool runs, so this is a backstop.</exception>
    public string? GetTenant()
    {
        if (options.SingleTenant || options.TenantClaimType is null) return null;

        var tenant = accessor.HttpContext?.User.FindFirst(options.TenantClaimType)?.Value;
        return string.IsNullOrWhiteSpace(tenant)
            ? throw new BpmnMcpTenantUnavailableException("The caller has no tenant claim.")
            : tenant;
    }

    /// <inheritdoc />
    public string? GetCallerId()
    {
        var user = accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return null;

        return (options.CallerClaimType is { } type ? user.FindFirst(type)?.Value : null)
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.Identity.Name;
    }
}
