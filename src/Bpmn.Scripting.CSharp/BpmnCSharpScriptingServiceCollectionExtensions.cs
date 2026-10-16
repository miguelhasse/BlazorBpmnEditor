using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Scripting.CSharp;

/// <summary>Opt-in registration for C# script validation.</summary>
public static class BpmnCSharpScriptingServiceCollectionExtensions
{
    /// <summary>Validates the syntax of Script Tasks whose <c>scriptFormat</c> is <c>csharp</c>.</summary>
    /// <param name="services">Host services.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddBpmnCSharpScripting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IBpmnScriptValidator, BpmnCSharpScriptValidator>());
        return services;
    }
}
