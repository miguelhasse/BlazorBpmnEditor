using System.Diagnostics.CodeAnalysis;

namespace Hasseware.AspNetCore.Components.Bpmn.Configuration;

internal sealed class BpmnComponentType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type type)
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    internal Type Type { get; } = type;
}
