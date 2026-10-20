using System.Globalization;

using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// End-to-end verification of the render path in a real browser.
/// </summary>
/// <remarks>
/// This is the only place the whole chain runs together: BPMN XML through the reader, across
/// interop, onto a real JointJS paper backed by a real SVG renderer. The unit suites deliberately
/// stop short of a browser, so nothing else proves the canvas actually draws anything.
/// </remarks>
public class CanvasSmokeTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    private const string CanvasSelector = ".bpmn-canvas__paper > svg";

    [Fact]
    public async Task RendersASampleDiagramOnTheCanvas()
    {
        await GoToEditorAsync();

        // Three shapes and two connectors for simple-process.bpmn.
        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(5);
    }

    [Fact]
    public async Task DrawsPathDataRatherThanFontGlyphs()
    {
        await GoToEditorAsync();

        var svg = await Page.Locator(CanvasSelector).InnerHTMLAsync();

        // The specification is explicit that notation must come from vendored path data, never a
        // webfont, so the shipped output must not depend on bpmn-font being present.
        Assert.DoesNotContain("font-family: bpmn", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bpmn-icon-", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReportsTheRenderedCountsBackToBlazor()
    {
        await GoToEditorAsync();

        // Proves the TypeScript to .NET bridge round-trips, not just that the canvas drew.
        await Expect(Page.Locator(".demo-status")).ToContainTextAsync("Rendered 3 shapes and 2 connectors");
    }

    [Fact]
    public async Task ZoomsInAndOutFromTheToolbar()
    {
        await GoToEditorAsync();

        var initial = await ReadScaleAsync();

        await Button("Zoom in").ClickAsync();
        await Expect(Page.Locator(".bpmn-toolbar__zoom")).Not.ToHaveTextAsync("100%");
        Assert.True(await ReadScaleAsync() > initial);

        await Button("Reset zoom").ClickAsync();
        await Expect(Page.Locator(".bpmn-toolbar__zoom")).ToHaveTextAsync("100%");
    }

    [Fact]
    public async Task FitsTheDiagramToTheViewport()
    {
        await GoToEditorAsync();

        await Button("Fit to content").ClickAsync();

        // Fitting must change the transform; a no-op here means the content bounds were empty.
        await Expect(Page.Locator(".bpmn-toolbar__zoom")).Not.ToHaveTextAsync("100%");
    }

    [Fact]
    public async Task RendersACollaborationWithPoolsAndLanes()
    {
        await GoToEditorAsync();

        await SelectSampleAsync("collaboration.bpmn");

        await Expect(Page.Locator(".bpmn-canvas .bpmn-swimlane-body").First).ToBeVisibleAsync();
        await Expect(Page.Locator(".bpmn-canvas .bpmn-swimlane-header").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task LogsNoConsoleErrors()
    {
        var errors = new List<string>();
        Page.Console += (_, message) =>
        {
            if (message.Type == "error") errors.Add(message.Text);
        };
        Page.PageError += (_, error) => errors.Add(error);

        await Page.GotoAsync(app.Url(this));
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // Asserted on the joined text so a failure reports what actually went wrong in the
        // browser rather than just a count.
        Assert.Equal(string.Empty, string.Join(" | ", errors));
    }

    /// <summary>A button addressed by its accessible name, which both kits share.</summary>
    private ILocator Button(string name) => Page.GetByRole(AriaRole.Button, new() { Name = name });

    /// <summary>
    /// Picks a sample from the Fluent UI select.
    /// </summary>
    /// <remarks>
    /// Not a native select element, so SelectOptionAsync does not apply: the listbox has to be
    /// opened and the option clicked, exactly as a user would.
    /// </remarks>
    private async Task SelectSampleAsync(string fileName)
    {
        await Page.OpenDemoFilesAsync();
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Sample diagram" }).ClickAsync();
        await Page.Locator("fluent-option", new() { HasTextString = fileName }).ClickAsync();
    }

    private async Task GoToEditorAsync()
    {
        await Page.GotoAsync(app.Url(this));

        // The canvas only exists after the WebAssembly client has booted and imported the module.
        await Expect(Page.Locator(CanvasSelector)).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync();
    }

    /// <summary>Reads the paper's current scale from its SVG transform matrix.</summary>
    private async Task<double> ReadScaleAsync()
    {
        var text = await Page.Locator(".bpmn-toolbar__zoom").InnerTextAsync();
        return double.Parse(text.TrimEnd('%'), CultureInfo.InvariantCulture) / 100d;
    }
}
