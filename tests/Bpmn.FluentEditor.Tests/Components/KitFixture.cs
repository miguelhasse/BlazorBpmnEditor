using System.Reflection;
using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FluentUI.AspNetCore.Components;
namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

internal static class KitFixture
{
    public static Task ConfirmAnnotationNormalizationAsync(IRenderedComponent<BpmnPart> offer) =>
        offer.InvokeAsync(() => offer.FindComponent<FluentCheckbox>().Instance.ValueChanged.InvokeAsync(true));

    public static async Task ChangeNameAsync(Bunit.IRenderedComponent<Microsoft.AspNetCore.Components.DynamicComponent> editor, string value)
    {
        var input = editor.FindComponents<FluentTextInput>().Single(component => component.Instance.Value is "Work" or "Changed");
        await input.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync(value));
        await input.InvokeAsync(() => input.Instance.OnFocusOut.InvokeAsync(new Microsoft.AspNetCore.Components.Web.FocusEventArgs()));
    }
    public static Type Composition => typeof(FluentBpmnEditor);
    public static Type DiagramNavigator => typeof(FluentBpmnDiagramNavigator);
    public static Task PickMenuTypeAsync(IRenderedComponent<BpmnPart> menu, string value) =>
        menu.InvokeAsync(() => menu.FindComponent<FluentSelect<BpmnTypeOption, string>>()
            .Instance.ValueChanged.InvokeAsync(value));
    public static string? MenuTypeValue(IRenderedComponent<BpmnPart> menu) =>
        menu.FindComponent<FluentSelect<BpmnTypeOption, string>>().Instance.Value;
    public static Task PickDiagramAsync(IRenderedComponent<Microsoft.AspNetCore.Components.DynamicComponent> editor, string diagramId) =>
        editor.InvokeAsync(() => editor.FindComponent<FluentSelect<BpmnDiagramTarget, string>>()
            .Instance.ValueChanged.InvokeAsync(diagramId));
    public static Assembly Assembly => Composition.Assembly;
    public static void Register(IServiceCollection services)
    {
        services.AddFluentUIComponents();
        services.AddFluentBpmnEditor();
    }
}
