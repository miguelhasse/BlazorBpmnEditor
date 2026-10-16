namespace Hasseware.AspNetCore.Components.Bpmn.Mcp;

/// <summary>Says which tenant's documents the current call may see.</summary>
/// <remarks>
/// The answer must come from the host's own configuration or the authenticated caller, never from text an
/// agent supplied; stores apply it, so no tool can forget to.
/// </remarks>
public interface IBpmnMcpTenantResolver
{
    /// <summary>The tenant of the current call, or null for the single, unpartitioned space.</summary>
    /// <returns>The tenant.</returns>
    /// <exception cref="BpmnMcpTenantUnavailableException">The caller's tenant cannot be determined; the call is refused.</exception>
    public string? GetTenant();

    /// <summary>Who is calling, for the audit log; null when the host has no notion of a caller.</summary>
    /// <returns>A stable identifier for the caller, never a secret.</returns>
    public string? GetCallerId() => null;
}

/// <summary>Thrown by a resolver that cannot tell which tenant the current caller belongs to; the call is refused as forbidden.</summary>
public sealed class BpmnMcpTenantUnavailableException : Exception
{
    /// <summary>Creates the exception.</summary>
    public BpmnMcpTenantUnavailableException()
        : base("The caller has no tenant.")
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is missing.</param>
    public BpmnMcpTenantUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is missing.</param>
    /// <param name="innerException">The cause.</param>
    public BpmnMcpTenantUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The tenant the host fixed in <see cref="BpmnMcpOptions.Tenant"/>.</summary>
/// <param name="options">The options.</param>
public sealed class FixedBpmnMcpTenantResolver(BpmnMcpOptions options) : IBpmnMcpTenantResolver
{
    /// <inheritdoc />
    public string? GetTenant() => options.Tenant;
}
