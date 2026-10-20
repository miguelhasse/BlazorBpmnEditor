using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

internal static class KitFixture
{
    public static Task ChangeNameAsync(Bunit.IRenderedComponent<Microsoft.AspNetCore.Components.DynamicComponent> editor, string value)
    {
        editor.FindAll(".bpmn-properties input[type=text]").Single(input => input.GetAttribute("value") is "Work" or "Changed").Change(value);
        return Task.CompletedTask;
    }
    public static Type Composition => typeof(BpmnEditor);
    public static Type DiagramNavigator => typeof(BpmnDiagramNavigator);
    public static Task PickDiagramAsync(IRenderedComponent<Microsoft.AspNetCore.Components.DynamicComponent> editor, string diagramId)
    {
        editor.Find(".bpmn-diagrams__select").Change(diagramId);
        return Task.CompletedTask;
    }
    public static Assembly Assembly => Composition.Assembly;
    public static void Register(IServiceCollection services) => services.AddBpmnEditor();
}
