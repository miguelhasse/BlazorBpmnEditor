using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;
using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class DiagramStencilCreationTests : BunitContext
{
    private readonly BunitJSModuleInterop _canvas;

    public DiagramStencilCreationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        KitFixture.Register(Services);
        var module = JSInterop.SetupModule(BpmnInteropContract.ModulePath);
        _canvas = module.SetupModule("initialize", _ => true);
        _canvas.Setup<CanvasInitResult>("getInitResult", _ => true)
            .SetResult(new("creation", BpmnInteropContract.Version, 1, true, null));
    }

    [Fact]
    public async Task UnfocusedCollaborationKeepsCuratedToolsWithoutChoosingAnOwner()
    {
        var editor = RenderEditor(PooledDocument(), new()
        {
            ["Stencil"] = new BpmnStencilOptions(
                new Dictionary<string, BpmnStencilEntryOptions> { ["flowNode/task"] = new(Label: "Work") },
                HiddenGroups: ["Data"]),
        });
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        var expected = session.StencilGroups.SelectMany(group => group.Entries).Select(entry => entry.Key).ToArray();
        Assert.Equal(10, expected.Length);
        Assert.Contains("Work", editor.Find(".bpmn-stencil").TextContent);
        Assert.DoesNotContain(session.StencilGroups, group => group.Title == "Data");
        Assert.Null(session.EffectiveContainerId);
        Assert.Equal(["Swimlanes", "Artifacts"], session.RuleStencilGroups.Select(group => group.Title));

        foreach (var selection in new string[][] { ["Pool"], ["Lane"], ["Task"], [], ["Pool", "Task"], [] })
        {
            await editor.InvokeAsync(() => session.SetSelectionAsync(selection));
            Assert.Equal(expected, session.StencilGroups.SelectMany(group => group.Entries).Select(entry => entry.Key));
            Assert.All(session.StencilGroups.SelectMany(group => group.Entries), entry => Assert.True(entry.IsEnabled));
            Assert.Equal(10, editor.FindAll(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool[draggable='true']").Count);
        }
        Assert.Null(session.EffectiveContainerId);

        var parameters = new Dictionary<string, object>(editor.Instance.Parameters!) { ["IsReadOnly"] = true };
        editor.Render(p => p.Add(component => component.Parameters, parameters));
        Assert.Empty(editor.FindAll(".bpmn-editor__stencil [role='option']"));
    }

    [Theory]
    [InlineData("BlackBox", 0)]
    [InlineData("EventSub", 11)]
    public void ExplicitContainerKeepsItsOwnPaletteRules(string container, int count)
    {
        var document = PooledDocument();
        var editor = RenderEditor(document, new() { ["ActiveContainerId"] = container });
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        Assert.Equal(count, session.StencilGroups.SelectMany(group => group.Entries).Count());
        Assert.Equal(session.RuleStencilGroups.SelectMany(group => group.Entries),
            session.StencilGroups.SelectMany(group => group.Entries));
        if (container == "EventSub")
            Assert.False(session.StencilGroups.SelectMany(group => group.Entries)
                .Single(entry => entry.Type == BpmnFlowNodeType.StartEvent).IsEnabled);
    }

    [Fact]
    public async Task SelectedSpecializedContainersAndOtherPlanesKeepTheirOwnRules()
    {
        var document = PooledDocument();
        var collaboration = document.Diagrams[0];
        document = document with
        {
            Diagrams =
            [
                collaboration,
                new BpmnDiagram("BlackBoxView", collaboration.Plane with
                {
                    Id = "BlackBoxPlane",
                    Shapes = [collaboration.Plane.Shapes.Single(shape => shape.BpmnElement == "BlackBox")],
                }),
                new BpmnDiagram("SubView", new BpmnPlane("SubPlane", "EventSub", [], [])),
            ],
        };
        var editor = RenderEditor(document);
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        await editor.InvokeAsync(() => session.SetSelectionAsync(["BlackBox"]));
        Assert.Empty(session.StencilGroups);
        await editor.InvokeAsync(() => session.SetSelectionAsync(["EventSub"]));
        Assert.False(session.StencilGroups.SelectMany(group => group.Entries)
            .Single(entry => entry.Type == BpmnFlowNodeType.StartEvent).IsEnabled);
        await editor.InvokeAsync(() => session.SetSelectionAsync([]));
        Assert.Equal(12, session.StencilGroups.SelectMany(group => group.Entries).Count());
        await NavigateAsync(editor, session, "BlackBoxView");
        Assert.Equal(["Swimlanes", "Artifacts"], session.StencilGroups.Select(group => group.Title));
        await NavigateAsync(editor, session, "SubView");
        Assert.Equal("EventSub", session.EffectiveContainerId);
        Assert.Equal(session.RuleStencilGroups.SelectMany(group => group.Entries),
            session.StencilGroups.SelectMany(group => group.Entries));
        await NavigateAsync(editor, session, "Diagram_1");
        Assert.Equal(12, session.StencilGroups.SelectMany(group => group.Entries).Count());
    }

    [Theory]
    [InlineData("Pool", true)]
    [InlineData("OtherPool", true)]
    [InlineData("BlackBox", false)]
    [InlineData(null, false)]
    public async Task UnfocusedToolUsesDropOwnerAndRejectsInvalidTargets(string? poolId, bool allowed)
    {
        var document = PooledDocument();
        var editor = RenderEditor(document);
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        Assert.Null(session.EffectiveContainerId);
        var entry = session.StencilGroups.SelectMany(group => group.Entries).Single(candidate => candidate.Key == "flowNode/task");
        _canvas.Setup<DropTarget?>("resolveDrop", _ => true)
            .SetResult(new(new Bounds(450, 200, 100, 80), null, poolId));

        var result = await editor.InvokeAsync(() => session.CreateAtAsync(entry, 500, 300));

        Assert.NotNull(result);
        Assert.Equal(allowed, result.IsApplied);
        if (!allowed)
        {
            Assert.Contains(result.Rejections, rejection => rejection.Code == "CT-001");
            Assert.Same(document, session.EditedDocument);
            Assert.False(session.CanUndo);
            return;
        }
        var created = Assert.Single(result.Document.FlowNodes, node => document.Find(node.Id) is null);
        Assert.Equal(poolId == "OtherPool" ? "Process_2" : "Process_1", created.ContainerId);
        Assert.Equal(poolId, created.PoolId);
        Assert.Single(result.Document.Diagrams[0].Plane.Shapes, shape => shape.BpmnElement == created.Id);
        await editor.InvokeAsync(session.UndoAsync);
        Assert.Equal(document, session.EditedDocument);
        await editor.InvokeAsync(session.RedoAsync);
        Assert.NotNull(session.EditedDocument!.FindFlowNode(created.Id));
    }

    private static BpmnDocument PooledDocument() => new BpmnDocumentBuilder()
        .Pool("Pool", "Pool", new Bounds(0, 0, 600, 400))
        .Lane("Lane", "Lane", new Bounds(30, 0, 570, 400), ["Task", "EventSub"])
        .Node("Task", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
        .Node("EventSub", BpmnFlowNodeType.EventSubProcess, new Bounds(220, 100, 200, 100), isExpanded: true)
        .Process("Process_2")
        .Pool("OtherPool", "Other", new Bounds(0, 500, 600, 400), processRef: "Process_2")
        .BlackBoxPool("BlackBox", "Black box", new Bounds(0, 1000, 600, 100))
        .Build();

    [Theory]
    [InlineData("flow", false)]
    [InlineData("flow", true)]
    [InlineData("annotation", false)]
    [InlineData("group", true)]
    [InlineData("object", false)]
    [InlineData("store", true)]
    [InlineData("intermediate", false)]
    [InlineData("template", true)]
    public async Task DropAndKeyboardCreationUseOpenPlaneAndContainer(string kind, bool keyboard)
    {
        var document = Document();
        var editor = RenderEditor(document);
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        await NavigateAsync(editor, session, "Diagram_2");
        Assert.Equal("Process_2", session.EffectiveContainerId);
        var target = new DropTarget(new Bounds(100, 100, 100, 80), null, null);
        _canvas.Setup<DropTarget?>(keyboard ? "resolveCreationPoint" : "resolveDrop", _ => true).SetResult(target);

        var result = await editor.InvokeAsync(() => keyboard
            ? session.CreateAsync(Entry(kind))
            : session.CreateAtAsync(Entry(kind), 400, 300));

        Assert.NotNull(result);
        Assert.True(result.IsApplied);
        Assert.Equal(document.Diagrams[0], result.Document.Diagrams[0]);
        var shape = Assert.Single(result.Document.Diagrams[1].Plane.Shapes);
        var containerId = result.Document.Find(shape.BpmnElement) switch
        {
            BpmnFlowNode node => node.ContainerId,
            BpmnTextAnnotation annotation => annotation.ContainerId,
            BpmnGroup group => group.ContainerId,
            BpmnDataObjectReference reference => reference.ContainerId,
            BpmnDataStoreReference reference => reference.ContainerId,
            _ => throw new InvalidOperationException("Created element has no expected container."),
        };
        Assert.Equal("Process_2", containerId);
        await editor.InvokeAsync(session.UndoAsync);
        Assert.Empty(session.EditedDocument!.Diagrams[1].Plane.Shapes);
        await editor.InvokeAsync(session.RedoAsync);
        Assert.Equal(shape, Assert.Single(session.EditedDocument!.Diagrams[1].Plane.Shapes));
    }

    [Fact]
    public async Task SubprocessDrillDownUsesSubprocessAsOwner()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Sub", BpmnFlowNodeType.SubProcess, new Bounds(100, 100, 100, 80), isExpanded: false)
            .Build();
        document = document with
        {
            Diagrams = [.. document.Diagrams, new BpmnDiagram("Diagram_2", new BpmnPlane("Plane_2", "Sub", [], []))],
        };
        var editor = RenderEditor(document);
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        _canvas.Setup<DocumentLoaded?>("loadDocument", _ => true).SetResult(new("Diagram_2", 0, 0, []));
        await editor.InvokeAsync(() => session.DrillIntoAsync("Sub"));
        Assert.Equal("Sub", session.EffectiveContainerId);
        _canvas.Setup<DropTarget?>("resolveDrop", _ => true)
            .SetResult(new(new Bounds(100, 100, 100, 80), null, null));

        var result = await editor.InvokeAsync(() => session.CreateAtAsync(Entry("flow"), 400, 300));

        Assert.NotNull(result);
        Assert.True(result.IsApplied);
        var shape = Assert.Single(result.Document.Diagrams[1].Plane.Shapes);
        Assert.Equal("Sub", result.Document.FindFlowNode(shape.BpmnElement)!.ContainerId);
    }

    [Fact]
    public async Task CallPickerRetainsOriginalDiagramAfterNavigation()
    {
        var document = Document();
        var editor = RenderEditor(document);
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        await NavigateAsync(editor, session, "Diagram_2");
        _canvas.Setup<DropTarget?>("resolveDrop", _ => true)
            .SetResult(new(new Bounds(400, 400, 100, 80), null, null));
        await editor.InvokeAsync(() => session.CreateAtAsync(
            new(BpmnFlowNodeType.CallActivity, "Call", Kind: BpmnStencilKind.CallActivity), 400, 300));
        Assert.True(session.IsPickingCalledElement);
        await NavigateAsync(editor, session, "Diagram_1");

        var result = await editor.InvokeAsync(() => session.ChooseCalledElementAsync("Process_1"));

        Assert.NotNull(result);
        Assert.True(result.IsApplied);
        var shape = Assert.Single(result.Document.Diagrams[1].Plane.Shapes);
        Assert.Equal("Process_2", result.Document.FindFlowNode(shape.BpmnElement)!.ContainerId);
        Assert.Equal(document.Diagrams[0], result.Document.Diagrams[0]);
    }

    [Fact]
    public async Task BoundaryDropIgnoresInvisibleHostAtSameCoordinates()
    {
        var document = Document();
        var editor = RenderEditor(document);
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        await NavigateAsync(editor, session, "Diagram_2");
        _canvas.Setup<DropTarget?>("resolveDrop", _ => true)
            .SetResult(new(new Bounds(125, 125, 36, 36), null, null));

        var result = await editor.InvokeAsync(() => session.CreateAtAsync(
            new(BpmnFlowNodeType.IntermediateThrowEvent, "Event", Kind: BpmnStencilKind.IntermediateBoundaryEvent), 400, 300));

        Assert.NotNull(result);
        Assert.True(result.IsApplied);
        var node = Assert.Single(result.Document.FlowNodes, candidate => candidate.Id != "Existing");
        Assert.Equal(BpmnFlowNodeType.IntermediateThrowEvent, node.Type);
        Assert.Null(node.AttachedToRef);
        Assert.Equal("Process_2", node.ContainerId);
    }

    [Fact]
    public async Task BoundaryStencilAttachesToVisibleHostRatherThanOtherPlane()
    {
        var document = Document();
        document = document with
        {
            FlowNodes = [.. document.FlowNodes, new BpmnFlowNode("Visible", BpmnFlowNodeType.Task, "Process_2")],
            Diagrams = [document.Diagrams[0], document.Diagrams[1] with
            {
                Plane = document.Diagrams[1].Plane with
                {
                    Shapes = [new BpmnShape("Visible_di", "Visible", new Bounds(100, 100, 100, 80))],
                },
            }],
        };
        var editor = RenderEditor(document);
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        await NavigateAsync(editor, session, "Diagram_2");
        _canvas.Setup<DropTarget?>("resolveDrop", _ => true)
            .SetResult(new(new Bounds(125, 125, 36, 36), null, null));

        var result = await editor.InvokeAsync(() => session.CreateAtAsync(
            new(BpmnFlowNodeType.BoundaryEvent, "Boundary", BpmnEventDefinitionType.Message,
                Kind: BpmnStencilKind.BoundaryEvent), 400, 300));

        Assert.NotNull(result);
        Assert.True(result.IsApplied);
        var boundary = Assert.Single(result.Document.FlowNodes, node => node.Type == BpmnFlowNodeType.BoundaryEvent);
        Assert.Equal("Visible", boundary.AttachedToRef);
        Assert.Equal("Process_2", boundary.ContainerId);
        Assert.Contains(result.Document.Diagrams[1].Plane.Shapes, shape => shape.BpmnElement == boundary.Id);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PoolAndLaneStencilUseNonRootCollaborationView(bool lane, bool keyboard)
    {
        var document = new BpmnDocumentBuilder()
            .Pool("Pool", "Pool", new Bounds(0, 0, 600, 400))
            .Build();
        var first = document.Diagrams[0];
        document = document with
        {
            Diagrams = [first, new BpmnDiagram("Diagram_2", first.Plane with
            {
                Id = "Plane_2",
                Shapes = [.. first.Plane.Shapes.Select(shape => shape with { Id = shape.Id + "_2" })],
            })],
        };
        var editor = RenderEditor(document);
        var session = editor.FindComponent<BpmnEditorScope>().Instance.Session;
        await NavigateAsync(editor, session, "Diagram_2");
        var entry = new BpmnStencilEntry(BpmnFlowNodeType.Task, "Pool / Lane", Kind: BpmnStencilKind.PoolOrLane);
        var target = new DropTarget(new Bounds(700, 0, 600, 400), null, lane ? "Pool" : null);
        _canvas.Setup<DropTarget?>(keyboard ? "resolveCreationPoint" : "resolveDrop", _ => true).SetResult(target);

        var result = await editor.InvokeAsync(() => keyboard
            ? session.CreateAsync(entry)
            : session.CreateAtAsync(entry, 400, 300));

        Assert.NotNull(result);
        Assert.True(result.IsApplied);
        Assert.Equal(first, result.Document.Diagrams[0]);
        Assert.Equal(2, result.Document.Diagrams[1].Plane.Shapes.Count);
        await editor.InvokeAsync(session.UndoAsync);
        Assert.Equal(document.Processes.Count, session.EditedDocument!.Processes.Count);
        Assert.Single(session.EditedDocument.Diagrams[1].Plane.Shapes);
    }

    private Task NavigateAsync(IRenderedComponent<DynamicComponent> editor, BpmnEditorSession session, string diagramId)
    {
        _canvas.Setup<DocumentLoaded?>("loadDocument", _ => true).SetResult(new(diagramId, 0, 0, []));
        return editor.InvokeAsync(() => session.GoToDiagramAsync(diagramId));
    }

    private IRenderedComponent<DynamicComponent> RenderEditor(BpmnDocument document, Dictionary<string, object>? options = null)
    {
        _canvas.Setup<DocumentLoaded?>("loadDocument", _ => true).SetResult(new("Diagram_1", 1, 0, []));
        var values = options ?? [];
        values["Document"] = document;
        values["ShowStencil"] = true;
        return Render<DynamicComponent>(parameters => parameters
            .Add(component => component.Type, KitFixture.Composition)
            .Add(component => component.Parameters, values));
    }

    private static BpmnStencilEntry Entry(string kind) => kind switch
    {
        "annotation" or "group" => new(BpmnFlowNodeType.Task, "Artifact", Kind: BpmnStencilKind.Artifact,
            Artifact: kind == "group" ? BpmnArtifactKind.Group : BpmnArtifactKind.TextAnnotation),
        "object" or "store" => new(BpmnFlowNodeType.Task, "Data", Kind: BpmnStencilKind.Data,
            Data: kind == "object" ? BpmnDataKind.DataObject : BpmnDataKind.DataStore),
        "intermediate" => new(BpmnFlowNodeType.IntermediateThrowEvent, "Event", Kind: BpmnStencilKind.IntermediateBoundaryEvent),
        "template" => new(BpmnFlowNodeType.ServiceTask, "Templated")
        {
            Template = new BpmnElementTemplate { Id = "Test", Name = "Test", AppliesTo = ["bpmn:ServiceTask"] },
        },
        _ => new(BpmnFlowNodeType.Task, "Task"),
    };

    private static BpmnDocument Document()
    {
        var document = new BpmnDocumentBuilder()
            .Process("Process_2")
            .Node("Existing", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Build();
        return document with
        {
            Diagrams = [.. document.Diagrams, new BpmnDiagram("Diagram_2", new BpmnPlane("Plane_2", "Process_2", [], []))],
        };
    }
}
