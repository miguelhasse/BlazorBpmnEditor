using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

internal static partial class TestScope
{
    static partial void ConfigureKit(IServiceCollection services) => services.AddBpmnEditor();
}
