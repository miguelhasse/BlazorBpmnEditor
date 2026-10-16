using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>Registration helpers for the BPMN component library.</summary>
public static class BpmnServiceCollectionExtensions
{
    /// <summary>
    /// Registers the BPMN editor services and application-wide editor defaults.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Optional callback for application-wide editor defaults.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddBpmnComponents(
        this IServiceCollection services,
        Action<BpmnOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddLogging();
        services.AddLocalization();
        services.TryAddSingleton<BpmnDiagnosticLocalizer>();
        services.TryAddSingleton<BpmnDisplayLocalizer>();

        if (configure is null)
        {
            services.AddOptions<BpmnOptions>();
        }
        else
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IBpmnExpressionValidator, BpmnExpressionValidator>();
        services.TryAddSingleton<IBpmnFeelVariableProvider, BpmnFeelVariableProvider>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBpmnBindingCommandFactory, BpmnTemplateBindingCommandFactory>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBpmnProfileValidator, BpmnElementTemplateValidator>());
        services.AddOptions<BpmnScriptOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBpmnProfileValidator, BpmnScriptTaskProfile>());
        return services;
    }
}
