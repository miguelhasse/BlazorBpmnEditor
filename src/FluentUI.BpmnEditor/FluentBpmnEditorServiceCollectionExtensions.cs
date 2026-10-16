using Hasseware.AspNetCore.Components.Bpmn;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn;

/// <summary>Registration helpers for the Fluent UI editor.</summary>
public static class FluentBpmnEditorServiceCollectionExtensions
{
    /// <summary>Registers Core services and Fluent parts without overriding host choices.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional application defaults.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddFluentBpmnEditor(this IServiceCollection services, Action<BpmnOptions>? configure = null)
    {
        services.AddBpmnComponents(configure);
        services.PostConfigure<BpmnOptions>(FluentBpmnDefaults.Register);
        return services;
    }
}
