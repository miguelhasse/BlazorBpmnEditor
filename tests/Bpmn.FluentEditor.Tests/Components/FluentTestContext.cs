using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public abstract class FluentTestContext : BunitContext
{
    protected FluentTestContext()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentUIComponents();
        Services.AddLocalization();
    }
}
