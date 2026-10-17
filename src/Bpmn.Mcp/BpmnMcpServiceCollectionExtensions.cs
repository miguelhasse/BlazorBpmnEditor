using Hasseware.AspNetCore.Components.Bpmn.Storage;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Mcp;

/// <summary>Registers the BPMN tools of an MCP server.</summary>
public static class BpmnMcpServiceCollectionExtensions
{
    /// <summary>Registers what the BPMN tools need, over the <see cref="IBpmnDocumentStore"/> the host registered.</summary>
    /// <param name="services">Host services.</param>
    /// <param name="configure">Changes the options.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddBpmnMcp(this IServiceCollection services, Action<BpmnMcpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new BpmnMcpOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.TryAddSingleton<IBpmnMcpTenantResolver, FixedBpmnMcpTenantResolver>();
        services.TryAddSingleton<IBpmnDocumentAuthorizer, AllowAllBpmnDocumentAuthorizer>();
        services.TryAddSingleton<BpmnMcpService>();
        return services;
    }

    /// <summary>Offers the tools that change documents, as well; they still refuse unless <see cref="BpmnMcpOptions.AllowWrites"/> is set.</summary>
    /// <param name="builder">The server builder from <c>AddMcpServer()</c>.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IMcpServerBuilder WithBpmnWriteTools(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithTools<BpmnMcpWriteTools>();
    }

    /// <summary>Offers the read-only BPMN tools on an MCP server.</summary>
    /// <param name="builder">The server builder from <c>AddMcpServer()</c>.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IMcpServerBuilder WithBpmnTools(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithTools<BpmnMcpTools>();
    }
}
