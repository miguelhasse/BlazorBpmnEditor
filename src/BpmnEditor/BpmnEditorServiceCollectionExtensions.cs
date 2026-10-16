using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>Registration helpers for the standard HTML editor.</summary>
public static class BpmnEditorServiceCollectionExtensions
{
    /// <summary>Registers Core services and standard parts without overriding host choices.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional application defaults.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddBpmnEditor(this IServiceCollection services, Action<BpmnOptions>? configure = null)
    {
        services.AddBpmnComponents(configure);
        services.PostConfigure<BpmnOptions>(BpmnStandardDefaults.Register);
        return services;
    }
}
