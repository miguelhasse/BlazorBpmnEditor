using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Microsoft.JSInterop;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>
/// Offering an import, and what happens when it is declined (implementation plan M6).
/// </summary>
/// <remarks>
/// The milestone's exit criterion is that a preview "can be accepted/rejected without corrupting
/// the current graph". Accepting is the easy half; these are mostly about the other one.
/// </remarks>
public sealed class ImportOfferTests : BunitContext
{
    private const string InstanceId = "instance-1";

    private readonly BunitJSModuleInterop _module;

    /// <summary>The canvas that <c>initialize</c> returns, on which every later call is made.</summary>
    private readonly BunitJSModuleInterop _canvas;

    public ImportOfferTests()
    {
        JSInterop.Mode = JSRuntimeMode.Strict;
        Services.AddBpmnEditor();
        _module = JSInterop.SetupModule(BpmnInteropContract.ModulePath);
        _module.SetupVoid("showDialog", _ => true).SetVoidResult();
        _module.SetupVoid("closeDialog", _ => true).SetVoidResult();
        _canvas = _module.SetupModule("initialize", _ => true);
        _canvas.Setup<CanvasInitResult>("getInitResult", _ => true)
            .SetResult(new CanvasInitResult(InstanceId, BpmnInteropContract.Version, 1, true, null));
    }

    [Fact]
    public void ShowsNothingUntilThereIsSomethingToOffer()
    {
        var editor = RenderEditor();

        Assert.Empty(editor.FindAll(".bpmn-import"));
        Assert.False(editor.Instance.IsImporting);
    }

    [Fact]
    public async Task DescribesTheFileWithoutOpeningIt()
    {
        var editor = RenderEditor();

        await editor.InvokeAsync(() => editor.Instance.PreviewImport("other.bpmn", Xml("Other_1")));
        editor.Render();

        Assert.True(editor.Instance.IsImporting);
        Assert.Contains("other.bpmn", editor.Find(".bpmn-import__title").TextContent, StringComparison.Ordinal);

        // The document on screen is still the one that was there.
        Assert.NotNull(editor.Instance.EditedDocument!.FindFlowNode("Task_1"));
        Assert.Null(editor.Instance.EditedDocument.FindFlowNode("Other_1"));
    }

    [Fact]
    public async Task DecliningLeavesTheDocumentExactlyAsItWas()
    {
        var editor = RenderEditor();
        var before = editor.Instance.EditedDocument;

        await editor.InvokeAsync(() => editor.Instance.PreviewImport("other.bpmn", Xml("Other_1")));
        editor.Render();

        editor.Find(".bpmn-import__cancel").Click();

        Assert.False(editor.Instance.IsImporting);
        Assert.Empty(editor.FindAll(".bpmn-import"));

        // The same instance, not merely an equal one: nothing was rebuilt, so nothing could have
        // been lost rebuilding it.
        Assert.Same(before, editor.Instance.EditedDocument);
    }

    [Fact]
    public async Task AFileThatWillNotParseSaysSoAndCannotBeOpened()
    {
        var editor = RenderEditor();
        var before = editor.Instance.EditedDocument;

        await editor.InvokeAsync(() => editor.Instance.PreviewImport("notes.txt", "this is not xml at all"));
        editor.Render();

        Assert.NotEmpty(editor.FindAll(".bpmn-import__error"));
        Assert.NotNull(editor.Find(".bpmn-import__accept").GetAttribute("disabled"));
        Assert.Same(before, editor.Instance.EditedDocument);
    }

    [Fact]
    public async Task DecliningAFileThatWillNotParseIsAlsoHarmless()
    {
        // The worst case for an accidental drop: a file that is not BPMN at all, declined.
        var editor = RenderEditor();
        var before = editor.Instance.EditedDocument;

        await editor.InvokeAsync(() => editor.Instance.PreviewImport("notes.txt", "<definitions"));
        editor.Render();
        editor.Find(".bpmn-import__cancel").Click();

        Assert.Same(before, editor.Instance.EditedDocument);
        Assert.False(editor.Instance.IsImporting);
    }

    [Fact]
    public async Task AnEditMadeBeforeAnOfferSurvivesDecliningIt()
    {
        // The case that would actually hurt: unsaved work on screen when the wrong file is dropped.
        var editor = RenderEditor();

        await editor.InvokeAsync(() => editor.Instance.PreviewImport("other.bpmn", Xml("Other_1")));
        editor.Render();
        editor.Find(".bpmn-import__cancel").Click();

        Assert.NotNull(editor.Instance.EditedDocument!.FindFlowNode("Task_1"));
    }

    [Fact]
    public async Task WarnsThatOpeningReplacesWhatIsOnScreen()
    {
        // An import cannot be undone into anything useful, because the history belongs to the
        // document being replaced. Saying so is part of asking the question honestly.
        var editor = RenderEditor();

        await editor.InvokeAsync(() => editor.Instance.PreviewImport("other.bpmn", Xml("Other_1")));
        editor.Render();

        Assert.Contains("replaces", editor.Find(".bpmn-import__note").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysWhatTheReaderChangedOnTheWayIn()
    {
        /*
         * The reader gives an orphan lane set the Pool it lacked, which is the one structural
         * rewrite it performs. Sections 10.6 and 6.1 are why that has to be visible before the
         * file is opened: what makes the rewrite defensible is that it is stated, and the offer
         * is the only place anyone would see it. Nothing is lost, so it is not a "not kept".
         */
        var editor = RenderEditor();

        await editor.InvokeAsync(() => editor.Instance.PreviewImport("lanes.bpmn", PoollessLanes));
        editor.Render();

        var warned = editor.FindAll(".bpmn-import__fact--warn")
            .Select(fact => fact.TextContent)
            .ToList();

        Assert.Contains(warned, text => text.Contains("Changed", StringComparison.Ordinal));
        Assert.Contains(warned, text => text.Contains("1 item", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AndNamesTheChangeRatherThanCountingIt()
    {
        var editor = RenderEditor();

        await editor.InvokeAsync(() => editor.Instance.PreviewImport("lanes.bpmn", PoollessLanes));
        editor.Render();

        var said = editor.Find(".bpmn-import__changed").TextContent;

        Assert.Contains("Process_3", said, StringComparison.Ordinal);
        Assert.Contains("no Pool", said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOrdinaryFileSaysNothingWasChanged()
    {
        // Mutation guard: the row is only useful if it is absent when there is nothing to say.
        var editor = RenderEditor();

        await editor.InvokeAsync(() => editor.Instance.PreviewImport("other.bpmn", Xml("Other_1")));
        editor.Render();

        Assert.Empty(editor.FindAll(".bpmn-import__changed"));
        Assert.DoesNotContain(
            editor.FindAll(".bpmn-import__fact").Select(fact => fact.TextContent),
            text => text.Contains("Changed", StringComparison.Ordinal));
    }

    // --- helpers ------------------------------------------------------------------

    private static string Xml(string taskId) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          id="Definitions_2" targetNamespace="http://example.org">
          <bpmn:process id="Process_2">
            <bpmn:task id="{taskId}" name="Imported" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_2">
            <bpmndi:BPMNPlane id="Plane_2" bpmnElement="Process_2">
              <bpmndi:BPMNShape id="{taskId}_di" bpmnElement="{taskId}">
                <dc:Bounds x="100" y="100" width="100" height="80" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    /// <summary>A file whose Lanes sit on a Process no Participant references.</summary>
    private const string PoollessLanes = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          id="Definitions_3" targetNamespace="http://example.org">
          <bpmn:process id="Process_3">
            <bpmn:laneSet id="LaneSet_3">
              <bpmn:lane id="Lane_3" name="Sales">
                <bpmn:flowNodeRef>Task_3</bpmn:flowNodeRef>
              </bpmn:lane>
            </bpmn:laneSet>
            <bpmn:task id="Task_3" name="Imported" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_3">
            <bpmndi:BPMNPlane id="Plane_3" bpmnElement="Process_3">
              <bpmndi:BPMNShape id="Lane_3_di" bpmnElement="Lane_3" isHorizontal="true">
                <dc:Bounds x="190" y="80" width="600" height="125" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Task_3_di" bpmnElement="Task_3">
                <dc:Bounds x="260" y="100" width="100" height="80" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    private IRenderedComponent<BpmnEditor> RenderEditor()
    {
        _canvas.Setup<DocumentLoaded?>("loadDocument", _ => true)
            .SetResult(new DocumentLoaded("Diagram_1", 1, 0, []));

        return Render<BpmnEditor>(parameters => parameters.Add(p => p.Document, Document()));
    }

    private static BpmnDocument Document() => new BpmnDocumentBuilder()
        .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(200, 120, 100, 80), "Work")
        .Build();
}
