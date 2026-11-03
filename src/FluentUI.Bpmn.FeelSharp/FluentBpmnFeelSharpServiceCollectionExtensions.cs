using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.FeelSharp;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Opt-in Fluent UI FEEL editor registration.</summary>
public static class FluentBpmnFeelSharpServiceCollectionExtensions
{
    /// <summary>Installs the Fluent editor without enabling expression evaluation.</summary>
    public static IServiceCollection AddFluentBpmnFeelSharpEditor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Configure<BpmnOptions>(options => options.Properties.Editors.AddFeelEditor<FluentBpmnFeelSharpCodeEditor>());
        return services;
    }

    /// <summary>Installs the Fluent editor with shared diagnostics options, without enabling evaluation.</summary>
    public static IServiceCollection AddFluentBpmnFeelSharpEditor(this IServiceCollection services,
        Action<BpmnFeelSharpEditorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.AddFluentBpmnFeelSharpEditor();
        services.Configure(configure);
        return services;
    }
}
