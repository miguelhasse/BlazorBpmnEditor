using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class DiagramNavigationCallbackTests : BunitContext
{
    [Fact]
    public async Task SelectingDiagramsNotifiesEditorWithPickerPath()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        KitFixture.Register(Services);
        var module = JSInterop.SetupModule(BpmnInteropContract.ModulePath);
        var canvas = module.SetupModule("initialize", _ => true);
        canvas.Setup<CanvasInitResult>("getInitResult", _ => true)
            .SetResult(new("navigation", BpmnInteropContract.Version, 1, true, null));
        canvas.Setup<DocumentLoaded?>("loadDocument", invocation => invocation.Arguments[1] is null or "Diagram_1")
            .SetResult(new("Diagram_1", 1, 0, []));
        canvas.Setup<DocumentLoaded?>("loadDocument", invocation => invocation.Arguments[1] is "Diagram_2")
            .SetResult(new("Diagram_2", 1, 0, []));

        var document = new BpmnDocumentBuilder()
            .Process("Process_2")
            .Node("Call_1", BpmnFlowNodeType.CallActivity, new Bounds(100, 100, 100, 80))
            .Node("Task_2", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80), containerId: "Process_2")
            .Build();
        document = document with
        {
            FlowNodes = [.. document.FlowNodes.Select(node => node.Id == "Call_1"
                ? node with { CalledElementRef = "Process_2" } : node)],
            Diagrams =
            [
                new BpmnDiagram("Diagram_1", new BpmnPlane("Plane_1", "Process_1",
                    [document.Diagrams[0].Plane.Shapes[0]], []), "Main"),
                new BpmnDiagram("Diagram_2", new BpmnPlane("Plane_2", "Process_2",
                    [document.Diagrams[0].Plane.Shapes[1]], []), "Shipment"),
            ],
        };
        var notifications = new List<BpmnDiagramNavigated>();
        BpmnEditorSession session = null!;
        RenderFragment toolbar = builder =>
        {
            builder.OpenComponent(0, KitFixture.DiagramNavigator);
            builder.CloseComponent();
        };
        var parameters = new Dictionary<string, object>
        {
            ["Document"] = document,
            ["ToolbarContent"] = toolbar,
            ["OnDiagramNavigated"] = EventCallback.Factory.Create<BpmnDiagramNavigated>(this, notification =>
            {
                Assert.Equal(notification.Path, session.NavigationPath);
                Assert.Equal(notification.DiagramId, session.State.ActiveDiagramId);
                notifications.Add(notification);
            }),
        };
        var editor = Render<DynamicComponent>(builder => builder
            .Add(component => component.Type, KitFixture.Composition)
            .Add(component => component.Parameters, parameters));
        session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        var bridge = Assert.IsType<DotNetObjectReference<BpmnCanvasEventBridge>>(
            Assert.Single(JSInterop.Invocations["initialize"]).Arguments[4]).Value;
        await editor.InvokeAsync(() => bridge.DocumentLoaded(new("Diagram_1", 1, 0, [])));
        editor.WaitForAssertion(() => Assert.Equal(BpmnNavigationOrigin.Load, Assert.Single(notifications).Origin));
        notifications.Clear();

        await KitFixture.PickDiagramAsync(editor, "Diagram_2");

        editor.WaitForAssertion(() =>
        {
            var notification = Assert.Single(notifications);
            Assert.Equal("Diagram_2", notification.DiagramId);
            Assert.Equal(BpmnNavigationOrigin.Picker, notification.Origin);
            Assert.Equal(
                [new BpmnDiagramStep("Diagram_1", "Main"), new BpmnDiagramStep("Diagram_2", "Shipment", "Call_1")],
                notification.Path);
            Assert.Equal(notification.Path, session.NavigationPath);
            Assert.Equal("Diagram_2", session.ActiveDiagramId);
            Assert.Equal("Diagram_2", editor.FindComponent<BpmnCanvas>().Instance.DiagramId);
        });

        editor.Render(builder => builder.Add(component => component.Parameters, parameters));
        Assert.Single(notifications);
        Assert.Equal("Diagram_2", session.ActiveDiagramId);
        notifications.Clear();

        await KitFixture.PickDiagramAsync(editor, "Diagram_1");

        editor.WaitForAssertion(() =>
        {
            var notification = Assert.Single(notifications);
            Assert.Equal("Diagram_1", notification.DiagramId);
            Assert.Equal(BpmnNavigationOrigin.Picker, notification.Origin);
            Assert.Equal(new BpmnDiagramStep("Diagram_1", "Main"), Assert.Single(notification.Path));
            Assert.Equal(notification.Path, session.NavigationPath);
            Assert.Equal("Diagram_1", session.ActiveDiagramId);
            Assert.Equal("Diagram_1", editor.FindComponent<BpmnCanvas>().Instance.DiagramId);
        });
    }
}
