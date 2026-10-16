using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp;

/// <summary>Opt-in registration for offering the editor to browser agents.</summary>
public static class BpmnWebMcpServiceCollectionExtensions
{
    /// <summary>Registers what <see cref="BpmnWebMcp"/> needs.</summary>
    /// <param name="services">Host services.</param>
    /// <param name="configure">Changes the options.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddBpmnWebMcp(this IServiceCollection services, Action<BpmnWebMcpOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new BpmnWebMcpOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddScoped<BpmnWebMcpRegistry>();
        return services;
    }
}
