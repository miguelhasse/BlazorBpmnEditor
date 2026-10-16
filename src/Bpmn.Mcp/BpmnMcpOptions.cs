namespace Hasseware.AspNetCore.Components.Bpmn.Mcp;

/// <summary>What the server offers an agent, and how much of it.</summary>
/// <remarks>
/// Preview. Out of the box the server only reads. Tools that change documents are offered with
/// <see cref="BpmnMcpServiceCollectionExtensions.WithBpmnWriteTools"/> and work only when <see cref="AllowWrites"/> is set.
/// </remarks>
public sealed class BpmnMcpOptions
{
    /// <summary>
    /// The tenant every call works in, or null for the single, unpartitioned space. Fixed by the host, never
    /// taken from a tool argument. A host that serves several users supplies an <see cref="IBpmnMcpTenantResolver"/> instead.
    /// </summary>
    public string? Tenant { get; set; }

    /// <summary>Whether tools that change documents may do so. False by default; the writing tools then refuse every call.</summary>
    public bool AllowWrites { get; set; }

    /// <summary>The name edits and calls are attributed to.</summary>
    public string AgentName { get; set; } = "mcp";

    /// <summary>The most elements, connectors or findings one call lists.</summary>
    public int MaxElementsPerCall
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 200;

    /// <summary>The largest document one call will read, and the largest export it returns, in bytes.</summary>
    public int MaxXmlBytes
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 1_000_000;
}
