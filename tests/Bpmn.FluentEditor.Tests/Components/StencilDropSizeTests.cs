using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>
/// What the editor asks the canvas to size a drop as (specification section 9.3).
/// </summary>
/// <remarks>
/// <para>
/// The canvas sizes a drop from a type name, and the editor's job is to send the right one. For a
/// Flow Node that is just the wire name, but a Pool, a Lane, an artifact and a data reference are
/// not Flow Nodes at all — their <c>Type</c> is a placeholder the command never reads. Sending it
/// anyway returns the Task rectangle, and the drop is then created at 100x80 whatever it is.
/// </para>
/// <para>
/// This was not cosmetic and not confined to drawing. The size the canvas reports is the size the
/// creation command uses, so it decides which container the drop resolves into and whether the drop
/// collides with what is already there (DI-011). A Group that arrives Task-sized is a frame too
/// small to put anything inside.
/// </para>
/// </remarks>
public sealed class StencilDropSizeTests : FluentTestContext
{
    private const string InstanceId = "instance-1";

    private readonly BunitJSModuleInterop _module;

    /// <summary>The canvas that <c>initialize</c> returns, on which every later call is made.</summary>
    private readonly BunitJSModuleInterop _canvas;

    public StencilDropSizeTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddFluentBpmnEditor();
        _module = JSInterop.SetupModule(BpmnInteropContract.ModulePath);
        _canvas = _module.SetupModule("initialize", _ => true);
        _canvas.Setup<CanvasInitResult>("getInitResult", _ => true)
            .SetResult(new CanvasInitResult(InstanceId, BpmnInteropContract.Version, 1, true, null));
    }

    [Theory]
    [InlineData(BpmnStencilKind.Data, BpmnDataKind.DataObject, "dataObjectReference")]
    [InlineData(BpmnStencilKind.Data, BpmnDataKind.DataStore, "dataStoreReference")]
    public async Task ADataReferenceIsSizedAsItself(BpmnStencilKind kind, BpmnDataKind data, string expected)
    {
        Assert.Equal(expected, await DropTypeNameAsync(new BpmnStencilEntry(
            BpmnFlowNodeType.Task,
            "Data",
            Kind: kind,
            Data: data)));
    }

    [Theory]
    [InlineData(BpmnArtifactKind.TextAnnotation, "textAnnotation")]
    [InlineData(BpmnArtifactKind.Group, "group")]
    public async Task AnArtifactIsSizedAsItself(BpmnArtifactKind artifact, string expected)
    {
        Assert.Equal(expected, await DropTypeNameAsync(new BpmnStencilEntry(
            BpmnFlowNodeType.Task,
            "Artifact",
            Kind: BpmnStencilKind.Artifact,
            Artifact: artifact)));
    }

    [Theory]
    [InlineData(BpmnStencilKind.Participant, "participant")]
    [InlineData(BpmnStencilKind.Lane, "lane")]
    [InlineData(BpmnStencilKind.PoolOrLane, "poolOrLane")]
    public async Task ASwimlaneIsStillSizedAsItself(BpmnStencilKind kind, string expected)
    {
        // These two were already right; they are here so a future edit cannot fix one family by
        // breaking the other.
        Assert.Equal(expected, await DropTypeNameAsync(new BpmnStencilEntry(
            BpmnFlowNodeType.Task,
            "Swimlane",
            Kind: kind)));
    }

    [Fact]
    public async Task AdaptiveEventUsesIntermediateEventGeometry()
    {
        Assert.Equal("intermediateThrowEvent", await DropTypeNameAsync(new BpmnStencilEntry(
            BpmnFlowNodeType.IntermediateThrowEvent,
            "Intermediate / Boundary Event",
            Kind: BpmnStencilKind.IntermediateBoundaryEvent)));
    }

    [Fact]
    public async Task AdaptiveEventCreatesBoundaryOnActivityDropAndIntermediateElsewhere()
    {
        var entry = new BpmnStencilEntry(
            BpmnFlowNodeType.IntermediateThrowEvent,
            "Intermediate / Boundary Event",
            Kind: BpmnStencilKind.IntermediateBoundaryEvent);
        var bounds = new Bounds(232, 122, 36, 36);

        var boundaryDocument = await CreateAtAsync(
            Document(),
            entry,
            new DropTarget(bounds, "Process_1", null));
        var boundary = Assert.Single(boundaryDocument.FlowNodes, node => node.Type == BpmnFlowNodeType.BoundaryEvent);
        Assert.Equal(BpmnEventDefinitionType.Message, boundary.EventDefinition);
        Assert.Equal("Task_1", boundary.AttachedToRef);

        var intermediateDocument = await CreateAtAsync(
            Document(),
            entry,
            new DropTarget(new Bounds(500, 400, 36, 36), "Process_1", null));
        var intermediate = Assert.Single(
            intermediateDocument.FlowNodes,
            node => node.Type == BpmnFlowNodeType.IntermediateThrowEvent);
        Assert.Equal(BpmnEventDefinitionType.None, intermediate.EventDefinition);
    }

    [Fact]
    public async Task PoolLaneToolCreatesLaneInsidePoolAndPoolOutside()
    {
        var entry = new BpmnStencilEntry(
            BpmnFlowNodeType.Task,
            "Pool / Lane",
            Kind: BpmnStencilKind.PoolOrLane);
        var document = PooledDocument() with
        {
            FlowNodes =
            [
                new BpmnFlowNode(
                    "SubProcess_1",
                    BpmnFlowNodeType.SubProcess,
                    "Process_1",
                    "Handle",
                    FlowElementsOwned: true),
            ],
        };

        var laneDocument = await CreateAtAsync(
            document,
            entry,
            new DropTarget(new Bounds(200, 200, 600, 125), "SubProcess_1", "Participant_1"));
        Assert.Single(laneDocument.Lanes);
        Assert.Single(laneDocument.Collaboration!.Participants);
        Assert.Equal("Process_1", Assert.Single(laneDocument.LaneSets).ContainerId);

        var poolDocument = await CreateAtAsync(
            document,
            entry,
            new DropTarget(new Bounds(800, 0, 600, 250), null, null));
        Assert.Equal(2, poolDocument.Collaboration!.Participants.Count);
        Assert.Equal(2, poolDocument.Processes.Count);
    }

    [Fact]
    public async Task CallActivityToolWaitsForAndUsesTheChosenDefinition()
    {
        var entry = new BpmnStencilEntry(
            BpmnFlowNodeType.CallActivity,
            "Call Activity",
            Kind: BpmnStencilKind.CallActivity);
        _canvas.Setup<DropTarget?>("resolveDrop", _ => true)
            .SetResult(new DropTarget(new Bounds(500, 400, 100, 80), "Process_1", null));
        var editor = RenderEditor(Document());

        var result = await editor.InvokeAsync(() => editor.Instance.CreateAtAsync(entry, 500, 400));

        Assert.Null(result);
        Assert.True(editor.Instance.IsPickingCalledElement);
        Assert.Contains(editor.Instance.CallableDefinitions, definition => definition.Id == "Process_1");

        var chosen = await editor.InvokeAsync(() => editor.Instance.ChooseCalledElementAsync("Process_1"));

        Assert.NotNull(chosen);
        Assert.True(chosen.IsApplied);
        var callActivity = Assert.Single(
            editor.Instance.EditedDocument!.FlowNodes,
            node => node.Type == BpmnFlowNodeType.CallActivity);
        Assert.Equal("Process_1", callActivity.CalledElementRef);
    }

    [Fact]
    public async Task AFlowNodeIsStillSizedByItsWireName()
    {
        Assert.Equal("userTask", await DropTypeNameAsync(new BpmnStencilEntry(
            BpmnFlowNodeType.UserTask,
            "User Task")));
    }

    // --- helpers ------------------------------------------------------------------

    /// <summary>
    /// Drops the entry and reports the type name the editor asked the canvas to size it as.
    /// </summary>
    /// <remarks>
    /// Read off the interop call rather than from the method, which is private: what matters is the
    /// value that crosses the boundary, since that is what the canvas actually measures by.
    /// </remarks>
    private async Task<string> DropTypeNameAsync(BpmnStencilEntry entry)
    {
        // Null: the drop resolves nowhere, so nothing is created. The call itself is the subject.
        var resolve = _canvas.Setup<DropTarget?>("resolveDrop", _ => true);
        resolve.SetResult(null);
        var editor = RenderEditor(Document());
        await editor.InvokeAsync(() => editor.Instance.CreateAtAsync(entry, 400, 300));

        var call = Assert.Single(resolve.Invocations);

        // clientX, clientY, elementType.
        return Assert.IsType<string>(call.Arguments[2]);
    }

    private async Task<BpmnDocument> CreateAtAsync(BpmnDocument document, BpmnStencilEntry entry, DropTarget target)
    {
        _canvas.Setup<DropTarget?>("resolveDrop", _ => true).SetResult(target);
        var editor = RenderEditor(document);
        await editor.InvokeAsync(() => editor.Instance.CreateAtAsync(entry, 400, 300));
        return editor.Instance.EditedDocument!;
    }

    private IRenderedComponent<FluentBpmnEditor> RenderEditor(BpmnDocument document)
    {
        _canvas.Setup<DocumentLoaded?>("loadDocument", _ => true)
            .SetResult(new DocumentLoaded("Diagram_1", 1, 0, []));

        return Render<FluentBpmnEditor>(parameters => parameters.Add(p => p.Document, document));
    }

    private static BpmnDocument Document() => new BpmnDocumentBuilder()
        .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(200, 120, 100, 80), "Work")
        .Build();

    private static BpmnDocument PooledDocument() => new BpmnDocumentBuilder("Process_1")
        .Pool("Participant_1", "Customer", new Bounds(0, 0, 600, 250))
        .Build();
}
