using System.Reflection;
using Bunit;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

internal static class KitFixture
{
    public static async Task ChangeNameAsync(Bunit.IRenderedComponent<Microsoft.AspNetCore.Components.DynamicComponent> editor, string value)
    {
        var input = editor.FindComponents<FluentTextInput>().Single(component => component.Instance.Value is "Work" or "Changed");
        await input.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync(value));
        await input.InvokeAsync(() => input.Instance.OnFocusOut.InvokeAsync(new Microsoft.AspNetCore.Components.Web.FocusEventArgs()));
    }
    public static Type Composition => typeof(FluentBpmnEditor);
    public static Assembly Assembly => Composition.Assembly;
    public static void Register(IServiceCollection services)
    {
        services.AddFluentUIComponents();
        services.AddFluentBpmnEditor();
    }
}
