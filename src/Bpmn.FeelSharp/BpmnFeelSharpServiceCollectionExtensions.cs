using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Independent opt-in registrations for editor and evaluation.</summary>
public static class BpmnFeelSharpServiceCollectionExtensions
{
    /// <summary>Installs the managed rich editor without enabling expression evaluation.</summary>
    public static IServiceCollection AddBpmnFeelSharpEditor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Configure<BpmnOptions>(options => options.Properties.Editors.AddFeelEditor<BpmnFeelSharpCodeEditor>());
        return services;
    }

    /// <summary>Registers explicit server-side interpretation; does not change editor selection.</summary>
    public static IServiceCollection AddBpmnFeelSharpEvaluation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IBpmnFeelSharpEvaluation, BpmnFeelSharpEvaluation>();
        return services;
    }
}
