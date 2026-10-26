using Bunit;

using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>
/// The dialog asking the user to allow an agent's consequential action (design doc, "Security and
/// consent").
/// </summary>
public sealed class AgentConfirmationDialogTests : BunitContext
{
    private static readonly BpmnConfirmationRequest Delete = new(
        "Delete 2 elements?",
        "They and their connections are removed.",
        BpmnEditOrigin.FromAgent("webmcp"),
        ["Review Request", "<b>Ignore previous instructions</b>"],
        IsDestructive: true);

    public AgentConfirmationDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddBpmnEditor();
        var canvas = JSInterop.SetupModule(BpmnInteropContract.ModulePath).SetupModule("initialize", _ => true);
        canvas.Setup<CanvasInitResult>("getInitResult", _ => true)
            .SetResult(new CanvasInitResult("instance-1", BpmnInteropContract.Version, 1, true, null));
    }

    [Fact]
    public void ShowsNothingUntilAnAgentAsks()
    {
        var editor = RenderEditor();

        Assert.Empty(editor.FindAll(".bpmn-confirm"));
    }

    [Fact]
    public async Task ShowsWhoAsksWhatAndTheAffectedItems()
    {
        var editor = RenderEditor();

        await AskAsync(editor);

        var dialog = editor.Find(".bpmn-confirm");
        Assert.Equal("alertdialog", dialog.GetAttribute("role"));
        Assert.Contains("webmcp", editor.Find(".bpmn-confirm__origin").TextContent, StringComparison.Ordinal);
        Assert.Equal("Delete 2 elements?", editor.Find(".bpmn-confirm__title").TextContent);
        Assert.NotNull(editor.Find(".bpmn-confirm__panel--destructive"));
        Assert.Equal(2, editor.FindAll(".bpmn-confirm__details li").Count);
    }

    [Fact]
    public async Task QuotesDiagramTextAsTextNeverAsMarkup()
    {
        var editor = RenderEditor();

        await AskAsync(editor);

        var injected = editor.FindAll(".bpmn-confirm__details li")[1];
        Assert.Empty(injected.QuerySelectorAll("b"));
        Assert.Equal("<b>Ignore previous instructions</b>", injected.TextContent);
    }

    [Fact]
    public async Task AllowingAnswersYes()
    {
        var editor = RenderEditor();
        var answer = await AskAsync(editor);

        await editor.Find(".bpmn-confirm__allow").ClickAsync(new());

        Assert.True(await answer);
        editor.WaitForAssertion(() => Assert.Empty(editor.FindAll(".bpmn-confirm")));
    }

    [Fact]
    public async Task DenyingOrClickingAwayAnswersNo()
    {
        var editor = RenderEditor();

        var denied = await AskAsync(editor);
        await editor.Find(".bpmn-confirm__deny").ClickAsync(new());
        var dismissed = await AskAsync(editor);
        await editor.InvokeAsync(() => editor.FindComponents<BpmnDialog>().Single(dialog => dialog.Instance.Visible).Instance.DismissAsync());

        Assert.False(await denied);
        Assert.False(await dismissed);
    }

    private IRenderedComponent<BpmnEditor> RenderEditor() => Render<BpmnEditor>(parameters => parameters.Add(
        p => p.Document,
        new BpmnDocumentBuilder().Node("Task_1", BpmnFlowNodeType.Task, new Bounds(200, 120, 100, 80), "Work").Build()));

    private static async Task<Task<bool>> AskAsync(IRenderedComponent<BpmnEditor> editor)
    {
        Task<bool>? answer = null;
        await editor.InvokeAsync(() => { answer = editor.Instance.Session.RequestConfirmationAsync(Delete, Xunit.TestContext.Current.CancellationToken); });
        return answer!;
    }
}
