using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

internal static partial class TestScope
{
    static partial void ConfigureKit(IServiceCollection services) { services.AddFluentUIComponents(); services.AddFluentBpmnEditor(); }
}
