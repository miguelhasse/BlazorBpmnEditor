using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class DiagramRoutingTests : BunitContext
{
    private readonly BunitJSModuleInterop _canvas;

    public DiagramRoutingTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        KitFixture.Register(Services);
        var module = JSInterop.SetupModule(BpmnInteropContract.ModulePath);
        _canvas = module.SetupModule("initialize", _ => true);
        _canvas.Setup<CanvasInitResult>("getInitResult", _ => true)
            .SetResult(new("routing", BpmnInteropContract.Version, 1, true, null));
    }

    [Theory]
    [InlineData("connect")]
    [InlineData("move")]
    [InlineData("resize")]
    [InlineData("reconnect")]
    [InlineData("reroute")]
    public async Task GeometryGesturesTargetOpenDiagram(string kind)
    {
        var document = Document();
        var editor = RenderEditor(document);
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        await NavigateAsync(editor, session, "Diagram_2");

        await editor.InvokeAsync(() => session.HandleEditRequestedAsync(Request(kind)));

        var edited = session.EditedDocument!;
        Assert.Equal(document.Diagrams[0], edited.Diagrams[0]);
        if (kind == "connect")
        {
            var flow = Assert.Single(edited.SequenceFlows, flow => flow.Id != "Flow");
            Assert.Contains(edited.Diagrams[1].Plane.Edges, edge => edge.BpmnElement == flow.Id);
        }
        else
        {
            Assert.Equal(Route(kind is "move" or "resize"), Assert.Single(edited.Diagrams[1].Plane.Edges).Waypoints);
        }

        if (kind is "move" or "resize")
        {
            Assert.Equal(new Bounds(100, 100, 120, 80), edited.Diagrams[1].Plane.Shapes[0].Bounds);
        }

        await editor.InvokeAsync(session.UndoAsync);
        Assert.Equal(document, session.EditedDocument);
        await editor.InvokeAsync(session.RedoAsync);
        Assert.Equal(edited, session.EditedDocument);
    }

    [Theory]
    [InlineData("connect")]
    [InlineData("move")]
    [InlineData("resize")]
    [InlineData("reconnect")]
    [InlineData("reroute")]
    public async Task QueuedGestureKeepsOriginatingDiagramAfterNavigation(string kind)
    {
        var document = Document();
        var editor = RenderEditor(document);
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;

        await editor.InvokeAsync(() => session.HandleEditRequestedAsync(Request(kind) with { DiagramId = "Diagram_2" }));

        Assert.Equal("Diagram_1", session.ActiveDiagramId);
        Assert.Equal(document.Diagrams[0], session.EditedDocument!.Diagrams[0]);
        Assert.NotEqual(document.Diagrams[1], session.EditedDocument.Diagrams[1]);
    }

    [Fact]
    public async Task ContextualAppendCreatesShapeAndEdgeOnOpenDiagram()
    {
        var document = Document();
        var editor = RenderEditor(document);
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        await NavigateAsync(editor, session, "Diagram_2");
        var bounds = document.Diagrams[1].Plane.Shapes[0].Bounds;
        await editor.InvokeAsync(() => session.HandleSelectionChangedAsync(
            new(["Source"], new Anchor(bounds, bounds))));
        _canvas.Setup<DropTarget?>("resolveAppendPoint", _ => true)
            .SetResult(new(new Bounds(500, 100, 100, 80), "Process_1", null));

        await editor.InvokeAsync(() => session.InvokeContextualActionAsync(
            new(BpmnActionKind.Append, "Append Task", string.Empty, BpmnFlowNodeType.Task)));

        var edited = session.EditedDocument!;
        Assert.Equal(document.Diagrams[0], edited.Diagrams[0]);
        var node = Assert.Single(edited.FlowNodes, node => node.Id is not "Source" and not "Target");
        var flow = Assert.Single(edited.SequenceFlows, flow => flow.Id != "Flow");
        Assert.Contains(edited.Diagrams[1].Plane.Shapes, shape => shape.BpmnElement == node.Id);
        Assert.Contains(edited.Diagrams[1].Plane.Edges, edge => edge.BpmnElement == flow.Id);
        await editor.InvokeAsync(session.UndoAsync);
        Assert.Equal(document, session.EditedDocument);
    }

    [Fact]
    public async Task NavigationInvalidatesCachedSnapshotGeometry()
    {
        var document = Document();
        var editor = RenderEditor(document);
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        var root = session.Describe("Source");
        await NavigateAsync(editor, session, "Diagram_2");

        var child = session.Describe("Source");

        Assert.NotNull(root);
        Assert.NotNull(child);
        Assert.Equal(document.Diagrams[0].Plane.Shapes[0].Bounds, root.Bounds);
        Assert.Equal(document.Diagrams[1].Plane.Shapes[0].Bounds, child.Bounds);
        Assert.NotEqual(root.Bounds, child.Bounds);
    }

    [Fact]
    public async Task SnapshotKeepsGeometryForElementOpenDiagramDoesNotDraw()
    {
        var document = Document();
        document = document with
        {
            Diagrams = [document.Diagrams[0], document.Diagrams[1] with
            {
                Plane = document.Diagrams[1].Plane with { Shapes = [document.Diagrams[1].Plane.Shapes[1]] },
            }],
        };
        var editor = RenderEditor(document);
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        await NavigateAsync(editor, session, "Diagram_2");

        var snapshot = session.Describe("Source");

        Assert.NotNull(snapshot);
        Assert.Equal(document.Diagrams[0].Plane.Shapes[0].Bounds, snapshot.Bounds);
    }

    [Fact]
    public async Task PendingAppendKeepsOriginalDiagramAfterNavigation()
    {
        var document = Document();
        var editor = RenderEditor(document);
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        await NavigateAsync(editor, session, "Diagram_2");
        var bounds = document.Diagrams[1].Plane.Shapes[0].Bounds;
        await editor.InvokeAsync(() => session.HandleSelectionChangedAsync(
            new(["Source"], new Anchor(bounds, bounds))));
        var placement = _canvas.Setup<DropTarget?>("resolveAppendPoint", _ => true);
        var append = editor.InvokeAsync(() => session.InvokeContextualActionAsync(
            new(BpmnActionKind.Append, "Append Task", string.Empty, BpmnFlowNodeType.Task)));
        editor.WaitForAssertion(() => Assert.Single(_canvas.Invocations["resolveAppendPoint"]));
        await NavigateAsync(editor, session, "Diagram_1");

        placement.SetResult(new(new Bounds(500, 100, 100, 80), "Process_1", null));
        await append;

        var edited = session.EditedDocument!;
        Assert.Equal("Diagram_1", session.ActiveDiagramId);
        Assert.Equal(document.Diagrams[0], edited.Diagrams[0]);
        var flow = Assert.Single(edited.SequenceFlows, flow => flow.Id != "Flow");
        Assert.Contains(edited.Diagrams[1].Plane.Edges, edge => edge.BpmnElement == flow.Id);
    }

    [Fact]
    public async Task ResizeOnMissingDiagramReportsRejection()
    {
        var document = Document();
        var editor = RenderEditor(document);
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        EditRejected? rejection = null;
        session.OnEditRejected = EventCallback.Factory.Create<EditRejected>(this, value => rejection = value);

        await editor.InvokeAsync(() => session.HandleEditRequestedAsync(Request("resize") with { DiagramId = "Missing" }));

        Assert.NotNull(rejection);
        Assert.Equal("EDIT-011", rejection.Code);
        Assert.Equal(document, session.EditedDocument);
        Assert.False(session.CanUndo);
    }

    private IRenderedComponent<DynamicComponent> RenderEditor(BpmnDocument document)
    {
        _canvas.Setup<DocumentLoaded?>("loadDocument", _ => true).SetResult(new("Diagram_1", 2, 1, []));
        return Render<DynamicComponent>(parameters => parameters
            .Add(component => component.Type, KitFixture.Composition)
            .Add(component => component.Parameters, new Dictionary<string, object> { ["Document"] = document }));
    }

    private Task NavigateAsync(IRenderedComponent<DynamicComponent> editor, BpmnEditorSession session, string diagramId)
    {
        _canvas.Setup<DocumentLoaded?>("loadDocument", _ => true).SetResult(new(diagramId, 2, 1, []));
        return editor.InvokeAsync(() => session.GoToDiagramAsync(diagramId));
    }

    private static EditRequest Request(string kind) => kind switch
    {
        "connect" => new(kind, ConnectorKind: "sequenceFlow", SourceRef: "Source", TargetRef: "Target", Waypoints: Route()),
        "move" => new(kind, Moves: [new("Source_2_di", new Bounds(100, 100, 120, 80))],
            Routes: [new("Flow", Route(true))]),
        "resize" => new(kind, ElementId: "Source", Bounds: new Bounds(100, 100, 120, 80),
            Routes: [new("Flow", Route(true))]),
        "reconnect" => new(kind, ElementId: "Flow", SourceRef: "Source", TargetRef: "Target", Waypoints: Route()),
        _ => new(kind, ElementId: "Flow", Waypoints: Route()),
    };

    private static IReadOnlyList<DiPoint> Route(bool resized = false) =>
        [new(resized ? 220 : 200, 140), new(250, 140), new(250, 240), new(300, 240)];

    private static BpmnDocument Document()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Source", BpmnFlowNodeType.Task, new Bounds(100, 100, 120, 80))
            .Node("Target", BpmnFlowNodeType.Task, new Bounds(300, 200, 100, 80))
            .Flow("Flow", "Source", "Target", [new(220, 140), new(250, 140), new(250, 240), new(300, 240)])
            .Build();
        return document with
        {
            Diagrams = [document.Diagrams[0], new BpmnDiagram("Diagram_2", new BpmnPlane("Plane_2", "Process_1",
                [new BpmnShape("Source_2_di", "Source", new Bounds(100, 100, 100, 80)),
                 new BpmnShape("Target_2_di", "Target", new Bounds(300, 200, 100, 80))],
                [new BpmnEdge("Flow_2_di", "Flow", [new(200, 140), new(200, 240), new(300, 240)])]))],
        };
    }
}
