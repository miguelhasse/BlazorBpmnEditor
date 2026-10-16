using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// Find usages and safe rename in a browser (implementation plan M6).
/// </summary>
/// <remarks>
/// A rename is a whole-document rewrite, and the half that unit tests cannot show is whether the
/// canvas came back: whether the shape still draws the element under its new id, whether the
/// selection survived, and whether a refused rename leaves the box showing a value the document
/// never accepted.
/// </remarks>
public class ReferenceBrowserTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Fact]
    public async Task ListsEveryPlaceTheElementIsNamed()
    {
        await GoToEditorAsync();
        await ShowPanelAsync();

        var taskId = await TaskIdAsync();
        await SelectAsync(taskId);

        // Two flows and the shape that draws it. The shape is the one a diagram never shows and a
        // rename is most likely to leave behind.
        await Expect(Page.Locator(".bpmn-usages__go")).ToHaveCountAsync(3);
        await Expect(Page.Locator(".bpmn-usages__attribute")).ToContainTextAsync(["targetRef", "sourceRef", "bpmnElement"]);
    }

    [Fact]
    public async Task FollowingAUsageSelectsWhatHoldsIt()
    {
        await GoToEditorAsync();
        await ShowPanelAsync();
        await SelectAsync(await TaskIdAsync());

        await Page.Locator(".bpmn-usages__go").First.ClickAsync();
        await Page.WaitForTimeoutAsync(900);

        // A finding you cannot get to is a finding you cannot act on — the same rule the validation
        // panel follows.
        await Expect(Page.Locator(".bpmn-properties__subtitle")).ToHaveTextAsync("sequenceFlow");
    }

    [Fact]
    public async Task RenamingRewritesTheDiagramAndKeepsTheSelection()
    {
        await GoToEditorAsync();
        await ShowPanelAsync();

        var taskId = await TaskIdAsync();
        await SelectAsync(taskId);

        await Page.Textbox("Element id").FillAsync("Task_Renamed_In_The_Browser");
        await Page.Keyboard.PressAsync("Tab");
        await Page.WaitForTimeoutAsync(1200);

        // The shape draws the element under its new id, and the old id is gone from the graph.
        await Expect(Page.Locator(".joint-cell[model-id='Task_Renamed_In_The_Browser']")).ToHaveCountAsync(1);
        await Expect(Page.Locator($".joint-cell[model-id='{taskId}']")).ToHaveCountAsync(0);

        /*
         * And the element is still selected. The canvas was holding the id the element had a
         * moment ago, so without carrying the selection across, renaming something deselects it —
         * and empties the panel it was renamed in.
         */
        await Expect(Page.Textbox("Element id")).ToHaveValueAsync("Task_Renamed_In_The_Browser");
        await Expect(Page.Locator(".bpmn-usages__go")).ToHaveCountAsync(3);
    }

    [Fact]
    public async Task TheDocumentIsStillValidAfterARename()
    {
        await GoToEditorAsync();
        await ShowPanelAsync();
        await SelectAsync(await TaskIdAsync());

        await Page.Textbox("Element id").FillAsync("Task_Still_Fine");
        await Page.Keyboard.PressAsync("Tab");
        await Page.WaitForTimeoutAsync(1200);

        // A missed reference shows up here: an unresolved sourceRef or a shape drawing nothing.
        await Expect(Page.Locator(".bpmn-validation__clean")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ARefusedRenameSnapsTheBoxBack()
    {
        await GoToEditorAsync();
        await ShowPanelAsync();

        var taskId = await TaskIdAsync();
        var takenId = await AnotherIdAsync(taskId);
        await SelectAsync(taskId);

        Assert.NotEqual(string.Empty, takenId);

        await Page.Textbox("Element id").FillAsync(takenId);
        await Page.Keyboard.PressAsync("Tab");
        await Page.WaitForTimeoutAsync(1200);

        // Section 7's stale-field rule applied to the id: the document refused it, so the box must
        // not go on showing it.
        await Expect(Page.Textbox("Element id")).ToHaveValueAsync(taskId);
        await Expect(Page.Locator($".joint-cell[model-id='{taskId}']")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("EDIT-074");
    }

    [Fact]
    public async Task UndoPutsTheIdBack()
    {
        await GoToEditorAsync();
        await ShowPanelAsync();

        var taskId = await TaskIdAsync();
        await SelectAsync(taskId);

        await Page.Textbox("Element id").FillAsync("Task_Undone");
        await Page.Keyboard.PressAsync("Tab");
        await Page.WaitForTimeoutAsync(1200);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo" }).ClickAsync();
        await Page.WaitForTimeoutAsync(1200);

        await Expect(Page.Locator($".joint-cell[model-id='{taskId}']")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".joint-cell[model-id='Task_Undone']")).ToHaveCountAsync(0);
    }

    // --- helpers ------------------------------------------------------------------

    private Task<string> TaskIdAsync() => FirstIdAsync("Task_");

    private Task<string> FirstIdAsync(string prefix) => Page.EvaluateAsync<string>(
        $$"""
        () => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')]
          .map((c) => c.getAttribute('model-id') ?? '')
          .find((id) => id.startsWith('{{prefix}}')) ?? ''
        """);

    /// <summary>Any other element's id, which is by definition one this document has taken.</summary>
    private Task<string> AnotherIdAsync(string except) => Page.EvaluateAsync<string>(
        $$"""
        () => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')]
          .map((c) => c.getAttribute('model-id') ?? '')
          .find((id) => id.length > 0 && id !== '{{except}}') ?? ''
        """);

    private async Task SelectAsync(string modelId)
    {
        await Page.Locator($".joint-cell[model-id='{modelId}'] [joint-selector='body']").First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);
    }

    private async Task ShowPanelAsync()
    {
        await Page.Locator(".demo-show-properties").CheckAsync();
        await Expect(Page.Locator(".bpmn-properties")).ToBeVisibleAsync();
    }

    private async Task GoToEditorAsync()
    {
        await Page.GotoAsync(app.Url(this));

        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync();
    }
}
