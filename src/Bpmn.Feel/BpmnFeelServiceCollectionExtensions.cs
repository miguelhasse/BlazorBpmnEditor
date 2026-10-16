using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Feel;

/// <summary>Opt-in registration for the rich FEEL editor.</summary>
public static class BpmnFeelServiceCollectionExtensions
{
    /// <summary>Replaces the default FEEL entry editor with the lazily loaded rich editor.</summary>
    /// <param name="services">Host services.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddBpmnFeelEditor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Configure<BpmnOptions>(options => options.Properties.Editors.AddFeelEditor<BpmnFeelCodeEditor>());
        return services;
    }
}
