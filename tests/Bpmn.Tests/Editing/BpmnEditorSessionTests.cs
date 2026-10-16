using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class BpmnEditorSessionTests
{
    private static BpmnDocument Document() => new BpmnDocumentBuilder()
        .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(200, 120, 100, 80), "Work")
        .Build();

    private static BpmnEditorSession Session(BpmnDocument? document = null, bool readOnly = false)
    {
        var session = new BpmnEditorSession(new BpmnOptions(), new ServiceCollection().BuildServiceProvider())
        {
            Document = document ?? Document(),
            IsReadOnly = readOnly,
        };
        session.UpdateParameters(EditorConfig.CreateDefault() with { IsReadOnly = readOnly });
        return session;
    }

    [Fact]
    public async Task MutableDescriptionOptionsInvalidateCachedSnapshots()
    {
        var options = new BpmnOptions();
        var session = new BpmnEditorSession(options, new ServiceCollection().BuildServiceProvider()) { Document = Document() };
        session.UpdateParameters(EditorConfig.CreateDefault());
        await session.SetSelectionAsync(["Task_1"]);
        var original = session.Describe("Task_1");
        options.Templates.HonorEntriesVisible = true;
        session.UpdateParameters(session.State.Configuration);
        var changed = session.Describe("Task_1");
        Assert.NotSame(original, changed);
        Assert.Same(changed, session.State.Overlays.ContextualActions.Snapshot);
        options.Extensions.AddZeebe();
        session.UpdateParameters(session.State.Configuration);
        Assert.NotSame(changed, session.Describe("Task_1"));
        Assert.Same(session.Describe("Task_1"), session.State.Overlays.ContextualActions.Snapshot);
    }

    [Fact]
    public async Task RepeatedSelectionSuppressesStateOnlyNotHostCallback()
    {
        var session = Session();
        var callbacks = 0;
        session.OnSelectionChanged = EventCallback.Factory.Create<SelectionChanged>(new object(), _ => callbacks++);
        await session.SetSelectionAsync(["Task_1"]);
        var before = session.State;
        var notifications = 0;
        session.StateChanged += (_, _) => notifications++;
        await session.SetSelectionAsync(["Task_1"]);
        Assert.Same(before, session.State);
        Assert.Equal(0, notifications);
        Assert.Equal(2, callbacks);
    }

    [Fact]
    public async Task ReadingOverridesBeforeUpdatingParametersStillPublishesNewSlices()
    {
        var session = Session();
        await session.SetSelectionAsync(["Task_1"]);
        var original = session.Describe("Task_1")!;
        session.DescribeOverride = _ => original with { Label = "Override" };
        _ = session.Describe("Task_1");
        session.AccessibleGraphOverride = [];
        _ = session.AccessibleGraph;
        session.DiagramsOverride = [];
        _ = session.Diagrams;
        session.UpdateParameters(session.State.Configuration);
        Assert.Equal("Override", session.State.Overlays.ContextualActions.Snapshot!.Label);
        Assert.Empty(session.State.Overlays.AccessibleTree.Nodes);
        Assert.Empty(session.State.Diagrams);
    }

    [Fact]
    public async Task ReadOnlyInspectionInvalidatesDrawnGraphAndExpansionSnapshotOnly()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(200, 120, 300, 200), "Sub")
            .Build();
        var diagram = document.Diagrams[0];
        document = document with
        {
            Diagrams = [diagram with { Plane = diagram.Plane with
        { Shapes = [.. diagram.Plane.Shapes.Select(shape => shape with { IsExpanded = true })] } }]
        };
        var session = Session(document, readOnly: true);
        var before = session.State;
        await session.ToggleExpansionAsync("Sub_1");
        Assert.Same(document, session.State.Document);
        Assert.NotSame(document, session.State.DrawnDocument);
        Assert.True(document.Diagrams[0].Plane.Shapes[0].IsExpanded);
        Assert.False(session.State.DrawnDocument!.Diagrams[0].Plane.Shapes[0].IsExpanded);
        Assert.NotEqual(before.Overlays.AccessibleTree.Nodes, session.State.Overlays.AccessibleTree.Nodes);
        Assert.NotSame(before.Expansions, session.State.Expansions);
        Assert.Single(session.State.Expansions);
        Assert.False(session.State.CanUndo);
        Assert.Equal(before.Diagrams, session.State.Diagrams);
        var expansions = session.State.Expansions;
        await session.HandleViewportChangedAsync(new ViewportChanged(1.1, 0, 0));
        Assert.Same(expansions, session.State.Expansions);
    }

    [Fact]
    public void UnchangedNotificationsKeepStateAndPublishNothing()
    {
        var session = Session();
        var before = session.State;
        var notifications = 0;
        session.StateChanged += (_, _) => notifications++;
        session.HideShortcutHelp();
        session.CancelCalledElement();
        session.UpdateParameters(session.State.Configuration);
        session.UpdateParameters(session.State.Configuration with { });
        Assert.Same(before, session.State);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task ViewportPublishesOnceAndReusesUnchangedSlices()
    {
        var session = Session();
        await session.SetSelectionAsync(["Task_1"]);
        var before = session.State;
        var notifications = 0;
        session.StateChanged += (_, _) => notifications++;
        await session.HandleViewportChangedAsync(new ViewportChanged(1.1, 0, 0));
        await session.HandleViewportChangedAsync(new ViewportChanged(1.1, 10, 20));
        Assert.Equal(1, notifications);
        Assert.Equal(1.1, session.State.Zoom);
        Assert.Same(before.Overlays, session.State.Overlays);
        Assert.Same(before.Overlays.AccessibleTree, session.State.Overlays.AccessibleTree);
        Assert.Same(before.Expansions, session.State.Expansions);
        Assert.Equal(before.Selection, session.State.Selection);
        Assert.Equal(before.CallableDefinitions, session.State.CallableDefinitions);
        Assert.Equal(before.Diagrams, session.State.Diagrams);
        Assert.Equal(before.NavigationPath, session.State.NavigationPath);
    }

    [Fact]
    public async Task EditInvalidatesDocumentDerivedCachesAndLeavesOriginalUntouched()
    {
        var session = Session();
        await session.SetSelectionAsync(["Task_1"]);
        var before = session.State;
        var graph = session.AccessibleGraph;
        var callables = session.CallableDefinitions;
        var diagrams = session.Diagrams;
        var result = await session.ApplyAsync(new UpdatePropertiesCommand("Task_1", "name", "Changed"));
        Assert.NotSame(before.Document, result.Document);
        Assert.Equal("Work", before.Document!.FindFlowNode("Task_1")!.Name);
        Assert.NotSame(graph, session.AccessibleGraph);
        Assert.NotSame(callables, session.CallableDefinitions);
        Assert.NotSame(diagrams, session.Diagrams);
        Assert.NotEqual(before.Overlays.AccessibleTree.Nodes, session.State.Overlays.AccessibleTree.Nodes);
        Assert.Equal("Changed", session.State.Overlays.ContextualActions.Snapshot!.Label);
        Assert.NotSame(before.Overlays.ContextualActions.Snapshot, session.Describe("Task_1"));
        Assert.Same(session.Describe("Task_1"), session.State.Overlays.ContextualActions.Snapshot);
    }

    [Fact]
    public void GraphAndDiagramOverridesInvalidateAndCanBeRemoved()
    {
        var session = Session();
        var graph = session.State.Overlays.AccessibleTree.Nodes;
        var diagrams = session.State.Diagrams;
        session.AccessibleGraphOverride = [new BpmnGraphNode("Other", "task", "Other", "Process_1", null, 1, [], [])];
        session.DiagramsOverride = [];
        session.UpdateParameters(session.State.Configuration);
        Assert.Equal("Other", Assert.Single(session.State.Overlays.AccessibleTree.Nodes).ElementId);
        Assert.Empty(session.State.Diagrams);
        session.AccessibleGraphOverride = null;
        session.DiagramsOverride = null;
        session.UpdateParameters(session.State.Configuration);
        Assert.Equal(graph.Length, session.State.Overlays.AccessibleTree.Nodes.Length);
        Assert.Equal(diagrams.Length, session.State.Diagrams.Length);
    }

    [Fact]
    public async Task DescribeOverrideAndReadOnlyChangesInvalidateSnapshots()
    {
        var session = Session();
        await session.SetSelectionAsync(["Task_1"]);
        var original = session.Describe("Task_1")!;
        session.DescribeOverride = _ => original with { Label = "Override" };
        session.UpdateParameters(session.State.Configuration);
        Assert.Equal("Override", session.State.Overlays.ContextualActions.Snapshot!.Label);
        session.DescribeOverride = null;
        session.IsReadOnly = true;
        session.UpdateParameters(session.State.Configuration);
        Assert.Equal(BpmnElementCapabilities.None, session.State.Overlays.ContextualActions.Snapshot!.Capabilities);
        Assert.NotSame(original, session.Describe("Task_1"));
    }

    [Fact]
    public async Task RepeatedCanvasLoadSuppressesPublicationButKeepsHostCallbacks()
    {
        var session = Session();
        var loaded = new DocumentLoaded("Diagram_1", 1, 0, []);
        var calls = new List<string>();
        session.OnDocumentChanged = EventCallback.Factory.Create<BpmnDocument>(new object(), _ => calls.Add("document"));
        session.OnDocumentLoaded = EventCallback.Factory.Create<DocumentLoaded>(new object(), _ => calls.Add("loaded"));
        session.OnDiagramNavigated = EventCallback.Factory.Create<BpmnDiagramNavigated>(new object(), _ => calls.Add("navigate"));
        await session.HandleDocumentLoadedAsync(loaded);
        calls.Clear();
        var notifications = 0;
        session.StateChanged += (_, _) => { notifications++; calls.Add("state"); };
        await session.ApplyAsync(new UpdatePropertiesCommand("Task_1", "name", "Changed"));
        var edited = session.State;
        await session.HandleDocumentLoadedAsync(loaded);
        Assert.Same(edited, session.State);
        Assert.Equal(1, notifications);
        Assert.Equal(["document", "state", "loaded"], calls);
    }

    [Fact]
    public void TreeVisibilityLabelAndDiagramParametersPublishChanges()
    {
        var session = Session();
        session.ShowAccessibleTree = false;
        session.AccessibleTreeLabel = "Changed tree";
        session.DiagramId = "Missing";
        session.UpdateParameters(session.State.Configuration);
        Assert.False(session.State.Overlays.AccessibleTree.IsOpen);
        Assert.Equal("Changed tree", session.State.Overlays.AccessibleTree.AriaLabel);
        Assert.Equal("Missing", session.State.ActiveDiagramId);
        Assert.Empty(session.State.Overlays.AccessibleTree.Nodes);
    }

    [Fact]
    public async Task ApplyUndoRedoOperateWithoutCanvas()
    {
        var session = Session();
        var original = session.EditedDocument;
        var result = await session.ApplyAsync(new UpdatePropertiesCommand("Task_1", "name", "Changed"));

        Assert.True(result.IsApplied);
        Assert.Equal("Changed", session.State.Document!.FindFlowNode("Task_1")!.Name);
        Assert.True(session.State.CanUndo);
        Assert.False(session.State.CanRedo);
        await session.UndoAsync();
        Assert.Same(original, session.EditedDocument);
        Assert.False(session.State.CanUndo);
        Assert.True(session.State.CanRedo);
        await session.RedoAsync();
        Assert.Same(result.Document, session.EditedDocument);
    }

    [Fact]
    public async Task HostEchoPreservesHistory()
    {
        var session = Session();
        var result = await session.ApplyAsync(new UpdatePropertiesCommand("Task_1", "name", "Changed"));
        session.Document = result.Document;
        session.UpdateParameters(session.State.Configuration);
        Assert.True(session.CanUndo);
        await session.UndoAsync();
        Assert.Equal("Work", session.EditedDocument!.FindFlowNode("Task_1")!.Name);
    }

    [Fact]
    public async Task DifferentHostDocumentStartsNewHistory()
    {
        var session = Session();
        await session.ApplyAsync(new UpdatePropertiesCommand("Task_1", "name", "Changed"));
        await session.SetSelectionAsync(["Task_1"]);
        var replacement = Document();
        session.Document = replacement;
        session.UpdateParameters(session.State.Configuration);
        Assert.Same(replacement, session.EditedDocument);
        Assert.False(session.CanUndo);
        Assert.Empty(session.State.Selection);
    }

    [Fact]
    public async Task ReadOnlyRefusesEditsButKeepsSelection()
    {
        var session = Session(readOnly: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.ApplyAsync(new UpdatePropertiesCommand("Task_1", "name", "Changed")));
        await session.SetSelectionAsync(["Task_1"]);
        Assert.Equal("Task_1", session.AccessibleCurrent);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public async Task SelectionPublishesImmutableWidgetSlices()
    {
        var session = Session();
        var before = session.State;
        var ids = new List<string> { "Task_1" };
        await session.SetSelectionAsync(ids);
        ids.Clear();
        Assert.NotSame(before, session.State);
        Assert.Empty(before.Selection);
        Assert.Equal("Task_1", Assert.Single(session.State.Selection));
        Assert.Equal("Task_1", session.State.Overlays.ContextualActions.Snapshot!.ElementId);
        Assert.Equal("Task_1", session.State.Overlays.AccessibleTree.Current);
        Assert.Single(session.SelectionSnapshots);
    }

    [Fact]
    public async Task StateChangedSeesNewSnapshot()
    {
        var session = Session();
        var before = session.State;
        BpmnEditorState? observed = null;
        session.StateChanged += (_, _) => observed = session.State;
        await session.SetSelectionAsync(["Task_1"]);
        Assert.Same(session.State, observed);
        Assert.NotSame(before, observed);
    }

    [Fact]
    public void ClipboardAndShortcutHelpPublishState()
    {
        var session = Session();
        session.CopyToClipboard(["Task_1"]);
        Assert.True(session.State.CanPaste);
        Assert.False(session.State.Clipboard.IsEmpty);
        session.ShowShortcutHelp();
        Assert.True(session.State.ShortcutHelpVisible);
        session.HideShortcutHelp();
        Assert.False(session.State.ShortcutHelpVisible);
    }

    [Fact]
    public async Task ImportPreviewCancelLeavesDocumentAndHistoryUntouched()
    {
        var session = Session();
        await session.ApplyAsync(new UpdatePropertiesCommand("Task_1", "name", "Changed"));
        var edited = session.EditedDocument;
        var preview = session.PreviewImport("other.bpmn", Xml);
        Assert.NotNull(preview.Document);
        Assert.Same(preview, session.State.PendingImport);
        Assert.Same(edited, session.EditedDocument);
        session.CancelImport();
        Assert.Null(session.State.PendingImport);
        Assert.Same(edited, session.EditedDocument);
        Assert.True(session.CanUndo);
    }

    [Fact]
    public async Task ImportAcceptPersistsWithoutHostEcho()
    {
        var session = Session();
        await session.ApplyAsync(new UpdatePropertiesCommand("Task_1", "name", "Changed"));
        await session.SetSelectionAsync(["Task_1"]);
        await session.GoToDiagramAsync("Diagram_1");
        var preview = session.PreviewImport("other.bpmn", Xml);
        BpmnDocument? published = null;
        session.OnDocumentChanged = EventCallback.Factory.Create<BpmnDocument>(new object(), value => published = value);
        await session.AcceptImportAsync();
        session.UpdateParameters(session.State.Configuration);
        Assert.Same(preview.Document, session.EditedDocument);
        Assert.Same(published, session.State.Document);
        Assert.Null(session.State.PendingImport);
        Assert.Empty(session.State.Selection);
        Assert.Empty(session.State.NavigationPath);
        Assert.False(session.CanUndo);
        Assert.False(session.CanRedo);
        await session.ApplyAsync(new UpdatePropertiesCommand("Imported", "name", "New name"));
        Assert.Equal("New name", session.EditedDocument!.FindFlowNode("Imported")!.Name);
    }

    [Fact]
    public async Task InvalidImportCannotReplaceDocument()
    {
        var session = Session();
        var document = session.EditedDocument;
        session.PreviewImport("bad.bpmn", "<definitions");
        await session.AcceptImportAsync();
        Assert.Same(document, session.EditedDocument);
        Assert.True(session.IsImporting);
        session.CancelImport();
        Assert.False(session.IsImporting);
    }

    [Fact]
    public async Task ExplicitValidationPublishesReport()
    {
        var document = Document();
        var session = Session(document with
        {
            FlowNodes = [.. document.FlowNodes, new BpmnFlowNode("Boundary", BpmnFlowNodeType.BoundaryEvent, "Process_1")],
        });
        session.ValidateOnChange = false;
        var report = await session.ValidateAsync();
        Assert.Same(report, session.State.Validation);
        Assert.NotEmpty(report.Diagnostics);
    }

    [Fact]
    public async Task ValidationTracksEditedDocument()
    {
        var session = Session();
        var before = session.State.Validation;
        await session.ApplyAsync(new UpdatePropertiesCommand("Task_1", "name", "Changed"));
        Assert.NotSame(before, session.State.Validation);
        Assert.Same(session.Validation, session.State.Validation);
    }

    [Fact]
    public async Task NavigationDoesNotEnterHistoryAndSnapshotsStayStable()
    {
        var session = Session();
        var document = session.EditedDocument;
        await session.GoToDiagramAsync("Diagram_1");
        var path = session.NavigationPath;
        await session.GoToDiagramAsync("Another");
        Assert.Equal("Diagram_1", Assert.Single(path).DiagramId);
        Assert.Equal("Another", session.State.ActiveDiagramId);
        Assert.Equal("Another", Assert.Single(session.State.NavigationPath).DiagramId);
        await session.AscendToAsync("Another");
        Assert.Same(document, session.EditedDocument);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public async Task RevealAndAccessibleNavigationWorkWithoutCanvas()
    {
        var session = Session();
        await session.RevealAsync("Task_1");
        Assert.Equal("Task_1", Assert.Single(session.State.Selection));
        await session.SetSelectionAsync([]);
        await session.GoToAccessibleAsync("Task_1");
        Assert.Equal("Task_1", session.State.Overlays.AccessibleTree.Current);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void SecondCanvasCannotAttachUntilFirstDetaches()
    {
        var session = Session();
        var first = new BpmnCanvas();
        var second = new BpmnCanvas();
        session.AttachCanvas(first);
        session.AttachCanvas(first);
        var error = Assert.Throws<InvalidOperationException>(() => session.AttachCanvas(second));
        Assert.Contains("only one", error.Message, StringComparison.Ordinal);
        session.DetachCanvas(second);
        Assert.Throws<InvalidOperationException>(() => session.AttachCanvas(second));
        session.DetachCanvas(first);
        session.AttachCanvas(second);
    }

    [Fact]
    public async Task PropertyRenameKeepsHeadlessSelection()
    {
        var session = Session();
        await session.SetSelectionAsync(["Task_1"]);
        var result = await session.ApplyPropertyCommandAsync(new RenameElementCommand("Task_1", "Renamed"));
        Assert.True(result!.IsApplied);
        Assert.Equal("Renamed", Assert.Single(session.State.Selection));
        Assert.Equal("Renamed", session.State.Overlays.ContextualActions.Snapshot!.ElementId);
    }

    private const string Xml = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                     xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                     id="Defs" targetNamespace="http://example.org">
          <process id="Process"><task id="Imported" name="Imported" /></process>
          <bpmndi:BPMNDiagram id="Diagram">
            <bpmndi:BPMNPlane id="Plane" bpmnElement="Process">
              <bpmndi:BPMNShape id="Shape" bpmnElement="Imported">
                <dc:Bounds x="100" y="100" width="100" height="80" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </definitions>
        """;
}
