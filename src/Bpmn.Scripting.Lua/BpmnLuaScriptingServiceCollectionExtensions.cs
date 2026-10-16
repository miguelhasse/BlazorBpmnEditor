using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Loretta.CodeAnalysis.Lua;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Scripting.Lua;

/// <summary>Opt-in registration for Lua script validation.</summary>
public static class BpmnLuaScriptingServiceCollectionExtensions
{
    /// <summary>Validates the syntax of Script Tasks whose <c>scriptFormat</c> is <c>lua</c>.</summary>
    /// <param name="services">Host services.</param>
    /// <param name="syntax">
    /// The Lua dialect the engine runs, such as <see cref="LuaSyntaxOptions.Lua51"/>; Lua 5.4 when null.
    /// Naming a dialect replaces any Lua validator registered by an earlier call, so the last call wins.
    /// </param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddBpmnLuaScripting(this IServiceCollection services, LuaSyntaxOptions? syntax = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (syntax is null)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBpmnScriptValidator, BpmnLuaScriptValidator>());
            return services;
        }

        // The profile takes the first validator registered for a format, so a dialect asked for after
        // the default would otherwise be ignored without a word.
        foreach (var earlier in services
            .Where(descriptor => descriptor.ServiceType == typeof(IBpmnScriptValidator)
                && (descriptor.ImplementationType == typeof(BpmnLuaScriptValidator)
                    || descriptor.ImplementationInstance is BpmnLuaScriptValidator))
            .ToList())
        {
            services.Remove(earlier);
        }

        services.AddSingleton<IBpmnScriptValidator>(new BpmnLuaScriptValidator(syntax));
        return services;
    }
}
