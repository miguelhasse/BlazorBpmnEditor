using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

internal static class KitFixture
{
    public static Task ChangeColorAsync(IRenderedComponent<Microsoft.AspNetCore.Components.DynamicComponent> editor,
        string channel, string color)
    {
        editor.Find($"input[data-color-channel='{channel}']").Change(color);
        return Task.CompletedTask;
    }

    public static Task ChangeMessageNameAsync(IRenderedComponent<Microsoft.AspNetCore.Components.DynamicComponent> editor, string value)
    {
        editor.Find("[data-message-name]").Change(value);
        return Task.CompletedTask;
    }
    public static Task SelectMessageAsync(IRenderedComponent<Microsoft.AspNetCore.Components.DynamicComponent> editor, string value)
    {
        editor.Find(".bpmn-message-reference select").Change(value);
        return Task.CompletedTask;
    }
    public static Task ConfirmAnnotationNormalizationAsync(IRenderedComponent<BpmnPart> offer)
    {
        offer.Find(".bpmn-import__annotation-consent").Change(true);
        return Task.CompletedTask;
    }

    public static Task ChangeNameAsync(Bunit.IRenderedComponent<Microsoft.AspNetCore.Components.DynamicComponent> editor, string value)
    {
        editor.FindAll(".bpmn-properties input[type=text]").Single(input => input.GetAttribute("value") is "Work" or "Changed").Change(value);
        return Task.CompletedTask;
    }
    public static Type Composition => typeof(BpmnEditor);
    public static Type DiagramNavigator => typeof(BpmnDiagramNavigator);
    public static Task PickMenuTypeAsync(IRenderedComponent<BpmnPart> menu, string value)
    {
        menu.Find(".bpmn-menu__type-select").Change(value);
        return Task.CompletedTask;
    }
    public static string? MenuTypeValue(IRenderedComponent<BpmnPart> menu) =>
        ((AngleSharp.Html.Dom.IHtmlSelectElement)menu.Find(".bpmn-menu__type-select")).Value;
    public static Task PickDiagramAsync(IRenderedComponent<Microsoft.AspNetCore.Components.DynamicComponent> editor, string diagramId)
    {
        editor.Find(".bpmn-diagrams__select").Change(diagramId);
        return Task.CompletedTask;
    }
    public static Assembly Assembly => Composition.Assembly;
    public static void Register(IServiceCollection services) => services.AddBpmnEditor();
}
