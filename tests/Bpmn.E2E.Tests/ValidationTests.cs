using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// The validation panel (specification section 5.2).
/// </summary>
/// <remarks>
/// The rules themselves are tested against documents, where every one of them can be provoked
/// exactly. What only a browser shows is the part that makes a diagnostic useful: that it appears
/// when the document changes, that it says whether the document can still be exported, and that it
/// takes you to the element it is about.
/// </remarks>
public class ValidationTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Fact]
    public async Task ACleanDocumentSaysSo()
    {
        await GoToEditorAsync();

        await Expect(Page.Locator(".bpmn-validation__clean")).ToHaveTextAsync("No problems found");
        await Expect(Page.Locator(".bpmn-validation__go")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AFixtureWithDefectsListsThemByRuleId()
    {
        // pizza-store models 'pizza received' as a throwing Event on the catching end of both an
        // Event-Based Gateway branch and a Message Flow.
        await GoToEditorAsync();
        await SelectSampleAsync("pizza-store.bpmn");

        await Expect(Page.Locator(".bpmn-validation__go")).ToHaveCountAsync(2);
        await Expect(Page.Locator(".bpmn-validation__code").First).ToHaveTextAsync("D006");

        // Section 5.2: an error blocks save and export, and the panel says which state you are in.
        await Expect(Page.Locator(".bpmn-validation__state")).ToHaveTextAsync("Not exportable");
    }

    [Fact]
    public async Task ChoosingAFindingSelectsWhatItIsAbout()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("pizza-store.bpmn");

        await Page.Locator(".bpmn-validation__go").First.ClickAsync();

        // A finding you cannot get to is a finding you cannot fix.
        await Expect(Page.Locator(".joint-highlight-mask")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Selected");
    }

    [Fact]
    public async Task AnEditIsValidatedAndAnUndoTakesTheFindingsBack()
    {
        await GoToEditorAsync();
        await Expect(Page.Locator(".bpmn-validation__clean")).ToBeVisibleAsync();

        // Deleting the Task strands both ends: the Start Event can reach no End Event and the End
        // Event can be reached from no Start Event.
        await DeleteAsync(await TaskIdAsync());

        await Expect(Page.Locator(".bpmn-validation__go")).ToHaveCountAsync(2);
        await Expect(Page.Locator(".bpmn-validation__code").First).ToHaveTextAsync("D009");

        // Reachability is advisory, so the document is still exportable.
        await Expect(Page.Locator(".bpmn-validation__state")).ToHaveTextAsync("Exportable");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo" }).ClickAsync();

        await Expect(Page.Locator(".bpmn-validation__clean")).ToBeVisibleAsync();
    }

    // --- helpers ------------------------------------------------------------------

    private Task<string> TaskIdAsync() => Page.EvaluateAsync<string>(
        """
        () => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')]
          .find((c) => (c.getAttribute('model-id') ?? '').startsWith('Task_'))
          ?.getAttribute('model-id') ?? ''
        """);

    private async Task DeleteAsync(string modelId)
    {
        await Page.Locator($".joint-cell[model-id='{modelId}'] [joint-selector='body']").First.ClickAsync();
        await Page.Keyboard.PressAsync("Delete");
        await Page.WaitForTimeoutAsync(1200);
    }

    private async Task SelectSampleAsync(string fileName)
    {
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Sample diagram" }).ClickAsync();
        await Page.Locator("fluent-option", new() { HasTextString = fileName }).ClickAsync();
        await Page.WaitForTimeoutAsync(1800);
    }

    private async Task GoToEditorAsync()
    {
        await Page.GotoAsync(app.Url(this));

        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync();
        await Expect(Page.Locator(".bpmn-validation")).ToBeVisibleAsync();
    }
}
