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

    /// <summary>Installs the editor with shared diagnostics options, without enabling evaluation.</summary>
    public static IServiceCollection AddBpmnFeelSharpEditor(this IServiceCollection services,
        Action<BpmnFeelSharpEditorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.AddBpmnFeelSharpEditor();
        services.Configure(configure);
        return services;
    }

    /// <summary>Registers explicit server-side interpretation; does not change editor selection.</summary>
    public static IServiceCollection AddBpmnFeelSharpEvaluation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IBpmnFeelSharpEvaluation, BpmnFeelSharpEvaluation>();
        return services;
    }

    /// <summary>Registers explicitly allowlisted trusted interpreter extensions, without enabling discovery.</summary>
    public static IServiceCollection AddBpmnFeelSharpEvaluation(this IServiceCollection services,
        Action<BpmnFeelSharpEvaluationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.AddBpmnFeelSharpEvaluation();
        services.Configure(configure);
        return services;
    }

    /// <summary>Opts into containment-based variable hints; does not infer execution-order availability.</summary>
    public static IServiceCollection AddBpmnFeelSharpScopedVariables(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Singleton<IBpmnFeelVariableProvider>(new BpmnFeelVariableProvider(true)));
        return services;
    }
}
