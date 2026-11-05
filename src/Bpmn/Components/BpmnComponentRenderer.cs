using System.Diagnostics.CodeAnalysis;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>Renders a registered component without passing its type through reflection-bound component parameters.</summary>
public static class BpmnComponentRenderer
{
    /// <summary>Creates content for a preserved component type.</summary>
    /// <param name="type">The component type.</param>
    /// <param name="parameters">Its parameters.</param>
    /// <param name="key">The identity of this component.</param>
    /// <returns>The component content.</returns>
    public static RenderFragment Render(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type,
        IDictionary<string, object> parameters, object? key = null)
    {
        var component = new BpmnComponentType(type);
        return builder =>
        {
            builder.OpenComponent(0, component.Type);
            builder.SetKey(key);
            builder.AddMultipleAttributes(1, parameters);
            builder.CloseComponent();
        };
    }
}
