using AngleSharp.Dom;
using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class StencilLayoutTests : BunitContext
{
    public StencilLayoutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        KitFixture.Register(Services);
        Services.Configure<BpmnOptions>(options =>
        {
            options.Templates.Registry.Add(Template("http", "HTTP request"));
            options.Templates.Registry.Add(Template("mail", "Send mail"));
        });
    }

    [Fact]
    public void ShowLabelsDefaultsToLabeledMarkup()
    {
        var target = Panel();

        Assert.True(target.Cut.FindComponent<BpmnStencilPanelBase>().Instance.ShowLabels);
        Assert.False(target.Cut.Find(".bpmn-stencil").ClassList.Contains("bpmn-stencil--icons"));
        Assert.True(target.Cut.Find(".bpmn-stencil").ClassList.Contains("host-stencil"));
        Assert.Equal(12, target.Cut.FindAll(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__label").Count);
        Assert.Equal(2, target.Cut.FindAll(".bpmn-stencil__template-tool .bpmn-stencil__label").Count);
        Assert.Equal("Templates", target.Cut.Find("summary").TextContent);
    }

    [Fact]
    public async Task TogglingLabelsPreservesNamesSearchAndPanelInstance()
    {
        var target = Panel();
        await KitFixture.SearchStencilAsync(target.Cut, "http");
        var tools = target.Cut.FindAll(".bpmn-stencil__tool").ToArray();
        var names = tools.Select(tool => tool.QuerySelector(".bpmn-stencil__label")!.TextContent).ToArray();
        var titles = tools.Select(tool => tool.GetAttribute("title")).ToArray();
        var panel = target.Cut.FindComponent<BpmnStencilPanelBase>().Instance;

        target.Cut.Render(parameters => parameters.Add(component => component.Parameters, Parameters(false)));

        Assert.True(target.Cut.Find(".bpmn-stencil").ClassList.Contains("bpmn-stencil--icons"));
        Assert.Same(panel, target.Cut.FindComponent<BpmnStencilPanelBase>().Instance);
        Assert.Equal(names, target.Cut.FindAll(".bpmn-stencil__label").Select(label => label.TextContent));
        Assert.Equal(titles, target.Cut.FindAll(".bpmn-stencil__tool").Select(tool => tool.GetAttribute("title")));
        Assert.All(target.Cut.FindAll("[role='group']"), group => Assert.False(string.IsNullOrEmpty(group.GetAttribute("aria-label"))));
        Assert.Single(target.Cut.FindAll(".bpmn-stencil__template-tool"));
        Assert.Equal("HTTP request", target.Cut.Find(".bpmn-stencil__template-tool .bpmn-stencil__label").TextContent);
        Assert.Equal("Templates", target.Cut.Find("summary .bpmn-stencil__summary-label").TextContent);

        target.Cut.Render(parameters => parameters.Add(component => component.Parameters, Parameters(true)));

        Assert.False(target.Cut.Find(".bpmn-stencil").ClassList.Contains("bpmn-stencil--icons"));
        Assert.Same(panel, target.Cut.FindComponent<BpmnStencilPanelBase>().Instance);
        Assert.Single(target.Cut.FindAll(".bpmn-stencil__template-tool"));
        await KitFixture.SearchStencilAsync(target.Cut, "no match");
        Assert.Empty(target.Cut.FindAll(".bpmn-stencil__template-tool"));
        Assert.Equal("No matching templates.", target.Cut.Find("[role='status']").TextContent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothLayoutsPreserveArmingAndKeyboardActivation(bool showLabels)
    {
        var target = Panel(showLabels);
        BpmnStencilEntry? activated = null;
        target.Session.ActivateToolOverride = entry =>
        {
            activated = entry;
            return Task.CompletedTask;
        };
        TaskTool(target.Cut).DragStart(new DragEventArgs());
        Assert.Equal(BpmnFlowNodeType.Task, target.Session.State.ArmedTool!.Type);
        TaskTool(target.Cut).DragEnd(new DragEventArgs());
        Assert.Null(target.Session.State.ArmedTool);
        TaskTool(target.Cut).KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal(BpmnFlowNodeType.Task, activated!.Type);
        activated = null;
        target.Cut.Find(".bpmn-stencil__template-tool").KeyDown(new KeyboardEventArgs { Key = " " });
        Assert.NotNull(activated!.Template);
        await KitFixture.SearchStencilAsync(target.Cut, "connectors");
        Assert.Equal(2, target.Cut.FindAll(".bpmn-stencil__template-tool").Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DisabledToolsKeepNamesAndReasonsInBothLayouts(bool showLabels)
    {
        var document = new BpmnDocumentBuilder()
            .Node("EventSub", BpmnFlowNodeType.EventSubProcess, new Bounds(100, 100, 400, 300), isExpanded: true)
            .Build();
        var target = Panel(showLabels, document, "EventSub");
        var disabled = Assert.Single(target.Cut.FindAll(".bpmn-stencil__tool--disabled"));

        Assert.Equal("Start Event", disabled.QuerySelector(".bpmn-stencil__label")!.TextContent);
        Assert.Contains("EV-004", disabled.GetAttribute("title"));
        Assert.Equal("true", disabled.GetAttribute("aria-disabled"));
        Assert.Equal("false", disabled.GetAttribute("draggable"));
        Assert.Equal("-1", disabled.GetAttribute("tabindex"));
    }

    private TestScope<DynamicComponent> Panel(bool? showLabels = null, BpmnDocument? document = null, string? containerId = null) =>
        TestScope.Render<DynamicComponent>(this,
            parameters => parameters
                .Add(component => component.Type, KitFixture.Assembly.GetTypes()
                    .Single(type => !type.IsAbstract && typeof(BpmnStencilPanelBase).IsAssignableFrom(type)))
                .Add(component => component.Parameters, Parameters(showLabels)),
            scope => scope.Add(component => component.Document, document ?? new BpmnDocumentBuilder().Build())
                .Add(component => component.ActiveContainerId, containerId));

    private static Dictionary<string, object> Parameters(bool? showLabels)
    {
        var parameters = new Dictionary<string, object> { ["Class"] = "host-stencil" };
        if (showLabels is { } value)
            parameters["ShowLabels"] = value;
        return parameters;
    }

    private static IElement TaskTool(IRenderedComponent<DynamicComponent> panel) =>
        panel.FindAll(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool")
            .Single(tool => tool.QuerySelector(".bpmn-stencil__label")!.TextContent == "Task");

    private static BpmnElementTemplate Template(string id, string name) => new()
    {
        Id = id,
        Name = name,
        AppliesTo = ["bpmn:Task"],
        ElementType = new("bpmn:ServiceTask"),
        Category = new("connectors", "Connectors"),
        Description = "Call an endpoint",
    };
}
