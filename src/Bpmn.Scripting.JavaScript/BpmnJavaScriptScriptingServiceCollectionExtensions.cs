using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Scripting.JavaScript;

/// <summary>Opt-in registration for JavaScript script validation.</summary>
public static class BpmnJavaScriptScriptingServiceCollectionExtensions
{
    /// <summary>Validates the syntax of Script Tasks whose <c>scriptFormat</c> is <c>javascript</c>.</summary>
    /// <param name="services">Host services.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddBpmnJavaScriptScripting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBpmnScriptValidator, BpmnJavaScriptScriptValidator>());
        return services;
    }
}
