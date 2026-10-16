using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// The size of the drawn DOM for the 602-shape stress diagram (docs/performance.md).
/// </summary>
/// <remarks>
/// Counts, not times: a count is the same on every machine, a frame time is not, so only the count can fail a build without flaking.
/// The budget is the documented one (at most 2,500 SVG nodes) and the measured value at the time of writing is about 1,640 with about 100
/// cells mounted. If a change makes the paper draw much more than the viewport and its margin, this is where it shows.
/// </remarks>
public class VirtualizationBudgetTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    private const int SvgNodeBudget = 2_500;

    [Fact]
    public async Task TheStressDiagramDrawsOnlyWhatIsNearTheViewport()
    {
        await GoToEditorAsync("large-virtualization-stress.bpmn");

        var drawn = await Page.EvaluateAsync<int[]>(
            "() => [document.querySelectorAll('svg *').length, document.querySelectorAll('.joint-element').length]");

        Assert.InRange(drawn[0], 1, SvgNodeBudget);

        // 602 shapes exist; a viewport and its margin hold a fraction of them. Fewer than fifty would mean the diagram did not render.
        Assert.InRange(drawn[1], 50, 300);
    }

    [Fact]
    public async Task EveryElementIsStillInTheAccessibleTreeWhileMostAreNotDrawn()
    {
        await GoToEditorAsync("large-virtualization-stress.bpmn");

        // The reason culling is allowed at all: a screen reader reads the tree, which lists everything.
        await Expect(Page.Locator(".bpmn-a11y-tree__item")).ToHaveCountAsync(602);
    }

    private async Task GoToEditorAsync(string sample)
    {
        await Page.GotoAsync(app.Url(this));

        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Sample diagram" }).ClickAsync();
        await Page.Locator("fluent-option", new() { HasTextString = sample }).ClickAsync();
        await Page.WaitForTimeoutAsync(2000);
    }
}
