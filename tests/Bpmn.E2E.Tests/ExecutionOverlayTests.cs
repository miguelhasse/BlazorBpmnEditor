using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// Execution overlays on a real paper (implementation plan M6.5).
/// </summary>
/// <remarks>
/// The demo plays the host here: there is no engine, so it invents a state from whatever sample is
/// loaded and pushes it. What only a browser can show is that the state reaches the SVG at all —
/// that the outlines are really restyled, that the badges are really on the elements, and that the
/// overlay is still there after the canvas has re-rendered underneath it.
/// </remarks>
public class ExecutionOverlayTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Fact]
    public async Task NothingIsDrawnUntilAHostSuppliesAState()
    {
        // The editor does not execute BPMN and never infers a state, so a diagram just opened has
        // no overlay of any kind on it.
        await GoToEditorAsync("order-process.bpmn");

        await Expect(Page.Locator(".bpmn-canvas [class*='bpmn-exec--']")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".bpmn-exec-badges")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task EveryStatusReachesTheOutline()
    {
        await GoToEditorAsync("order-process.bpmn");
        await ShowExecutionAsync();

        // One of each, because the demo hands out one of each: five statuses that a reader has to
        // be able to tell apart is the whole reason they have separate classes.
        await Expect(Page.Locator(".bpmn-canvas .bpmn-exec--active")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".bpmn-canvas .bpmn-exec--completed")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".bpmn-canvas .bpmn-exec--failed")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".bpmn-canvas .bpmn-exec--waiting")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".bpmn-canvas .bpmn-exec--canceled")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task StatusesAreDistinguishableWithoutColour()
    {
        await GoToEditorAsync("order-process.bpmn");
        await ShowExecutionAsync();

        /*
         * Colour is never the only signal. Two statuses that differ only in hue are two statuses a
         * reader with a colour vision deficiency, a greyscale print or a monochrome display cannot
         * separate, so each also carries its own combination of width and dash pattern.
         */
        var strokes = await Page.EvaluateAsync<string[]>(
            """
            () => ['active', 'completed', 'failed', 'canceled', 'waiting'].map((s) => {
              const node = document.querySelector(`.bpmn-canvas .bpmn-exec--${s}`);
              if (node == null) return `${s}=absent`;
              const style = getComputedStyle(node);
              return `${style.strokeWidth}/${style.strokeDasharray}/${style.strokeOpacity}`;
            })
            """);

        Assert.Equal(5, strokes.Length);
        Assert.DoesNotContain(strokes, value => value.Contains("absent", StringComparison.Ordinal));
        Assert.Equal(5, strokes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task TokensAndIncidentsAreDrawnOnTheElement()
    {
        await GoToEditorAsync("order-process.bpmn");
        await ShowExecutionAsync();

        await Expect(Page.Locator(".bpmn-exec-badge--token")).ToHaveCountAsync(2);
        await Expect(Page.Locator(".bpmn-exec-badge--incident")).ToHaveCountAsync(1);

        // Inside the element's own group, so it travels with the shape instead of being positioned
        // against the paper and drifting the moment anything moves or the viewport changes.
        var inside = await Page.EvaluateAsync<bool>(
            "() => [...document.querySelectorAll('.bpmn-exec-badges')].every((g) => g.closest('.joint-cell') != null)");

        Assert.True(inside);
    }

    [Fact]
    public async Task ATokenCounterIsNeverDrawnAtZero()
    {
        await GoToEditorAsync("order-process.bpmn");
        await ShowExecutionAsync();

        // Five elements carry a status; only two carry tokens. A badge reading "0" would be on the
        // other three, and it would be saying something that is not true of them.
        await Expect(Page.Locator(".bpmn-exec-badges")).ToHaveCountAsync(5);

        var counts = await Page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('.bpmn-exec-badge--token .bpmn-exec-badge__count')].map((n) => n.textContent ?? '')");

        Assert.DoesNotContain("0", counts);
    }

    [Fact]
    public async Task EveryDecoratedElementTellsAScreenReaderWhatItIs()
    {
        await GoToEditorAsync("order-process.bpmn");
        await ShowExecutionAsync();

        // The status is drawn as a colour and a dash pattern, and a reader being read to gets
        // neither. If it is not in the title it is not conveyed at all.
        var titles = await Page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('.bpmn-exec-badges > title')].map((t) => t.textContent ?? '')");

        Assert.Equal(5, titles.Length);
        Assert.Contains(titles, title => title.StartsWith("Active", StringComparison.Ordinal));
        Assert.Contains(titles, title => title.Contains("token", StringComparison.Ordinal));
        Assert.Contains(titles, title => title.Contains("incident:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TraversedFlowsAreHighlighted()
    {
        await GoToEditorAsync("order-process.bpmn");
        await ShowExecutionAsync();

        // Only the flows out of what the run has reached: a path that stops where the run did.
        await Expect(Page.Locator(".bpmn-canvas .bpmn-exec-flow--traversed")).ToHaveCountAsync(2);
    }

    [Fact]
    public async Task MotionIsOffUnlessTheHostAsksForIt()
    {
        await GoToEditorAsync("order-process.bpmn");
        await ShowExecutionAsync();

        Assert.Equal("none", await FlowAnimationAsync());

        await Page.SetDemoSwitchAsync("Animate tokens");
        await Page.WaitForTimeoutAsync(500);

        Assert.Equal("bpmn-exec-token", await FlowAnimationAsync());
    }

    [Fact]
    public async Task TheOverlayOutlivesAPlaneSwitch()
    {
        /*
         * The M6 exit criterion, and the reason the canvas keeps the state rather than drawing it
         * once: switching planes re-renders everything, and a host that pushed a state should not
         * have to push it again. The two planes carry different parts of the same state, so this
         * also proves the overlay follows the elements rather than the count.
         */
        await GoToEditorAsync("nested-subprocesses.bpmn");
        await ShowExecutionAsync();

        var root = await OverlayShapeAsync();
        Assert.NotEqual("0/0", root);

        await Page.PickAsync(Page.DiagramSelect(), 1);
        await Page.WaitForTimeoutAsync(1400);

        var nested = await OverlayShapeAsync();
        Assert.NotEqual(root, nested);

        await Page.PickAsync(Page.DiagramSelect(), 0);
        await Page.WaitForTimeoutAsync(1400);

        Assert.Equal(root, await OverlayShapeAsync());
    }

    [Fact]
    public async Task AStateSwitchedOnBeforeLoadingDrawsOnTheLoadedDiagram()
    {
        await GoToEditorAsync("simple-process.bpmn");
        await ShowExecutionAsync();

        await Page.OpenDemoFilesAsync();
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Sample diagram" }).ClickAsync();
        await Page.Locator("fluent-option", new() { HasTextString = "order-process.bpmn" }).ClickAsync();

        await Expect(Page.Locator(".bpmn-canvas .bpmn-exec--active")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".bpmn-canvas .bpmn-exec--failed")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task ARebuiltCanvasKeepsTheStateItWasShowing()
    {
        await GoToEditorAsync("order-process.bpmn");
        await ShowExecutionAsync();
        await Expect(Page.Locator(".bpmn-canvas .bpmn-exec--active")).ToHaveCountAsync(1);

        // A configuration change rebuilds the canvas instance.
        await Page.SetDemoSwitchAsync("Snap to grid");

        await Expect(Page.Locator(".bpmn-canvas .bpmn-exec--active")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".bpmn-canvas .bpmn-exec--failed")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task ClearingRemovesEverything()
    {
        await GoToEditorAsync("order-process.bpmn");
        await ShowExecutionAsync();

        await Page.SetDemoSwitchAsync("Execution state", false);
        await Page.WaitForTimeoutAsync(700);

        // Additive throughout, so removing it must leave the diagram exactly as it was found.
        await Expect(Page.Locator(".bpmn-canvas [class*='bpmn-exec--']")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".bpmn-exec-badges")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".bpmn-canvas .bpmn-exec-flow--traversed")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task SelectionStillWorksUnderTheOverlay()
    {
        await GoToEditorAsync("order-process.bpmn");
        await ShowExecutionAsync();

        // The badges sit in the element's own group, so a pointer landing on one must still reach
        // the element. Clicking the corner a badge occupies is the case that would have failed.
        await Page.Locator(".joint-cell[model-id='Task_ValidateOrder'] [joint-selector='body']").First.ClickAsync();
        await Page.WaitForTimeoutAsync(500);

        await Expect(Page.Locator(".joint-highlight-mask")).ToHaveCountAsync(1);
    }

    // --- helpers ------------------------------------------------------------------

    /// <summary>How many statuses and badges are drawn, as one comparable string.</summary>
    private Task<string> OverlayShapeAsync() => Page.EvaluateAsync<string>(
        """
        () => `${document.querySelectorAll(".bpmn-canvas [class*='bpmn-exec--']").length}`
            + `/${document.querySelectorAll('.bpmn-exec-badges').length}`
        """);

    private Task<string> FlowAnimationAsync() => Page.EvaluateAsync<string>(
        """
        () => {
          const node = document.querySelector('.bpmn-canvas .bpmn-exec-flow--traversed');
          return node == null ? '(absent)' : getComputedStyle(node).animationName;
        }
        """);

    private async Task ShowExecutionAsync()
    {
        await Page.SetDemoSwitchAsync("Execution state");
        await Page.WaitForTimeoutAsync(900);
    }

    private async Task GoToEditorAsync(string sample)
    {
        await Page.GotoAsync(app.Url(this));

        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync();

        await Page.OpenDemoFilesAsync();
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Sample diagram" }).ClickAsync();
        await Page.Locator("fluent-option", new() { HasTextString = sample }).ClickAsync();
        await Page.WaitForTimeoutAsync(1800);
    }
}
